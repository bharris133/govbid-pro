import { getOpportunity } from '../api/opportunities.js';
import { describeError, el, formatDate, requireElement } from '../components/dom.js';
import { noticeTypeLabels, responseTypeLabels } from '../labels.js';
import type { OpportunityDetail } from '../models/api.js';

function row(label: string, value: string | Node): HTMLElement {
  return el('div', { className: 'detail-row' }, [el('dt', { text: label }), el('dd', {}, [value])]);
}

/** Only http(s) links are rendered as links; anything else is shown as text. */
function sourceLink(url: string | null): Node {
  if (!url) return document.createTextNode('Not provided');
  try {
    const parsed = new URL(url);
    if (parsed.protocol === 'https:' || parsed.protocol === 'http:') {
      return el('a', { text: url, attrs: { href: parsed.toString(), rel: 'noopener noreferrer external', target: '_blank' } });
    }
  } catch {
    // fall through to plain text
  }
  return document.createTextNode(url);
}

export function renderOpportunity(target: HTMLElement, o: OpportunityDetail): void {
  const tags = [
    el('span', { text: o.isLiveSource ? 'Live source' : 'Demo fixture — not live government data', className: o.isLiveSource ? 'tag' : 'tag fixture' }),
    ...(o.isAmended ? [el('span', { text: `Amended (revision ${o.revisionNumber})`, className: 'tag' })] : []),
  ];
  const basis = o.responseTypeBasis === 'UserOverride' ? 'set by a user' : `interpreted by rule ${o.responseTypeRuleVersion ?? 'unknown'}`;
  const place = [o.placeOfPerformanceState, o.placeOfPerformanceCountry].filter(Boolean).join(', ') || 'Not stated';
  const deadline = o.responseDueAt
    ? `${formatDate(o.responseDueAt)} (your local time)${o.responseDueOriginalText ? ` · source text: “${o.responseDueOriginalText}”` : ''}${o.responseDueTimeZone ? ` · source time zone: ${o.responseDueTimeZone}` : ''}`
    : 'Not stated';

  const revisions = el(
    'ol',
    {},
    o.revisions.map((r) => el('li', { text: `Revision ${r.revisionNumber}: ${r.changeKind === 'Created' ? 'first imported' : 'amended'} ${formatDate(r.recordedAt)}${r.sourceUpdatedAt ? ` (source updated ${formatDate(r.sourceUpdatedAt)})` : ''}` })),
  );

  target.replaceChildren(
    el('h1', { text: o.title }),
    el('p', {}, tags),
    el('dl', { className: 'detail' }, [
      row('Solicitation number', o.solicitationNumber ?? 'Not stated'),
      row('Agency', [o.agency, o.office].filter(Boolean).join(' · ') || 'Not stated'),
      row('Notice type (as published)', `${noticeTypeLabels[o.noticeType]}${o.sourceNoticeTypeRaw ? ` (“${o.sourceNoticeTypeRaw}”)` : ''}`),
      row('Response type', `${responseTypeLabels[o.responseType]} — ${basis}`),
      row('NAICS / PSC', `${o.naicsCode ?? '—'} / ${o.pscCode ?? '—'}`),
      row('Set-aside', o.setAside ?? 'None stated'),
      row('Place of performance', place),
      row('Posted', formatDate(o.postedAt)),
      row('Response due', deadline),
      row('Submission instructions', o.submissionInstructions ?? 'Not provided. Follow the solicitation.'),
      row('Source', sourceLink(o.sourceUrl)),
      row('Provenance', `Source “${o.sourceKey}”, first imported ${formatDate(o.firstIngestedAt)}, last retrieved ${formatDate(o.lastRetrievedAt)}`),
    ]),
    el('p', { className: 'notice warning', text: 'The solicitation is authoritative. GovBid Pro does not submit responses; verify the deadline, time zone and submission method at the source.' }),
    el('h2', { text: 'Description' }),
    el('p', { text: o.description ?? 'No description was provided by the source.', className: 'preserve' }),
    el('h2', { text: 'Revision history' }),
    revisions,
  );
}

export async function initOpportunityDetailPage(): Promise<void> {
  const status = requireElement('#detail-status', HTMLElement);
  const target = requireElement('#opportunity-detail', HTMLElement);
  const id = new URLSearchParams(window.location.search).get('id') ?? '';
  try {
    const opportunity = await getOpportunity(id);
    renderOpportunity(target, opportunity);
    document.title = `${opportunity.title} · GovBid Pro`;
    status.textContent = '';
  } catch (error) {
    status.textContent = (error as { status?: number }).status === 404 ? 'This opportunity was not found.' : describeError(error);
    status.className = 'error';
  }
}
