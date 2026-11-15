import type {
  Dashboard,
  NotificationFrequency,
  OpportunityDetail,
  OpportunitySearchCriteria,
  OpportunitySearchResult,
  OpportunitySort,
  SavedSearch,
} from '../models/api.js';
import { getJson, sendJson } from './httpClient.js';

export function toQueryString(criteria: OpportunitySearchCriteria, page = 1, pageSize = 25, sort: OpportunitySort = 'PostedNewest'): string {
  const params = new URLSearchParams();
  if (criteria.keywords) params.set('keywords', criteria.keywords);
  for (const value of criteria.naicsCodes) params.append('naics', value);
  for (const value of criteria.agencies) params.append('agency', value);
  for (const value of criteria.noticeTypes) params.append('noticeType', value);
  for (const value of criteria.responseTypes) params.append('responseType', value);
  for (const value of criteria.setAsides) params.append('setAside', value);
  for (const value of criteria.placeOfPerformanceStates) params.append('state', value);
  if (criteria.postedFrom) params.set('postedFrom', criteria.postedFrom);
  if (criteria.postedTo) params.set('postedTo', criteria.postedTo);
  if (criteria.dueAfter) params.set('dueAfter', criteria.dueAfter);
  if (criteria.dueBefore) params.set('dueBefore', criteria.dueBefore);
  if (criteria.openOnly) params.set('openOnly', 'true');
  if (sort !== 'PostedNewest') params.set('sort', sort);
  params.set('page', String(page));
  params.set('pageSize', String(pageSize));
  return params.toString();
}

export function searchOpportunities(criteria: OpportunitySearchCriteria, page = 1, sort: OpportunitySort = 'PostedNewest'): Promise<OpportunitySearchResult> {
  return getJson<OpportunitySearchResult>(`/api/v1/opportunities?${toQueryString(criteria, page, 25, sort)}`);
}

export function getOpportunity(id: string): Promise<OpportunityDetail> {
  return getJson<OpportunityDetail>(`/api/v1/opportunities/${encodeURIComponent(id)}`);
}

export function getDashboard(): Promise<Dashboard> {
  return getJson<Dashboard>('/api/v1/dashboard');
}

export function listSavedSearches(): Promise<SavedSearch[]> {
  return getJson<SavedSearch[]>('/api/v1/saved-searches');
}

export async function createSavedSearch(
  name: string,
  criteria: OpportunitySearchCriteria,
  notificationFrequency: NotificationFrequency = 'None',
): Promise<SavedSearch> {
  const created = await sendJson<SavedSearch>('POST', '/api/v1/saved-searches', { name, criteria, notificationFrequency });
  if (!created) throw new Error('Saved search was not returned.');
  return created;
}

export async function deleteSavedSearch(id: string): Promise<void> {
  await sendJson<null>('DELETE', `/api/v1/saved-searches/${encodeURIComponent(id)}`);
}
