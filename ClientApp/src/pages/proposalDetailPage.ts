import { getMe } from '../api/assessments.js';
import {
  addRequirement,
  addSection,
  changeStatus,
  decideReview,
  deleteRequirement,
  getProposal,
  listMembers,
  requestReview,
  toInput,
  updateProposal,
  updateRequirement,
  updateSection,
} from '../api/proposals.js';
import { describeError, el, formatDate, link, requireElement } from '../components/dom.js';
import { blockedReasonLabels, complianceLabels, responseTypeLabels } from '../labels.js';
import type { ComplianceStatus, Member, ProposalDetail, Requirement, RequirementInput, ResponseType } from '../models/api.js';
import { renderProposalComments } from './proposalComments.js';
import { renderProposalFiles } from './proposalFiles.js';

interface Context {
  id: string;
  me: string;
  members: Member[];
  status: HTMLElement;
  render: (detail: ProposalDetail) => void;
}

const complianceOrder: ComplianceStatus[] = ['NotStarted', 'InProgress', 'NeedsReview', 'Compliant', 'Exception'];

function memberSelect(id: string, members: Member[], selected: string | null, emptyLabel: string): HTMLSelectElement {
  const select = el('select', { attrs: { id } }, [el('option', { text: emptyLabel, attrs: { value: '' } })]);
  for (const m of members) select.append(el('option', { text: `${m.email} (${m.role})`, attrs: { value: m.userId } }));
  select.value = selected ?? '';
  return select;
}

function field(label: string, control: HTMLElement): HTMLElement {
  return el('div', { className: 'field' }, [el('label', { text: label, attrs: { for: control.id } }), control]);
}

async function act(ctx: Context, work: () => Promise<ProposalDetail | null>, done: string): Promise<void> {
  try {
    const detail = await work();
    if (detail) ctx.render(detail);
    else ctx.render(await getProposal(ctx.id));
    ctx.status.textContent = done;
    ctx.status.className = '';
  } catch (error) {
    ctx.status.textContent = describeError(error);
    ctx.status.className = 'error';
  }
}

function overview(d: ProposalDetail, ctx: Context, closed: boolean): HTMLElement {
  const title = el('input', { attrs: { id: 'p-title', maxlength: '300', value: d.title } });
  const type = el('select', { attrs: { id: 'p-type' } }, (Object.keys(responseTypeLabels) as ResponseType[]).map((t) => el('option', { text: responseTypeLabels[t], attrs: { value: t } })));
  type.value = d.responseType;
  const owner = memberSelect('p-owner', ctx.members, d.ownerUserId, 'Choose an owner');
  const save = el('button', { text: 'Save details', attrs: { type: 'submit' } });
  const form = el('form', { className: 'panel', attrs: { id: 'proposal-form' } }, [field('Title', title), field('Response type', type), field('Owner', owner), save]);
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    void act(ctx, () => updateProposal(ctx.id, title.value, type.value as ResponseType, owner.value), 'Details saved.');
  });
  for (const control of [title, type, owner, save]) control.toggleAttribute('disabled', closed);

  const due = d.dueAt ? `${formatDate(d.dueAt)}${d.dueText ? ` (source: “${d.dueText}”)` : ''}` : 'Not stated';
  return el('section', { attrs: { 'aria-labelledby': 'overview-heading' } }, [
    el('h2', { text: 'Overview', attrs: { id: 'overview-heading' } }),
    el('dl', { className: 'detail' }, [
      el('div', { className: 'detail-row' }, [el('dt', { text: 'Opportunity' }), el('dd', {}, [link(`/Opportunities/Detail?id=${encodeURIComponent(d.opportunityId)}`, d.opportunityTitle)])]),
      el('div', { className: 'detail-row' }, [el('dt', { text: 'Solicitation number' }), el('dd', { text: d.solicitationNumber ?? 'Not stated' })]),
      el('div', { className: 'detail-row' }, [el('dt', { text: 'Response due' }), el('dd', { text: due })]),
      el('div', { className: 'detail-row' }, [el('dt', { text: 'Submission instructions' }), el('dd', { text: d.submissionInstructions ?? 'See the solicitation.' })]),
      ...(d.submissionEvidence ? [el('div', { className: 'detail-row' }, [el('dt', { text: 'Submission evidence' }), el('dd', { text: d.submissionEvidence, className: 'preserve' })])] : []),
    ]),
    form,
  ]);
}

