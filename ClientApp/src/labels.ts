import type { CompanyRole, ComplianceStatus, DataClassification, FitOutcome, GoNoGoDecision, ResponseType, SetAsideCategory, SourceNoticeType, WorkTaskStatus } from './models/api.js';

export const responseTypeLabels: Record<ResponseType, string> = {
  Undetermined: 'Undetermined',
  Rfi: 'RFI',
  Rfp: 'RFP',
  Rfq: 'RFQ',
  SourcesSought: 'Sources sought',
  NoResponseExpected: 'No response expected',
};

export const noticeTypeLabels: Record<SourceNoticeType, string> = {
  Unknown: 'Unknown',
  PreSolicitation: 'Presolicitation',
  Solicitation: 'Solicitation',
  CombinedSynopsisSolicitation: 'Combined synopsis/solicitation',
  SourcesSought: 'Sources sought',
  SpecialNotice: 'Special notice',
  AwardNotice: 'Award notice',
  SoleSource: 'Sole source',
  Other: 'Other',
};

export const classificationLabels: Record<DataClassification, string> = {
  Public: 'Public',
  BusinessInternal: 'Business internal',
  ConfidentialCommercial: 'Confidential commercial',
  RestrictedPendingReview: 'Restricted (pending review)',
  ProhibitedClassified: 'Prohibited',
};

export const decisionLabels: Record<GoNoGoDecision, string> = {
  Undecided: 'Scored, no decision',
  Go: 'Go',
  NoGo: 'No-go',
  Watch: 'Watch',
};

export const fitOutcomeLabels: Record<FitOutcome, string> = {
  Met: 'Met',
  Partial: 'Partly met',
  NotMet: 'Not met',
  NotEvaluated: 'Not evaluated',
};

export const fitBandLabels: Record<string, string> = {
  Strong: 'Strong fit',
  Possible: 'Possible fit',
  Weak: 'Weak fit',
  NotScored: 'Not scored',
};

export const setAsideLabels: Record<SetAsideCategory, string> = {
  SmallBusiness: 'Small business',
  EightA: '8(a)',
  HubZone: 'HUBZone',
  Sdvosb: 'Service-disabled veteran-owned small business (SDVOSB)',
  Vosb: 'Veteran-owned small business (VOSB)',
  Wosb: 'Women-owned small business (WOSB)',
  Edwosb: 'Economically disadvantaged WOSB (EDWOSB)',
  IndianEconomicEnterprise: 'Indian economic enterprise',
};

export const complianceLabels: Record<ComplianceStatus, string> = {
  NotStarted: 'Not started',
  InProgress: 'In progress',
  NeedsReview: 'Needs review',
  Compliant: 'Compliant',
  Exception: 'Exception',
};

export const blockedReasonLabels: Record<string, string> = {
  'role.insufficient': 'Needs a team manager or company administrator',
  'proposal.review_required': 'Needs an approved review',
  'proposal.review_pending': 'Waiting for reviewers',
  'proposal.review_changes_requested': 'A reviewer requested changes',
  'proposal.mandatory_requirements_open': 'Mandatory requirements are still open',
  'proposal.transition_not_allowed': 'Not allowed from this status',
};

export const roleLabels: Record<CompanyRole, string> = {
  Reviewer: 'Reviewer',
  ProposalWriter: 'Proposal writer',
  TeamManager: 'Team manager',
  CompanyAdmin: 'Company admin',
};

export const taskStatusLabels: Record<WorkTaskStatus, string> = {
  Open: 'Open',
  InProgress: 'In progress',
  Done: 'Done',
  Cancelled: 'Cancelled',
};
