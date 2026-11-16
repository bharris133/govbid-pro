import type { CompanyRole, Invitation, InvitationCreated, NotificationList, NotificationPreferences, ProposalComment, TaskInput, WorkTask, WorkTaskStatus } from '../models/api.js';
import { getJson, sendJson } from './httpClient.js';

async function required<T>(result: Promise<T | null>): Promise<T> {
  const value = await result;
  if (value === null) throw new Error('The server returned no content.');
  return value;
}

export function listNotifications(unreadOnly = false): Promise<NotificationList> {
  return getJson<NotificationList>(`/api/v1/notifications${unreadOnly ? '?unreadOnly=true' : ''}`);
}

export async function unreadCount(): Promise<number> {
  return (await getJson<{ unread: number }>('/api/v1/notifications/unread-count')).unread;
}

export async function markRead(id: string): Promise<void> {
  await sendJson<void>('POST', `/api/v1/notifications/${encodeURIComponent(id)}/read`);
}

export async function markAllRead(): Promise<void> {
  await sendJson<{ marked: number }>('POST', '/api/v1/notifications/read-all');
}

export function getNotificationPreferences(): Promise<NotificationPreferences> {
  return getJson<NotificationPreferences>('/api/v1/notifications/preferences');
}

export function setEmailNotifications(emailNotifications: boolean): Promise<NotificationPreferences> {
  return required(sendJson<NotificationPreferences>('PUT', '/api/v1/notifications/preferences', { emailNotifications }));
}

export function listTasks(filter: { mine?: boolean; proposalId?: string; includeClosed?: boolean }): Promise<WorkTask[]> {
  const query = new URLSearchParams();
  if (filter.mine) query.set('mine', 'true');
  if (filter.proposalId) query.set('proposalId', filter.proposalId);
  if (filter.includeClosed) query.set('includeClosed', 'true');
  return getJson<WorkTask[]>(`/api/v1/tasks?${query.toString()}`);
}

export function createTask(input: TaskInput): Promise<WorkTask> {
  return required(sendJson<WorkTask>('POST', '/api/v1/tasks', input));
}

export function setTaskStatus(id: string, status: WorkTaskStatus): Promise<WorkTask> {
  return required(sendJson<WorkTask>('POST', `/api/v1/tasks/${encodeURIComponent(id)}/status`, { status }));
}

export function listComments(proposalId: string): Promise<ProposalComment[]> {
  return getJson<ProposalComment[]>(`/api/v1/proposals/${encodeURIComponent(proposalId)}/comments`);
}

export function addComment(proposalId: string, body: string, requirementId: string | null): Promise<ProposalComment[]> {
  return required(sendJson<ProposalComment[]>('POST', `/api/v1/proposals/${encodeURIComponent(proposalId)}/comments`, { body, requirementId }));
}

export async function deleteComment(proposalId: string, commentId: string): Promise<void> {
  await sendJson<void>('DELETE', `/api/v1/proposals/${encodeURIComponent(proposalId)}/comments/${encodeURIComponent(commentId)}`);
}

export function listInvitations(): Promise<Invitation[]> {
  return getJson<Invitation[]>('/api/v1/company/invitations');
}

export function invite(email: string, role: CompanyRole): Promise<InvitationCreated> {
  return required(sendJson<InvitationCreated>('POST', '/api/v1/company/invitations', { email, role }));
}

export async function revokeInvitation(id: string): Promise<void> {
  await sendJson<void>('DELETE', `/api/v1/company/invitations/${encodeURIComponent(id)}`);
}

export async function changeMemberRole(userId: string, role: CompanyRole): Promise<void> {
  await sendJson<unknown>('PUT', `/api/v1/company/members/${encodeURIComponent(userId)}/role`, { role });
}

export async function removeMember(userId: string): Promise<void> {
  await sendJson<void>('DELETE', `/api/v1/company/members/${encodeURIComponent(userId)}`);
}
