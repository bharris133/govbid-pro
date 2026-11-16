import { unreadCount } from '../api/collaboration.js';

/** Shows the unread notification count in the navigation. Silent when signed out or without a company. */
export async function refreshUnreadBadge(): Promise<void> {
  const badge = document.querySelector<HTMLElement>('#unread-badge');
  if (!badge) return;
  try {
    const unread = await unreadCount();
    badge.hidden = unread === 0;
    badge.textContent = unread > 99 ? '99+' : String(unread);
    badge.setAttribute('aria-label', `${unread} unread`);
  } catch {
    badge.hidden = true;
  }
}
