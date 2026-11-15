import type { Assessment, AssessmentSummary, CompanyFitProfile, CompanyProfile, FitResult, GoNoGoDecision, Me } from '../models/api.js';
import { getJson, sendJson } from './httpClient.js';

export function getMe(): Promise<Me> {
  return getJson<Me>('/api/v1/me');
}

export function getCompanyProfile(): Promise<CompanyProfile> {
  return getJson<CompanyProfile>('/api/v1/company/profile');
}

export async function saveCompanyProfile(fit: CompanyFitProfile): Promise<CompanyProfile> {
  const saved = await sendJson<CompanyProfile>('PUT', '/api/v1/company/profile', fit);
  if (!saved) throw new Error('Profile was not returned.');
  return saved;
}

export function getFit(opportunityId: string): Promise<FitResult> {
  return getJson<FitResult>(`/api/v1/opportunities/${encodeURIComponent(opportunityId)}/fit`);
}

export function listAssessments(opportunityId: string): Promise<Assessment[]> {
  return getJson<Assessment[]>(`/api/v1/opportunities/${encodeURIComponent(opportunityId)}/assessments`);
}

export async function recordAssessment(opportunityId: string, decision: GoNoGoDecision, rationale: string): Promise<void> {
  await sendJson<Assessment>('POST', `/api/v1/opportunities/${encodeURIComponent(opportunityId)}/assessments`, { decision, rationale: rationale || null });
}

export function listLatestAssessments(): Promise<AssessmentSummary[]> {
  return getJson<AssessmentSummary[]>('/api/v1/assessments');
}
