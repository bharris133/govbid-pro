// DTOs mirrored from the .NET API (camelCase JSON, enums as strings).

export type SourceNoticeType =
  | 'Unknown'
  | 'PreSolicitation'
  | 'Solicitation'
  | 'CombinedSynopsisSolicitation'
  | 'SourcesSought'
  | 'SpecialNotice'
  | 'AwardNotice'
  | 'SoleSource'
  | 'Other';

export type ResponseType = 'Undetermined' | 'Rfi' | 'Rfp' | 'Rfq' | 'SourcesSought' | 'NoResponseExpected';

export type DataClassification =
  | 'Public'
  | 'BusinessInternal'
  | 'ConfidentialCommercial'
  | 'RestrictedPendingReview'
  | 'ProhibitedClassified';

export type ScanStatus = 'Pending' | 'Clean' | 'Infected' | 'ScannerNotConfigured' | 'DevelopmentBypass';
export type DocumentState = 'Active' | 'Quarantined' | 'SoftDeleted' | 'Purged';
export type NotificationFrequency = 'None' | 'Daily' | 'Weekly';

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface OpportunitySummary {
  id: string;
  title: string;
  solicitationNumber: string | null;
  agency: string | null;
  noticeType: SourceNoticeType;
  sourceNoticeTypeRaw: string | null;
  responseType: ResponseType;
  responseTypeBasis: 'MappingRule' | 'UserOverride';
  naicsCode: string | null;
  setAside: string | null;
  placeOfPerformanceState: string | null;
  postedAt: string | null;
  responseDueAt: string | null;
  sourceKey: string;
  isLiveSource: boolean;
  isAmended: boolean;
  lastRetrievedAt: string;
}

export interface SourceFreshness {
  sourceKey: string;
  isLiveSource: boolean;
  lastSuccessfulSyncAt: string | null;
  lastAttemptAt: string | null;
  lastErrorCode: string | null;
}

export interface OpportunitySearchResult {
  results: PagedResult<OpportunitySummary>;
  sources: SourceFreshness[];
}

export type SetAsideCategory = 'SmallBusiness' | 'EightA' | 'HubZone' | 'Sdvosb' | 'Vosb' | 'Wosb' | 'Edwosb' | 'IndianEconomicEnterprise';
export type GoNoGoDecision = 'Undecided' | 'Go' | 'NoGo' | 'Watch';
export type FitOutcome = 'Met' | 'Partial' | 'NotMet' | 'NotEvaluated';
export type CompanyRole = 'Reviewer' | 'ProposalWriter' | 'TeamManager' | 'CompanyAdmin';

export interface CompanyFitProfile {
  naicsCodes: string[];
  setAsideQualifications: SetAsideCategory[];
  preferredStates: string[];
  preferredAgencies: string[];
  capabilityKeywords: string[];
  minimumResponseDays: number;
}

export interface CompanyProfile {
  fit: CompanyFitProfile;
  updatedAt: string | null;
  updatedBy: string | null;
  isEmpty: boolean;
}

export interface FitCriterion {
  key: string;
  label: string;
  weight: number;
  points: number;
  outcome: FitOutcome;
  explanation: string;
}

export interface FitResult {
  score: number | null;
  band: 'Strong' | 'Possible' | 'Weak' | 'NotScored';
  ruleVersion: string;
  criteria: FitCriterion[];
  cautions: string[];
}

export interface Assessment {
  id: string;
  opportunityId: string;
  sequence: number;
  opportunityRevisionNumber: number;
  score: number | null;
  band: string;
  ruleVersion: string;
  criteria: FitCriterion[];
  decision: GoNoGoDecision;
  rationale: string | null;
  createdBy: string;
  createdAt: string;
}

export interface AssessmentSummary {
  opportunityId: string;
  opportunityTitle: string;
  responseDueAt: string | null;
  score: number | null;
  band: string;
  decision: GoNoGoDecision;
  decidedAt: string;
  opportunityAmendedSince: boolean;
}

export interface Me {
  userId: string;
  tenantId: string | null;
  companyName: string | null;
  role: CompanyRole | null;
}

export interface OpportunitySearchCriteria {
  keywords?: string | null;
  naicsCodes: string[];
  agencies: string[];
  noticeTypes: SourceNoticeType[];
  responseTypes: ResponseType[];
  setAsides: string[];
  placeOfPerformanceStates: string[];
  postedFrom?: string | null;
  postedTo?: string | null;
  dueAfter?: string | null;
  dueBefore?: string | null;
  openOnly?: boolean;
}

export type OpportunitySort = 'PostedNewest' | 'DueSoonest';

export interface OpportunityRevision {
  revisionNumber: number;
  changeKind: 'Created' | 'Amended';
  sourceUpdatedAt: string | null;
  recordedAt: string;
}

export interface OpportunityDetail extends OpportunitySummary {
  description: string | null;
  office: string | null;
  responseTypeRuleVersion: string | null;
  pscCode: string | null;
  placeOfPerformanceCountry: string | null;
  sourceUpdatedAt: string | null;
  responseDueOriginalText: string | null;
  responseDueTimeZone: string | null;
  submissionInstructions: string | null;
  sourceUrl: string | null;
  revisionNumber: number;
  firstIngestedAt: string;
  revisions: OpportunityRevision[];
}

export interface DiscoverySummary {
  openOpportunities: number;
  dueWithin7Days: number;
  newLast7Days: number;
  amendedLast7Days: number;
}

export interface UpcomingDeadline {
  id: string;
  title: string;
  agency: string | null;
  responseType: ResponseType;
  dueAt: string;
  dueOriginalText: string | null;
  isLiveSource: boolean;
}

export interface SavedSearchSummary {
  id: string;
  name: string;
  notificationFrequency: NotificationFrequency;
  openMatches: number;
  newMatchesLast7Days: number;
}

export interface Dashboard {
  generatedAt: string;
  discovery: DiscoverySummary;
  upcomingDeadlines: UpcomingDeadline[];
  savedSearches: SavedSearchSummary[];
  sources: SourceFreshness[];
  decisions: { go: number; noGo: number; watch: number; undecided: number };
}

export interface SavedSearch {
  id: string;
  name: string;
  criteria: OpportunitySearchCriteria;
  notificationFrequency: NotificationFrequency;
  createdAt: string;
}

export interface DocumentSummary {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  classification: DataClassification;
  scanStatus: ScanStatus;
  state: DocumentState;
  createdAt: string;
  recoverableUntil: string | null;
  isDownloadable: boolean;
}

export interface ProblemDetails {
  status?: number;
  title?: string;
  reasonCode?: string;
  traceId?: string;
}