function workflow(d: ProposalDetail, ctx: Context): HTMLElement {
  const stages = el(
    'ol',
    { className: 'stages', attrs: { 'aria-label': 'Proposal stages' } },
    d.stages
      .filter((s) => !['Lost', 'Cancelled'].includes(s.status) || s.status === d.status)
      .map((s) => el('li', { text: s.label, className: s.status === d.status ? 'current' : '', attrs: s.status === d.status ? { 'aria-current': 'step' } : {} })),
  );
  const note = el('input', { attrs: { id: 'w-note', maxlength: '1000' } });
  const evidence = el('textarea', { attrs: { id: 'w-evidence', rows: '2', maxlength: '2000' } });
  const buttons = d.nextStages.map((n) => {
    const button = el('button', { text: `Move to ${n.label}`, className: n.status === 'Cancelled' ? 'secondary' : '', attrs: { type: 'button' } });
    button.disabled = !n.allowed;
    if (n.blockedReason) button.title = blockedReasonLabels[n.blockedReason] ?? n.blockedReason;
    button.addEventListener('click', () => void act(ctx, () => changeStatus(ctx.id, n.status, note.value.trim() || null, evidence.value.trim() || null), `Moved to ${n.label}.`));
    return el('li', {}, [button, ...(n.blockedReason ? [el('span', { className: 'muted', text: ` ${blockedReasonLabels[n.blockedReason] ?? n.blockedReason}` })] : [])]);
  });
  const needsEvidence = d.nextStages.some((n) => n.status === 'SubmittedExternally');
  return el('section', { attrs: { 'aria-labelledby': 'workflow-heading' } }, [
    el('h2', { text: 'Status', attrs: { id: 'workflow-heading' } }),
    stages,
    ...(buttons.length > 0
      ? [
          field('Note for the history (optional)', note),
          ...(needsEvidence ? [field('Submission evidence (required to record an external submission: portal, confirmation number, time)', evidence)] : []),
          el('ul', { className: 'plain-list', attrs: { id: 'status-actions' } }, buttons),
        ]
      : [el('p', { className: 'muted', text: 'This proposal is closed.' })]),
  ]);
}

function sections(d: ProposalDetail, ctx: Context, closed: boolean): HTMLElement {
  const rows = d.sections.map((s) => {
    const assignee = memberSelect(`s-assignee-${s.id}`, ctx.members, s.assigneeUserId, 'Unassigned');
    assignee.setAttribute('aria-label', `Author for ${s.title}`);
    const complete = el('input', { attrs: { type: 'checkbox', 'aria-label': `${s.title} complete` } });
    complete.checked = s.isComplete;
    const save = (): void => void act(ctx, () => updateSection(ctx.id, s.id, s.title, assignee.value || null, complete.checked), `Section “${s.title}” saved.`);
    assignee.addEventListener('change', save);
    complete.addEventListener('change', save);
    assignee.disabled = closed;
    complete.disabled = closed;
    return el('tr', {}, [el('td', { text: String(s.order) }), el('th', { text: s.title, attrs: { scope: 'row' } }), el('td', {}, [assignee]), el('td', {}, [complete])]);
  });
  const newTitle = el('input', { attrs: { id: 'new-section', maxlength: '200' } });
  const add = el('form', { className: 'inline-form' }, [el('label', { text: 'New section', attrs: { for: 'new-section' } }), newTitle, el('button', { text: 'Add section', className: 'secondary', attrs: { type: 'submit' } })]);
  add.addEventListener('submit', (event) => {
    event.preventDefault();
    if (newTitle.value.trim()) void act(ctx, () => addSection(ctx.id, newTitle.value.trim()), 'Section added.');
  });
  return el('section', { attrs: { 'aria-labelledby': 'sections-heading' } }, [
    el('h2', { text: 'Sections and assignments', attrs: { id: 'sections-heading' } }),
    el('div', { className: 'table-wrap' }, [
      el('table', { attrs: { id: 'sections-table' } }, [el('thead', {}, [el('tr', {}, ['#', 'Section', 'Author', 'Done'].map((h) => el('th', { text: h, attrs: { scope: 'col' } })))]), el('tbody', {}, rows)]),
    ]),
    ...(closed ? [] : [add]),
  ]);
}

