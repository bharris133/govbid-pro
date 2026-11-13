import type { DocumentSummary } from '../models/api.js';
import { getJson, sendForm, sendJson } from './httpClient.js';

export function listDocuments(): Promise<DocumentSummary[]> {
  return getJson<DocumentSummary[]>('/api/v1/documents');
}

export function uploadDocument(form: FormData): Promise<DocumentSummary> {
  return sendForm<DocumentSummary>('/api/v1/documents', form);
}

export async function deleteDocument(id: string): Promise<void> {
  await sendJson<DocumentSummary>('DELETE', `/api/v1/documents/${encodeURIComponent(id)}`);
}

export async function restoreDocument(id: string): Promise<void> {
  await sendJson<DocumentSummary>('POST', `/api/v1/documents/${encodeURIComponent(id)}/restore`);
}

export function downloadUrl(id: string): string {
  return `/api/v1/documents/${encodeURIComponent(id)}/content`;
}
