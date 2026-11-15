import { getDashboard } from '../api/opportunities.js';
import { describeError, el, formatDate, link, renderSourceStatus, requireElement } from '../components/dom.js';
import { responseTypeLabels } from '../labels.js';
import type { Dashboard } from '../models/api.js';

const frequencyLabels: Record<string, string> = { None: 'No alerts', Immediate: 'Alert after each sync', Daily: 'Daily digest', Weekly: 'Weekly digest' };

function stat(label: string, value: number, href?: string): HTMLElement {
  return el('div', { className: 'stat' }, [el('dt', { text: label }), el('dd', {}, [href ? link(href, String(value)) : String(value)])]);
}

function render(data: Dashboard): void {
  renderSourceStatus(requireElement('#source-status', HTMLElement), data.sources);

  const weekAhead = new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 10);
  const weekAgo = new Date(Date.now() - 7 * 86_400_000).toISOString().slice(0, 10);
  requireElement('#discovery-stats', HTMLElement).replaceChildren(
    stat('Open for response', data.discovery.openOpportunities, '/Opportunities?openOnly=true'),
    stat('Due in the next 7 days', data.discovery.dueWithin7Days, `/Opportunities?openOnly=true&dueBefore=${weekAhead}&sort=DueSoonest`),
    stat('New in the last 7 days', data.discovery.newLast7Days, `/Opportunities?postedFrom=${weekAgo}`),
    stat('Amended in the last 7 days', data.discovery.amendedLast7Days),
  );

  const deadlines = requireElement('#upcoming-deadlines', HTMLElement);
  if (data.upcomingDeadlines.length === 0) {
    deadlines.replaceChildren(el('p', { text: 'No upcoming deadlines.', className: 'muted' }));
  } else {
    deadlines.replaceChildren(
      el(
        'ul',
        { className: 'plain-list' },
        data.upcomingDeadlines.map((d) =>
          el('li', {}, [
            link(`/Opportunities/Detail?id=${encodeURIComponent(d.id)}`, d.title),
            el('div', { className: 'muted', text: `${responseTypeLabels[d.responseType]} · due ${formatDate(d.dueAt)}${d.dueOriginalText ? ` (source: “${d.dueOriginalText}”)` : ''}` }),
            ...(d.isLiveSource ? [] : [el('span', { text: 'Demo fixture', className: 'tag fixture' })]),
          ]),
        ),
      ),
    );
  }

  const searches = requireElement('#saved-search-summary', HTMLElement);
  if (data.savedSearches.length === 0) {
    searches.replaceChildren(el('p', { text: 'No saved searches yet.', className: 'muted' }));
  } else {
    const header = el('tr', {}, ['Search', 'Open matches', 'New (7 days)', 'Alerts'].map((h) => el('th', { text: h, attrs: { scope: 'col' } })));
    const rows = data.savedSearches.map((s) =>
      el('tr', {}, [
        el('td', {}, [link(`/Opportunities?savedSearch=${encodeURIComponent(s.id)}`, s.name)]),
        el('td', { text: String(s.openMatches) }),
        el('td', { text: String(s.newMatchesLast7Days) }),
        el('td', { text: frequencyLabels[s.notificationFrequency] ?? s.notificationFrequency }),
      ]),
    );
    searches.replaceChildren(el('div', { className: 'table-wrap' }, [el('table', {}, [el('thead', {}, [header]), el('tbody', {}, rows)])]));
  }
}

export async function initDashboardPage(): Promise<void> {
  const status = requireElement('#dashboard-status', HTMLElement);
  try {
    render(await getDashboard());
    status.textContent = '';
  } catch (error) {
    status.textContent = describeError(error);
    status.className = 'error';
  }
}