function requirementForm(d: ProposalDetail, ctx: Context): HTMLElement {
  const ref = el('input', { attrs: { id: 'r-reference', maxlength: '64', required: '' } });
  const text = el('textarea', { attrs: { id: 'r-text', rows: '2', maxlength: '4000', required: '' } });
  const locator = el('input', { attrs: { id: 'r-locator', maxlength: '200' } });
  const category = el('input', { attrs: { id: 'r-category', maxlength: '64' } });
  const mandatory = el('input', { attrs: { id: 'r-mandatory', type: 'checkbox' } });
  mandatory.checked = true;
  const section = el('select', { attrs: { id: 'r-section' } }, [el('option', { text: 'Not linked', attrs: { value: '' } }), ...d.sections.map((s) => el('option', { text: s.title, attrs: { value: s.id } }))]);
  const assignee = memberSelect('r-assignee', ctx.members, null, 'Unassigned');
  const form = el('form', { className: 'panel', attrs: { id: 'requirement-form' } }, [
    el('h3', { text: 'Add a requirement' }),
    el('div', { className: 'filters' }, [
      field('Reference (e.g. L.4.2)', ref),
      field('Source location (document, page)', locator),
      field('Category', category),
      field('Response section', section),
      field('Owner', assignee),
    ]),
    field('Requirement text', text),
    el('label', { className: 'check' }, [mandatory, ' Mandatory (“shall” / “must”)']),
    el('button', { text: 'Add requirement', attrs: { type: 'submit' } }),
  ]);
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    const input: RequirementInput = {
      reference: ref.value.trim(),
      text: text.value.trim(),
      sourceLocator: locator.value.trim() || null,
      category: category.value.trim() || null,
      mandatory: mandatory.checked,
      sectionId: section.value || null,
      responseLocation: null,
      assigneeUserId: assignee.value || null,
      evidence: null,
      status: 'NotStarted',
    };
    void act(
      ctx,
      async () => {
        await addRequirement(ctx.id, input);
        return null;
      },
      `Requirement ${input.reference} added.`,
    );
  });
  return form;
}

