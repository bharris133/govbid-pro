import { getFit, listAssessments, recordAssessment } from '../api/assessments.js';
import { describeError, el, formatDate } from '../components/dom.js';
import { decisionLabels, fitBandLabels, fitOutcomeLabels } from '../labels.js';
import type { Assessment, FitResult, GoNoGoDecision } from '../models/api.js';

function fitTable(fit: FitResult): HTMLElement {
  const header = el('tr', {}, ['Criterion', 'Result', 'Points', 'Why'].map((h) => el('th', { text: h, attrs: { scope: 'col' } })));
  const rows = fit.criteria.map((c) =>
    el('tr', {}, [
      el('th', { text: c.label, attrs: { scope: 'row' } }),
      el('td', { text: fitOutcomeLabels[c.outcome] }),
      el('td', { text: c.outcome === 'NotEvaluated' ? '—' : `${c.points} of ${c.weight}` }),
      el('td', { text: c.explanation }),
    ]),
  );
  return el('div', { className: 'table-wrap' }, [el('table', {}, [el('caption', { text: `How the score was calculated (rules ${fit.ruleVersion})` }), el('thead', {}, [header]), el('tbody', {}, rows)])]);
}

function historyList(history: Assessment[], currentRevision: number): HTMLElement {
  if (history.length === 0) return el('p', { text: 'No assessment has been recorded for this opportunity yet.', className: 'muted' });
  return el(
    'ol',
    { className: 'plain-list', attrs: { reversed: '' } },
    history.map((a) =>
      el('li', {}, [
        el('strong', { text: `${decisionLabels[a.decision]} · score ${a.score ?? 'not scored'}` }),
        el('div', { className: 'muted', text: `${a.createdBy}, ${formatDate(a.createdAt)} · scored notice revision ${a.opportunityRevisionNumber}${a.opportunityRevisionNumber < currentRevision ? ' (the notice has been amended since)' : ''}` }),
        ...(a.rationale ? [el('div', { text: a.rationale, className: 'preserve' })] : []),
      ]),
    ),
  );
}

/** Fit score, Go/No-go form and decision history for one opportunity (P1.3). */
export async function renderAssessmentPanel(target: HTMLElement, opportunityId: string, currentRevision: number): Promise<void> {
  const scoreBox = el('div', { attrs: { id: 'fit-score' } });
  const historyBox = el('div', { attrs: { id: 'assessment-history' } });
  const status = el('p', { attrs: { id: 'assessment-status', role: 'status', 'aria-live': 'polite' } });

  const decision = el('select', { attrs: { id: 'a-decision', name: 'decision' } }, [
    el('option', { text: 'Go — pursue this opportunity', attrs: { value: 'Go' } }),
    el('option', { text: 'No-go — do not pursue', attrs: { value: 'NoGo' } }),
    el('option', { text: 'Watch — decide later', attrs: { value: 'Watch' } }),
    el('option', { text: 'Save the score only', attrs: { value: 'Undecided' } }),
  ]);
  const rationale = el('textarea', { attrs: { id: 'a-rationale', name: 'rationale', rows: '3', maxlength: '2000' } });
  const form = el('form', { className: 'panel', attrs: { id: 'assessment-form' } }, [
    el('h3', { text: 'Record a decision' }),
    el('div', { className: 'field' }, [el('label', { text: 'Decision', attrs: { for: 'a-decision' } }), decision]),
    el('div', { className: 'field' }, [el('label', { text: 'Rationale (required for Go and No-go)', attrs: { for: 'a-rationale' } }), rationale]),
    el('button', { text: 'Record decision', attrs: { type: 'submit' } }),
    status,
  ]);

  const refresh = async (): Promise<void> => {
    try {
      const [fit, history] = await Promise.all([getFit(opportunityId), listAssessments(opportunityId)]);
      scoreBox.replaceChildren(
        el('p', { className: 'score' }, [
          el('strong', { text: fit.score === null ? 'Not scored' : `${fit.score} / 100` }),
          el('span', { text: ` ${fitBandLabels[fit.band] ?? fit.band}`, className: 'tag' }),
        ]),
        fitTable(fit),
        el('ul', { className: 'muted' }, fit.cautions.map((c) => el('li', { text: c }))),
        ...(fit.criteria.some((c) => c.outcome === 'NotEvaluated') ? [el('p', {}, [el('a', { text: 'Complete your company profile', attrs: { href: '/Company' } })])] : []),
      );
      historyBox.replaceChildren(historyList(history, currentRevision));
    } catch (error) {
      scoreBox.replaceChildren(el('p', { text: describeError(error), className: 'error' }));
    }
  };

  form.addEventListener('submit', (event) => {
    event.preventDefault();
    void (async () => {
      try {
        await recordAssessment(opportunityId, decision.value as GoNoGoDecision, rationale.value.trim());
        status.textContent = 'Decision recorded.';
        rationale.value = '';
        await refresh();
      } catch (error) {
        const reason = (error as { reasonCode?: string }).reasonCode;
        status.textContent = reason === 'role.insufficient' ? 'Your role cannot record this decision. Ask a team manager or company administrator.' : describeError(error);
      }
    })();
  });

  target.replaceChildren(
    el('section', { attrs: { 'aria-labelledby': 'fit-heading' } }, [el('h2', { text: 'Fit for your company', attrs: { id: 'fit-heading' } }), scoreBox]),
    el('section', { attrs: { 'aria-labelledby': 'decision-heading' } }, [el('h2', { text: 'Go / No-go', attrs: { id: 'decision-heading' } }), form, el('h3', { text: 'Decision history' }), historyBox]),
  );
  await refresh();
}
