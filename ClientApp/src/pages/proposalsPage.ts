import { listProposals } from '../api/proposals.js';
import { describeError, el, formatDate, link, requireElement } from '../components/dom.js';
import { responseTypeLabels } from '../labels.js';
import type { ProposalSummary } from '../models/api.js';

function render(container: HTMLElement, proposals: ProposalSummary[]): void {
  if (proposals.length === 0) {
    container.replaceChildren(
      el('p', { className: 'muted' }, ['No proposals yet. Open an opportunity and choose “Start a response”. ', link('/Opportunities', 'Find opportunities')]),
    );
    return;
  }
  const header = el('tr', {}, ['Proposal', 'Type', 'Status', 'Owner', 'Response due', 'Mandatory items open', 'Updated'].map((h) => el('th', { text: h, attrs: { scope: 'col' } })));
  const rows = proposals.map((p) =>
    el('tr', {}, [
      el('td', {}, [link(`/Proposals/Detail?id=${encodeURIComponent(p.id)}`, p.title), el('div', { className: 'muted', text: p.opportunityTitle })]),
      el('td', { text: responseTypeLabels[p.responseType] }),
      el('td', {}, [el('span', { text: p.statusLabel, className: 'tag' })]),
      el('td', { text: p.owner }),
      el('td', { text: formatDate(p.dueAt) }),
      el('td', { text: `${p.compliance.mandatoryOpen} of ${p.compliance.mandatory}` }),
      el('td', { text: formatDate(p.updatedAt) }),
    ]),
  );
  container.replaceChildren(el('div', { className: 'table-wrap' }, [el('table', {}, [el('thead', {}, [header]), el('tbody', {}, rows)])]));
}

export async function initProposalsPage(): Promise<void> {
  const container = requireElement('#proposals', HTMLElement);
  const includeClosed = requireElement('#include-closed', HTMLInputElement);
  const refresh = async (): Promise<void> => {
    try {
      render(container, await listProposals(includeClosed.checked));
    } catch (error) {
      container.replaceChildren(el('p', { text: describeError(error), className: 'error' }));
    }
  };
  includeClosed.addEventListener('change', () => void refresh());
  await refresh();
}