function matrix(d: ProposalDetail, ctx: Context, closed: boolean): HTMLElement {
  const c = d.compliance;
  const summary = el('p', { attrs: { id: 'compliance-summary' } }, [
    `${c.total} requirements, ${c.mandatory} mandatory. `,
    el('strong', { text: `${c.mandatoryOpen} mandatory still open.` }),
    ` ${c.compliant} compliant, ${c.exceptions} exceptions, ${c.needsReview} need review.`,
  ]);

  const statusSelect = (r: Requirement): HTMLSelectElement => {
    const select = el('select', { attrs: { 'aria-label': `Status of ${r.reference}` } }, complianceOrder.map((s) => el('option', { text: complianceLabels[s], attrs: { value: s } })));
    select.value = r.status;
    select.disabled = closed;
    select.addEventListener('change', () => {
      const next = select.value as ComplianceStatus;
      const input = toInput(r, next);
      if (next === 'Exception' && !input.evidence) {
        const reason = window.prompt(`Explain the exception for ${r.reference}:`);
        if (!reason) {
          select.value = r.status;
          return;
        }
        input.evidence = reason;
      }
      void act(
        ctx,
        async () => {
          await updateRequirement(ctx.id, r.id, input);
          return null;
        },
        `${r.reference} marked ${complianceLabels[next]}.`,
      );
    });
    return select;
  };

  const rows = d.requirements.map((r) => {
    const owner = memberSelect(`r-owner-${r.id}`, ctx.members, r.assigneeUserId, 'Unassigned');
    owner.setAttribute('aria-label', `Owner of ${r.reference}`);
    owner.disabled = closed;
    owner.addEventListener('change', () => void act(ctx, async () => (await updateRequirement(ctx.id, r.id, { ...toInput(r), assigneeUserId: owner.value || null }), null), `${r.reference} reassigned.`));
    const where = el('input', { attrs: { value: r.responseLocation ?? '', maxlength: '200', 'aria-label': `Response location for ${r.reference}` } });
    where.disabled = closed;
    where.addEventListener('change', () => void act(ctx, async () => (await updateRequirement(ctx.id, r.id, { ...toInput(r), responseLocation: where.value.trim() || null }), null), `${r.reference} updated.`));
    const remove = el('button', { text: 'Remove', className: 'secondary', attrs: { type: 'button', 'aria-label': `Remove requirement ${r.reference}` } });
    remove.disabled = closed;
    remove.addEventListener('click', () => {
      if (window.confirm(`Remove requirement ${r.reference}?`)) void act(ctx, async () => (await deleteRequirement(ctx.id, r.id), null), `${r.reference} removed.`);
    });
    return el('tr', { className: r.mandatory && !['Compliant', 'Exception'].includes(r.status) ? 'open-mandatory' : '' }, [
      el('th', { attrs: { scope: 'row' } }, [el('span', { text: r.reference }), ...(r.mandatory ? [el('span', { text: 'Mandatory', className: 'tag' })] : [])]),
      el('td', {}, [el('div', { text: r.text, className: 'preserve' }), el('div', { className: 'muted', text: [r.sourceLocator, r.category].filter(Boolean).join(' · ') }), ...(r.evidence ? [el('div', { text: `Evidence: ${r.evidence}`, className: 'preserve' })] : [])]),
      el('td', { text: r.sectionTitle ?? '—' }),
      el('td', {}, [where]),
      el('td', {}, [owner]),
      el('td', {}, [statusSelect(r)]),
      el('td', {}, [remove]),
    ]);
  });

  return el('section', { attrs: { 'aria-labelledby': 'matrix-heading' } }, [
    el('h2', { text: 'Compliance matrix', attrs: { id: 'matrix-heading' } }),
    summary,
    d.requirements.length === 0
      ? el('p', { className: 'muted', text: 'No requirements yet. Add each “shall” statement from the solicitation’s instructions (Section L), evaluation criteria (Section M) and statement of work.' })
      : el('div', { className: 'table-wrap' }, [
          el('table', { attrs: { id: 'compliance-matrix' } }, [
            el('thead', {}, [el('tr', {}, ['Ref.', 'Requirement', 'Section', 'Where addressed', 'Owner', 'Status', ''].map((h) => el('th', { text: h, attrs: { scope: 'col' } })))]),
            el('tbody', {}, rows),
          ]),
        ]),
    ...(closed ? [] : [requirementForm(d, ctx)]),
  ]);
}

