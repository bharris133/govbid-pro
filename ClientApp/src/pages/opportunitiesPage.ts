import { createSavedSearch, deleteSavedSearch, listSavedSearches, searchOpportunities } from '../api/opportunities.js';
import { describeError, el, formatDate, requireElement } from '../components/dom.js';
import { noticeTypeLabels, responseTypeLabels } from '../labels.js';
import type {
  OpportunitySearchCriteria,
  OpportunitySearchResult,
  ResponseType,
  SavedSearch,
  SourceFreshness,
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
  return {
    keywords: keywords || null,
    naicsCodes: naics ? [naics] : [],
    agencies: agency ? [agency] : [],
    placeOfPerformanceStates: state ? [state] : [],
    noticeTypes: noticeType ? [noticeType as SourceNoticeType] : [],
    responseTypes: responseType ? [responseType as ResponseType] : [],
    setAsides: [],
  };
}

function renderSources(target: HTMLElement, sources: SourceFreshness[]): void {
  target.replaceChildren();
  if (sources.length === 0) {
    target.textContent = 'No opportunity source has been synchronized yet.';
    return;
  }
  const anyFixture = sources.some((s) => !s.isLiveSource);
  target.classList.toggle('fixture', anyFixture);
  for (const source of sources) {
    const label = source.isLiveSource ? 'Live source' : 'DEMO FIXTURE DATA — not live government data';
    const error = source.lastErrorCode ? ` Last error: ${source.lastErrorCode}.` : '';
    target.append(el('p', { text: `${label} (${source.sourceKey}). Last updated ${formatDate(source.lastSuccessfulSyncAt)}.${error}` }));
  }
}

function renderResults(summary: HTMLElement, container: HTMLElement, result: OpportunitySearchResult): void {
  const { items, totalCount } = result.results;
  summary.textContent = `${totalCount} matching ${totalCount === 1 ? 'opportunity' : 'opportunities'}.`;
  if (items.length === 0) {
    container.replaceChildren(el('p', { text: 'No opportunities match these filters.', className: 'muted' }));
    return;
  }
  const header = el('tr', {}, ['Title', 'Notice type (source)', 'Response type', 'NAICS', 'Response due', 'Source'].map((h) => el('th', { text: h, attrs: { scope: 'col' } })));
  const rows = items.map((o) =>
    el('tr', {}, [
      el('td', {}, [el('strong', { text: o.title }), el('div', { text: o.solicitationNumber ?? '', className: 'muted' })]),
      el('td', { text: `${noticeTypeLabels[o.noticeType]}${o.sourceNoticeTypeRaw ? ` (“${o.sourceNoticeTypeRaw}”)` : ''}` }),
      el('td', { text: responseTypeLabels[o.responseType] }),
      el('td', { text: o.naicsCode ?? '—' }),
      el('td', { text: formatDate(o.responseDueAt) }),
      el('td', {}, [
        el('span', { text: o.isLiveSource ? 'Live' : 'Demo fixture', className: o.isLiveSource ? 'tag' : 'tag fixture' }),
        ...(o.isAmended ? [el('span', { text: 'Amended', className: 'tag' })] : []),
      ]),
    ]),
  );
  const table = el('table', {}, [el('thead', {}, [header]), el('tbody', {}, rows)]);
  container.replaceChildren(el('div', { className: 'table-wrap' }, [table]));
}

function renderSaved(list: HTMLUListElement, searches: SavedSearch[], onDelete: (id: string) => void): void {
  list.replaceChildren();
  if (searches.length === 0) {
    list.append(el('li', { text: 'No saved searches yet.', className: 'muted' }));
    return;
  }
  for (const search of searches) {
    const remove = el('button', { text: 'Delete', className: 'secondary', attrs: { type: 'button', 'aria-label': `Delete saved search ${search.name}` } });
    remove.addEventListener('click', () => onDelete(search.id));
    list.append(el('li', {}, [el('span', { text: `${search.name} ` }), remove]));
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

  const refreshSaved = async (): Promise<void> => {
    try {
      renderSaved(savedList, await listSavedSearches(), (id) => void removeSaved(id));
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

  const runSearch = async (): Promise<void> => {
    summary.textContent = 'Searching…';
    try {
      const result = await searchOpportunities(readCriteria(filters));
      renderSources(sourceStatus, result.sources);
      renderResults(summary, results, result);
    } catch (error) {
      summary.textContent = describeError(error);
      results.replaceChildren();
    }
  };

  filters.addEventListener('submit', (event) => {
    event.preventDefault();
    void runSearch();
  });

  saveForm.addEventListener('submit', (event) => {
    event.preventDefault();
    const name = String(new FormData(saveForm).get('name') ?? '').trim();
    if (!name) return;
    void (async () => {
      try {
        await createSavedSearch(name, readCriteria(filters));
        saveStatus.textContent = `Saved “${name}”.`;
        saveForm.reset();
        await refreshSaved();
      } catch (error) {
        saveStatus.textContent = describeError(error);
      }
    })();
  });

  await Promise.all([runSearch(), refreshSaved()]);
}
