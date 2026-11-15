import { listLatestAssessments } from '../api/assessments.js';
import { createSavedSearch, deleteSavedSearch, listSavedSearches, searchOpportunities } from '../api/opportunities.js';
import { dateInputToIso, describeError, el, formatDate, link, renderSourceStatus, requireElement } from '../components/dom.js';
import { decisionLabels, noticeTypeLabels, responseTypeLabels } from '../labels.js';
import type {
  GoNoGoDecision,
  NotificationFrequency,
  OpportunitySearchCriteria,
  OpportunitySearchResult,
  OpportunitySort,
  ResponseType,
  SavedSearch,
  SourceNoticeType,
} from '../models/api.js';

function readCriteria(form: HTMLFormElement): OpportunitySearchCriteria {
  const data = new FormData(form);
  const text = (name: string): string => String(data.get(name) ?? '').trim();
  const keywords = text('keywords');
  const naics = text('naics');
  const agency = text('agency');
  const state = text('state').toUpperCase();
  const noticeType = text('noticeType');
  const responseType = text('responseType');
  const setAside = text('setAside');
  return {
    keywords: keywords || null,
    naicsCodes: naics ? [naics] : [],
    agencies: agency ? [agency] : [],
    placeOfPerformanceStates: state ? [state] : [],
    noticeTypes: noticeType ? [noticeType as SourceNoticeType] : [],
    responseTypes: responseType ? [responseType as ResponseType] : [],
    setAsides: setAside ? [setAside] : [],
    postedFrom: dateInputToIso(text('postedFrom')),
    dueBefore: dateInputToIso(text('dueBefore'), true),
    openOnly: data.get('openOnly') === 'true',
  };
}

function readSort(form: HTMLFormElement): OpportunitySort {
  return new FormData(form).get('sort') === 'DueSoonest' ? 'DueSoonest' : 'PostedNewest';
}

function setField(form: HTMLFormElement, name: string, value: string): void {
  const field = form.elements.namedItem(name);
  if (field instanceof HTMLInputElement && field.type === 'checkbox') {
    field.checked = value === 'true';
  } else if (field instanceof HTMLInputElement || field instanceof HTMLSelectElement) {
    field.value = value;
  }
}

/** Fills the filter form from saved criteria so a saved search can be re-run and adjusted. */
function applyCriteria(form: HTMLFormElement, criteria: OpportunitySearchCriteria): void {
  form.reset();
  setField(form, 'keywords', criteria.keywords ?? '');
  setField(form, 'naics', criteria.naicsCodes[0] ?? '');
  setField(form, 'agency', criteria.agencies[0] ?? '');
  setField(form, 'state', criteria.placeOfPerformanceStates[0] ?? '');
  setField(form, 'noticeType', criteria.noticeTypes[0] ?? '');
  setField(form, 'responseType', criteria.responseTypes[0] ?? '');
  setField(form, 'setAside', criteria.setAsides[0] ?? '');
  setField(form, 'postedFrom', (criteria.postedFrom ?? '').slice(0, 10));
  setField(form, 'dueBefore', (criteria.dueBefore ?? '').slice(0, 10));
  setField(form, 'openOnly', criteria.openOnly ? 'true' : 'false');
}

/** Applies simple query-string filters (links from the dashboard). */
function applyQueryString(form: HTMLFormElement, params: URLSearchParams): void {
  for (const name of ['keywords', 'naics', 'agency', 'state', 'noticeType', 'responseType', 'setAside', 'postedFrom', 'dueBefore', 'openOnly', 'sort']) {
    const value = params.get(name);
    if (value !== null) setField(form, name, value);
  }
}

function renderResults(summary: HTMLElement, container: HTMLElement, result: OpportunitySearchResult, decisions: Map<string, GoNoGoDecision>): void {
  const { items, totalCount, page, pageSize } = result.results;
  const first = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const last = Math.min(totalCount, page * pageSize);
  summary.textContent = `${totalCount} matching ${totalCount === 1 ? 'opportunity' : 'opportunities'}${totalCount > 0 ? `, showing ${first}–${last}` : ''}.`;
  if (items.length === 0) {
    container.replaceChildren(el('p', { text: 'No opportunities match these filters.', className: 'muted' }));
    return;
  }
  const header = el('tr', {}, ['Title', 'Notice type (source)', 'Response type', 'NAICS', 'Response due', 'Your decision', 'Source'].map((h) => el('th', { text: h, attrs: { scope: 'col' } })));
  const rows = items.map((o) =>
    el('tr', {}, [
      el('td', {}, [
        el('strong', {}, [link(`/Opportunities/Detail?id=${encodeURIComponent(o.id)}`, o.title)]),
        el('div', { text: [o.solicitationNumber, o.agency].filter(Boolean).join(' · '), className: 'muted' }),
      ]),
      el('td', { text: `${noticeTypeLabels[o.noticeType]}${o.sourceNoticeTypeRaw ? ` (“${o.sourceNoticeTypeRaw}”)` : ''}` }),
      el('td', { text: responseTypeLabels[o.responseType] }),
      el('td', { text: o.naicsCode ?? '—' }),
      el('td', { text: formatDate(o.responseDueAt) }),
      el('td', { text: decisions.has(o.id) ? decisionLabels[decisions.get(o.id) ?? 'Undecided'] : '—' }),
      el('td', {}, [
        el('span', { text: o.isLiveSource ? 'Live' : 'Demo fixture', className: o.isLiveSource ? 'tag' : 'tag fixture' }),
        ...(o.isAmended ? [el('span', { text: 'Amended', className: 'tag' })] : []),
      ]),
    ]),
  );
  const table = el('table', {}, [el('thead', {}, [header]), el('tbody', {}, rows)]);
  container.replaceChildren(el('div', { className: 'table-wrap' }, [table]));
}

