import type { DocumentSummary, DocumentVersion } from '../models/api.js';
import { getJson, sendForm, sendJson } from './httpClient.js';

const base = '/api/v1/documents';
const one = (id: string): string => `${base}/${encodeURIComponent(id)}`;

export function listDocuments(filter: { proposalId?: string; templates?: boolean } = {}): Promise<DocumentSummary[]> {
  const query = new URLSearchParams();
  if (filter.proposalId) query.set('proposalId', filter.proposalId);
  if (filter.templates) query.set('templates', 'true');
  const suffix = query.toString();
  return getJson<DocumentSummary[]>(suffix ? `${base}?${suffix}` : base);
}

export function uploadDocument(form: FormData): Promise<DocumentSummary> {
  return sendForm<DocumentSummary>(base, form);
}

export function listVersions(id: string): Promise<DocumentVersion[]> {
  return getJson<DocumentVersion[]>(`${one(id)}/versions`);
}

export function uploadVersion(id: string, form: FormData): Promise<DocumentSummary> {
  return sendForm<DocumentSummary>(`${one(id)}/versions`, form);
}

export async function placeDocument(id: string, proposalId: string | null, isTemplate: boolean): Promise<void> {
  await sendJson<DocumentSummary>('PUT', `${one(id)}/placement`, { proposalId, isTemplate });
}

export async function copyDocument(id: string, proposalId: string | null): Promise<void> {
  await sendJson<DocumentSummary>('POST', `${one(id)}/copy`, { proposalId });
}

export async function deleteDocument(id: string): Promise<void> {
  await sendJson<DocumentSummary>('DELETE', one(id));
}

export async function restoreDocument(id: string): Promise<void> {
  await sendJson<DocumentSummary>('POST', `${one(id)}/restore`);
}

export function downloadUrl(id: string): string {
  return `${one(id)}/content`;
}

export function versionDownloadUrl(id: string, version: number): string {
  return `${one(id)}/versions/${version}/content`;
}
