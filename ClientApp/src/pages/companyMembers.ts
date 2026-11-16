import { getMe } from '../api/assessments.js';
import { changeMemberRole, invite, listInvitations, removeMember, revokeInvitation } from '../api/collaboration.js';
import { listMembers } from '../api/proposals.js';
import { describeError, el, formatDate } from '../components/dom.js';
import { roleLabels } from '../labels.js';
import type { CompanyRole, Invitation, Member } from '../models/api.js';

const roles: CompanyRole[] = ['Reviewer', 'ProposalWriter', 'TeamManager', 'CompanyAdmin'];

function roleSelect(id: string, selected: CompanyRole): HTMLSelectElement {
  const select = el('select', { attrs: { id } }, roles.map((r) => el('option', { text: roleLabels[r], attrs: { value: r } })));
  select.value = selected;
  return select;
}

/** Members and invitations (P1.6). Everyone sees the member list; company admins manage it. */
export async function renderMembers(target: HTMLElement): Promise<void> {
  const status = el('p', { attrs: { id: 'members-status', role: 'status', 'aria-live': 'polite' } });
  const membersBox = el('div', { attrs: { id: 'members' } });
  const invitationsBox = el('div', { attrs: { id: 'invitations' } });
  const linkBox = el('div', { attrs: { id: 'invitation-link' } });
  const me = await getMe();
  const isAdmin = me.role === 'CompanyAdmin';

  const act = async (work: () => Promise<void>, done: string): Promise<void> => {
    try {
      await work();
      status.textContent = done;
      await refresh();
    } catch (error) {
      status.textContent = describeError(error);
    }
  };

  const memberRow = (m: Member): HTMLElement => {
    const cells: Node[] = [el('th', { text: m.email + (m.userId === me.userId ? ' (you)' : ''), attrs: { scope: 'row' } })];
    if (isAdmin) {
      const select = roleSelect(`role-${m.userId}`, m.role);
      select.setAttribute('aria-label', `Role of ${m.email}`);
      select.addEventListener('change', () => void act(() => changeMemberRole(m.userId, select.value as CompanyRole), `${m.email} is now ${roleLabels[select.value as CompanyRole].toLowerCase()}.`));
      const remove = el('button', { text: 'Remove', className: 'secondary', attrs: { type: 'button', 'aria-label': `Remove ${m.email}` } });
      remove.addEventListener('click', () => void act(() => removeMember(m.userId), `${m.email} was removed and can no longer open your company's data.`));
      cells.push(el('td', {}, [select]), el('td', { text: formatDate(m.joinedAt) }), el('td', {}, [remove]));
    } else {
      cells.push(el('td', { text: roleLabels[m.role] }), el('td', { text: formatDate(m.joinedAt) }));
    }
    return el('tr', {}, cells);
  };

  const invitationRow = (i: Invitation): HTMLElement => {
    const revoke = el('button', { text: 'Revoke', className: 'secondary', attrs: { type: 'button', 'aria-label': `Revoke invitation for ${i.email}` } });
    revoke.addEventListener('click', () => void act(() => revokeInvitation(i.id), `Invitation for ${i.email} revoked.`));
    return el('tr', {}, [
      el('th', { text: i.email, attrs: { scope: 'row' } }),
      el('td', { text: roleLabels[i.role] }),
      el('td', { text: i.state === 'Pending' ? `Pending, expires ${formatDate(i.expiresAt)}` : i.state }),
      el('td', {}, i.state === 'Pending' ? [revoke] : []),
    ]);
  };

  const refresh = async (): Promise<void> => {
    const members = await listMembers();
    const head = el('tr', {}, ['Member', 'Role', 'Joined', ...(isAdmin ? ['Actions'] : [])].map((h) => el('th', { text: h, attrs: { scope: 'col' } })));
    membersBox.replaceChildren(el('div', { className: 'table-wrap' }, [el('table', {}, [el('thead', {}, [head]), el('tbody', {}, members.map(memberRow))])]));
    if (!isAdmin) return;
    const invitations = await listInvitations();
    const invitationHead = el('tr', {}, ['Invited email', 'Role', 'Status', 'Actions'].map((h) => el('th', { text: h, attrs: { scope: 'col' } })));
    invitationsBox.replaceChildren(
      el('h3', { text: 'Invitations' }),
      invitations.length === 0 ? el('p', { className: 'muted', text: 'No invitations yet.' }) : el('div', { className: 'table-wrap' }, [el('table', {}, [el('thead', {}, [invitationHead]), el('tbody', {}, invitations.map(invitationRow))])]),
    );
  };

  const children: Node[] = [el('h2', { text: 'Members', attrs: { id: 'members-heading' } }), membersBox];
  if (isAdmin) {
    const email = el('input', { attrs: { id: 'invite-email', type: 'email', required: '', maxlength: '256', autocomplete: 'off' } });
    const role = roleSelect('invite-role', 'ProposalWriter');
    const form = el('form', { className: 'panel', attrs: { id: 'invite-form' } }, [
      el('h3', { text: 'Invite someone' }),
      el('div', { className: 'filters' }, [
        el('div', { className: 'field' }, [el('label', { text: 'Email address', attrs: { for: 'invite-email' } }), email]),
        el('div', { className: 'field' }, [el('label', { text: 'Role', attrs: { for: 'invite-role' } }), role]),
      ]),
      el('p', { className: 'muted', text: 'Invitations last 7 days by default, work once, and only for an account with this email address.' }),
      el('button', { text: 'Send invitation', attrs: { type: 'submit' } }),
    ]);
    form.addEventListener('submit', (event) => {
      event.preventDefault();
      void (async () => {
        try {
          const created = await invite(email.value, role.value as CompanyRole);
          const url = new URL(created.acceptPath, window.location.origin).toString();
          const field = el('input', { attrs: { id: 'invitation-url', readonly: '', value: url } });
          linkBox.replaceChildren(el('div', { className: 'panel' }, [
            el('p', { text: created.emailSent ? `Invitation emailed to ${created.invitation.email}. You can also share this link:` : `Email is not set up or could not be sent. Send this link to ${created.invitation.email} yourself:` }),
            el('label', { text: 'Invitation link (shown once)', attrs: { for: 'invitation-url' } }),
            field,
          ]));
          status.textContent = 'Invitation created.';
          email.value = '';
          await refresh();
        } catch (error) {
          status.textContent = describeError(error);
        }
      })();
    });
    const auditLink = el('a', { text: 'Download the audit log (CSV, last 90 days)', attrs: { href: '/api/v1/company/audit/export.csv', download: '' } });
    const auditBox = el('div', { attrs: { id: 'audit-export' } }, [
      el('h3', { text: 'Audit log' }),
      el('p', { className: 'muted', text: 'A record of changes, denied attempts, exports and deletions in your company. Each download is itself recorded.' }),
      el('p', {}, [auditLink]),
    ]);
    children.push(form, linkBox, invitationsBox, auditBox);
  }
  children.push(status);
  target.replaceChildren(el('section', { attrs: { 'aria-labelledby': 'members-heading' } }, children));
  try {
    await refresh();
  } catch (error) {
    status.textContent = describeError(error);
  }
}
