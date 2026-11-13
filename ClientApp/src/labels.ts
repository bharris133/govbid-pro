import type { DataClassification, ResponseType, SourceNoticeType } from './models/api.js';

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