function reviews(d: ProposalDetail, ctx: Context, closed: boolean): HTMLElement {
  const items = d.reviews.map((r) => {
    const outcome = r.outcome === 'Requested' ? 'Waiting for review' : r.outcome === 'Approved' ? 'Approved' : r.outcome === 'ChangesRequested' ? 'Changes requested' : 'Withdrawn';
    const children: (Node | string)[] = [
      el('strong', { text: `${r.reviewer}: ${outcome}` }),
      el('div', { className: 'muted', text: `Requested by ${r.requestedBy} ${formatDate(r.requestedAt)}${r.decidedAt ? ` · answered ${formatDate(r.decidedAt)}` : ''}${r.currentRound ? '' : ' · earlier round'}` }),
    ];
    if (r.instructions) children.push(el('div', { text: `Instructions: ${r.instructions}`, className: 'preserve' }));
    if (r.comment) children.push(el('div', { text: `Comment: ${r.comment}`, className: 'preserve' }));
    if (r.outcome === 'Requested' && r.reviewerUserId === ctx.me && d.status === 'InReview') {
      const comment = el('textarea', { attrs: { id: `rv-comment-${r.id}`, rows: '2', maxlength: '2000' } });
      const approve = el('button', { text: 'Approve', attrs: { type: 'button' } });
      const changes = el('button', { text: 'Request changes', className: 'secondary', attrs: { type: 'button' } });
      approve.addEventListener('click', () => void act(ctx, () => decideReview(ctx.id, r.id, 'Approved', comment.value.trim() || null), 'Review approved.'));
      changes.addEventListener('click', () => void act(ctx, () => decideReview(ctx.id, r.id, 'ChangesRequested', comment.value.trim() || null), 'Changes requested.'));
      children.push(field('Your review comment', comment), el('div', { className: 'inline-form' }, [approve, changes]));
    }
    return el('li', {}, children);
  });

  const reviewer = memberSelect('rv-reviewer', ctx.members.filter((m) => m.userId !== d.ownerUserId), null, 'Choose a reviewer');
  const instructions = el('input', { attrs: { id: 'rv-instructions', maxlength: '2000' } });
  const request = el('form', { className: 'panel', attrs: { id: 'review-form' } }, [
    el('h3', { text: 'Request a review' }),
    field('Reviewer', reviewer),
    field('What to focus on (optional)', instructions),
    el('button', { text: 'Request review', attrs: { type: 'submit' } }),
  ]);
  request.addEventListener('submit', (event) => {
    event.preventDefault();
    if (reviewer.value) void act(ctx, () => requestReview(ctx.id, reviewer.value, instructions.value.trim() || null), 'Review requested.');
  });

  return el('section', { attrs: { 'aria-labelledby': 'reviews-heading' } }, [
    el('h2', { text: 'Reviews and approval', attrs: { id: 'reviews-heading' } }),
    items.length === 0 ? el('p', { className: 'muted', text: 'No reviews requested yet.' }) : el('ul', { className: 'plain-list', attrs: { id: 'review-list' } }, items),
    ...(d.status === 'InReview' && !closed ? [request] : [el('p', { className: 'muted', text: 'Move the proposal to review to request reviewers.' })]),
  ]);
}

function history(d: ProposalDetail): HTMLElement {
  return el('section', { attrs: { 'aria-labelledby': 'history-heading' } }, [
    el('h2', { text: 'History', attrs: { id: 'history-heading' } }),
    d.history.length === 0
      ? el('p', { className: 'muted', text: `Created ${formatDate(d.createdAt)}.` })
      : el('ol', { className: 'plain-list', attrs: { id: 'proposal-history' } }, d.history.map((h) => el('li', { text: `${formatDate(h.occurredAt)}: ${h.actor} moved it from ${h.fromLabel} to ${h.toLabel}${h.note ? ` — “${h.note}”` : ''}` }))),
  ]);
}

export async function initProposalDetailPage(): Promise<void> {
  const status = requireElement('#proposal-status', HTMLElement);
  const target = requireElement('#proposal', HTMLElement);
  const id = new URLSearchParams(window.location.search).get('id') ?? '';
  try {
    const [detail, me, members] = await Promise.all([getProposal(id), getMe(), listMembers()]);
    const ctx: Context = {
      id,
      me: me.userId,
      members,
      status,
      render: (d) => {
        const closed = ['Won', 'Lost', 'Cancelled'].includes(d.status);
        document.title = `${d.title} · GovBid Pro`;
        const extra = el('div', { attrs: { id: 'proposal-extra' } });
        const discussion = el('div', { attrs: { id: 'proposal-discussion' } });
        target.replaceChildren(
          el('h1', { text: d.title }),
          el('p', {}, [el('span', { text: d.statusLabel, className: 'tag' }), el('span', { text: responseTypeLabels[d.responseType], className: 'tag' }), ...(d.opportunityIsLive ? [] : [el('span', { text: 'Demo fixture', className: 'tag fixture' })])]),
          el('ul', { className: 'notice warning', attrs: { id: 'proposal-warnings' } }, d.warnings.map((w) => el('li', { text: w }))),
          workflow(d, ctx),
          overview(d, ctx, closed),
          sections(d, ctx, closed),
          matrix(d, ctx, closed),
          reviews(d, ctx, closed),
          history(d),
          extra,
          discussion,
        );
        void renderProposalFiles(extra, d, closed);
        void renderProposalComments(discussion, d);
        document.dispatchEvent(new CustomEvent('proposal:rendered', { detail: d }));
      },
    };
    ctx.render(detail);
    status.textContent = '';
  } catch (error) {
    status.textContent = (error as { status?: number }).status === 404 ? 'This proposal was not found.' : describeError(error);
    status.className = 'error';
  }
}
