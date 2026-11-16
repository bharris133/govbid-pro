import type { ComplianceStatus, Member, ProposalDetail, ProposalStatus, ProposalSummary, Requirement, RequirementInput, ResponseType, ReviewOutcome } from '../models/api.js';
import { getJson, sendJson } from './httpClient.js';

const base = (id: string): string => `/api/v1/proposals/${encodeURIComponent(id)}`;

async function required<T>(promise: Promise<T | null>): Promise<T> {
  const value = await promise;
  if (value === null) throw new Error('The server returned no data.');
  return value;
}

export function listMembers(): Promise<Member[]> {
  return getJson<Member[]>('/api/v1/company/members');
}

export function listProposals(includeClosed = false): Promise<ProposalSummary[]> {
  return getJson<ProposalSummary[]>(`/api/v1/proposals${includeClosed ? '?includeClosed=true' : ''}`);
}

export function createProposal(opportunityId: string): Promise<ProposalDetail> {
  return required(sendJson<ProposalDetail>('POST', '/api/v1/proposals', { opportunityId }));
}

export function getProposal(id: string): Promise<ProposalDetail> {
  return getJson<ProposalDetail>(base(id));
}

export function updateProposal(id: string, title: string, responseType: ResponseType, ownerUserId: string): Promise<ProposalDetail> {
  return required(sendJson<ProposalDetail>('PUT', base(id), { title, responseType, ownerUserId }));
}

export function changeStatus(id: string, to: ProposalStatus, note: string | null, submissionEvidence: string | null): Promise<ProposalDetail> {
  return required(sendJson<ProposalDetail>('POST', `${base(id)}/status`, { to, note, submissionEvidence }));
}

export function addSection(id: string, title: string): Promise<ProposalDetail> {
  return required(sendJson<ProposalDetail>('POST', `${base(id)}/sections`, { title, assigneeUserId: null, isComplete: false }));
}

export function updateSection(id: string, sectionId: string, title: string, assigneeUserId: string | null, isComplete: boolean): Promise<ProposalDetail> {
  return required(sendJson<ProposalDetail>('PUT', `${base(id)}/sections/${encodeURIComponent(sectionId)}`, { title, assigneeUserId, isComplete }));
}

export function addRequirement(id: string, input: RequirementInput): Promise<Requirement> {
  return required(sendJson<Requirement>('POST', `${base(id)}/requirements`, input));
}

export function updateRequirement(id: string, requirementId: string, input: RequirementInput): Promise<Requirement> {
  return required(sendJson<Requirement>('PUT', `${base(id)}/requirements/${encodeURIComponent(requirementId)}`, input));
}

export async function deleteRequirement(id: string, requirementId: string): Promise<void> {
  await sendJson<null>('DELETE', `${base(id)}/requirements/${encodeURIComponent(requirementId)}`);
}

export function requestReview(id: string, reviewerUserId: string, instructions: string | null): Promise<ProposalDetail> {
  return required(sendJson<ProposalDetail>('POST', `${base(id)}/reviews`, { reviewerUserId, instructions }));
}

export function decideReview(id: string, reviewId: string, outcome: ReviewOutcome, comment: string | null): Promise<ProposalDetail> {
  return required(sendJson<ProposalDetail>('POST', `${base(id)}/reviews/${encodeURIComponent(reviewId)}/decision`, { outcome, comment }));
}

export function toInput(r: Requirement, status: ComplianceStatus = r.status): RequirementInput {
  return {
    reference: r.reference,
    text: r.text,
    sourceLocator: r.sourceLocator,
    category: r.category,
    mandatory: r.mandatory,
    sectionId: r.sectionId,
    responseLocation: r.responseLocation,
    assigneeUserId: r.assigneeUserId,
    evidence: r.evidence,
    status,
  };
}