function renderPagination(nav: HTMLElement, result: OpportunitySearchResult, go: (page: number) => void): void {
  const { page, pageSize, totalCount } = result.results;
  const pages = Math.max(1, Math.ceil(totalCount / pageSize));
  nav.replaceChildren();
  if (pages <= 1) return;
  const button = (text: string, target: number, disabled: boolean): HTMLButtonElement => {
    const b = el('button', { text, className: 'secondary', attrs: { type: 'button' } });
    b.disabled = disabled;
    b.addEventListener('click', () => go(target));
    return b;
  };
  nav.append(button('Previous page', page - 1, page <= 1), el('span', { text: `Page ${page} of ${pages}` }), button('Next page', page + 1, page >= pages));
}

function renderSaved(list: HTMLUListElement, searches: SavedSearch[], onRun: (search: SavedSearch) => void, onDelete: (id: string) => void): void {
  list.replaceChildren();
  if (searches.length === 0) {
    list.append(el('li', { text: 'No saved searches yet.', className: 'muted' }));
    return;
  }
  for (const search of searches) {
    const run = el('button', { text: search.name, className: 'link-button', attrs: { type: 'button', 'aria-label': `Run saved search ${search.name}` } });
    run.addEventListener('click', () => onRun(search));
    const remove = el('button', { text: 'Delete', className: 'secondary', attrs: { type: 'button', 'aria-label': `Delete saved search ${search.name}` } });
    remove.addEventListener('click', () => onDelete(search.id));
    const alerts = search.notificationFrequency === 'None' ? '' : ` (${search.notificationFrequency.toLowerCase()} alerts) `;
    list.append(el('li', {}, [run, el('span', { text: ` ${alerts}`, className: 'muted' }), remove]));
  }
}

export async function initOpportunitiesPage(): Promise<void> {
  const filters = requireElement('#opportunity-filters', HTMLFormElement);
  const saveForm = requireElement('#save-search', HTMLFormElement);
  const saveStatus = requireElement('#save-status', HTMLElement);
  const savedList = requireElement('#saved-searches', HTMLUListElement);
  const sourceStatus = requireElement('#source-status', HTMLElement);
  const summary = requireElement('#results-summary', HTMLElement);
  const results = requireElement('#results', HTMLElement);
  const pagination = requireElement('#pagination', HTMLElement);
  const params = new URLSearchParams(window.location.search);
  let saved: SavedSearch[] = [];
  const decisions = new Map<string, GoNoGoDecision>();
  try {
    for (const a of await listLatestAssessments()) decisions.set(a.opportunityId, a.decision);
  } catch {
    // Decisions are an enhancement to the list; the search still works without them.
  }

  const runSearch = async (page = 1): Promise<void> => {
    summary.textContent = 'Searching…';
    try {
      const result = await searchOpportunities(readCriteria(filters), page, readSort(filters));
      renderSourceStatus(sourceStatus, result.sources);
      renderResults(summary, results, result, decisions);
      renderPagination(pagination, result, (target) => void runSearch(target));
    } catch (error) {
      summary.textContent = describeError(error);
      results.replaceChildren();
      pagination.replaceChildren();
    }
  };

  const runSaved = (search: SavedSearch): void => {
    applyCriteria(filters, search.criteria);
    saveStatus.textContent = `Showing saved search “${search.name}”.`;
    void runSearch();
  };

  const refreshSaved = async (): Promise<void> => {
    try {
      saved = await listSavedSearches();
      renderSaved(savedList, saved, runSaved, (id) => void removeSaved(id));
    } catch (error) {
      savedList.replaceChildren(el('li', { text: describeError(error), className: 'error' }));
    }
  };

  const removeSaved = async (id: string): Promise<void> => {
    try {
      await deleteSavedSearch(id);
      saveStatus.textContent = 'Saved search deleted.';
      await refreshSaved();
    } catch (error) {
      saveStatus.textContent = describeError(error);
    }
  };

  filters.addEventListener('submit', (event) => {
    event.preventDefault();
    void runSearch();
  });
  filters.addEventListener('reset', () => {
    // Run after the browser has cleared the fields.
    window.setTimeout(() => void runSearch(), 0);
  });

  saveForm.addEventListener('submit', (event) => {
    event.preventDefault();
    const data = new FormData(saveForm);
    const name = String(data.get('name') ?? '').trim();
    const frequency = String(data.get('notificationFrequency') ?? 'None') as NotificationFrequency;
    if (!name) return;
    void (async () => {
      try {
        await createSavedSearch(name, readCriteria(filters), frequency);
        saveStatus.textContent = `Saved “${name}”.`;
        saveForm.reset();
        await refreshSaved();
      } catch (error) {
        saveStatus.textContent = describeError(error);
      }
    })();
  });

  applyQueryString(filters, params);
  await refreshSaved();
  const requested = saved.find((s) => s.id === params.get('savedSearch'));
  if (requested) {
    runSaved(requested);
  } else {
    await runSearch();
  }
}
