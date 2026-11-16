import { getNotificationPreferences, listNotifications, markAllRead, markRead, setEmailNotifications } from '../api/collaboration.js';
import { describeError, el, formatDate, requireElement } from '../components/dom.js';
import type { NotificationItem } from '../models/api.js';
import { refreshUnreadBadge } from '../components/unreadBadge.js';

/** Only same-site paths are followed; anything else is shown as plain text. */
function safePath(link: string | null): string | null {
  return link && link.startsWith('/') && !link.startsWith('//') ? link : null;
}

export async function initNotificationsPage(): Promise<void> {
  const status = requireElement('#notification-status', HTMLElement);
  const container = requireElement('#notifications', HTMLElement);
  const emailToggle = requireElement('#email-notifications', HTMLInputElement);
  const availability = requireElement('#email-availability', HTMLElement);
  const markAll = requireElement('#mark-all-read', HTMLButtonElement);

  const item = (n: NotificationItem): HTMLElement => {
    const path = safePath(n.link);
    const heading = path ? el('a', { text: n.title, attrs: { href: path } }) : el('span', { text: n.title });
    if (path && !n.isRead) heading.addEventListener('click', () => void markRead(n.id).catch(() => undefined));
    const actions: Node[] = [];
    if (!n.isRead) {
      const read = el('button', { text: 'Mark as read', className: 'secondary', attrs: { type: 'button', 'aria-label': `Mark as read: ${n.title}` } });
      read.addEventListener('click', () => void act(() => markRead(n.id)));
      actions.push(read);
    }
    return el('li', { className: n.isRead ? 'panel' : 'panel unread' }, [
      el('p', {}, [...(n.isRead ? [] : [el('span', { text: 'New', className: 'tag' })]), heading]),
      ...(n.body ? [el('p', { className: 'preserve', text: n.body })] : []),
      el('p', { className: 'muted' }, [formatDate(n.createdAt), ' ', ...actions]),
    ]);
  };

  const refresh = async (): Promise<void> => {
    try {
      const list = await listNotifications();
      markAll.disabled = list.unread === 0;
      container.replaceChildren(list.items.length === 0 ? el('p', { className: 'muted', text: 'No notifications yet. Assignments, review requests, comments and saved-search alerts appear here.' }) : el('ul', { className: 'plain-list' }, list.items.map(item)));
      await refreshUnreadBadge();
    } catch (error) {
      container.replaceChildren(el('p', { className: 'error', text: describeError(error) }));
    }
  };

  const act = async (work: () => Promise<void>): Promise<void> => {
    try {
      await work();
      await refresh();
    } catch (error) {
      status.textContent = describeError(error);
    }
  };

  markAll.addEventListener('click', () => void act(markAllRead));
  emailToggle.addEventListener('change', () => {
    void (async () => {
      try {
        const saved = await setEmailNotifications(emailToggle.checked);
        status.textContent = saved.emailNotifications ? 'Email notifications are on.' : 'Email notifications are off. You still get them here.';
      } catch (error) {
        status.textContent = describeError(error);
      }
    })();
  });

  try {
    const preferences = await getNotificationPreferences();
    emailToggle.checked = preferences.emailNotifications;
    availability.textContent = preferences.emailAvailable ? '' : 'Email is not set up for this site yet, so notifications only appear here.';
  } catch (error) {
    status.textContent = describeError(error);
  }
  await refresh();
}
