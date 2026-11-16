import { createTask, listTasks, setTaskStatus } from '../api/collaboration.js';
import { listMembers, listProposals } from '../api/proposals.js';
import { describeError, el, link, requireElement } from '../components/dom.js';
import { taskStatusLabels } from '../labels.js';
import type { WorkTask, WorkTaskStatus } from '../models/api.js';

const statuses: WorkTaskStatus[] = ['Open', 'InProgress', 'Done', 'Cancelled'];

export async function initTasksPage(): Promise<void> {
  const form = requireElement('#task-form', HTMLFormElement);
  const status = requireElement('#task-status', HTMLElement);
  const container = requireElement('#tasks', HTMLElement);
  const title = requireElement('#t-title', HTMLInputElement);
  const description = requireElement('#t-description', HTMLTextAreaElement);
  const assignee = requireElement('#t-assignee', HTMLSelectElement);
  const due = requireElement('#t-due', HTMLInputElement);
  const proposal = requireElement('#t-proposal', HTMLSelectElement);
  const mineOnly = requireElement('#mine-only', HTMLInputElement);
  const includeClosed = requireElement('#include-closed', HTMLInputElement);

  const proposalFilter = new URLSearchParams(window.location.search).get('proposalId');
  try {
    const [members, proposals] = await Promise.all([listMembers(), listProposals()]);
    for (const m of members) assignee.append(el('option', { text: m.email, attrs: { value: m.userId } }));
    for (const p of proposals) proposal.append(el('option', { text: p.title, attrs: { value: p.id } }));
    if (proposalFilter && proposals.some((p) => p.id === proposalFilter)) {
      proposal.value = proposalFilter;
      mineOnly.checked = false;
    }
  } catch (error) {
    status.textContent = describeError(error);
  }

  const row = (task: WorkTask): HTMLElement => {
    const select = el('select', { attrs: { 'aria-label': `Status of ${task.title}` } }, statuses.map((s) => el('option', { text: taskStatusLabels[s], attrs: { value: s } })));
    select.value = task.status;
    select.addEventListener('change', () => {
      void (async () => {
        try {
          await setTaskStatus(task.id, select.value as WorkTaskStatus);
          status.textContent = `“${task.title}” is now ${taskStatusLabels[select.value as WorkTaskStatus].toLowerCase()}.`;
          await refresh();
        } catch (error) {
          status.textContent = (error as { reasonCode?: string }).reasonCode === 'role.insufficient' ? 'Only the assignee or a proposal writer can change this task.' : describeError(error);
          select.value = task.status;
        }
      })();
    });
    return el('tr', { className: task.overdue ? 'open-mandatory' : '' }, [
      el('td', {}, [el('span', { text: task.title }), ...(task.overdue ? [el('span', { text: 'Overdue', className: 'tag' })] : []), ...(task.description ? [el('div', { className: 'muted preserve', text: task.description })] : [])]),
      el('td', { text: task.assignee ?? 'Unassigned' }),
      el('td', { text: task.dueOn ?? 'No date' }),
      el('td', {}, task.proposalId && task.proposalTitle ? [link(`/Proposals/Detail?id=${encodeURIComponent(task.proposalId)}`, task.proposalTitle)] : ['—']),
      el('td', {}, [select]),
    ]);
  };

  const refresh = async (): Promise<void> => {
    try {
      const tasks = await listTasks({ mine: mineOnly.checked, includeClosed: includeClosed.checked, ...(proposalFilter && !mineOnly.checked ? { proposalId: proposalFilter } : {}) });
      if (tasks.length === 0) {
        container.replaceChildren(el('p', { className: 'muted', text: mineOnly.checked ? 'Nothing is assigned to you right now.' : 'No tasks match.' }));
        return;
      }
      const header = el('tr', {}, ['Task', 'Assigned to', 'Due', 'Proposal', 'Status'].map((h) => el('th', { text: h, attrs: { scope: 'col' } })));
      container.replaceChildren(el('div', { className: 'table-wrap' }, [el('table', {}, [el('thead', {}, [header]), el('tbody', {}, tasks.map(row))])]));
    } catch (error) {
      container.replaceChildren(el('p', { className: 'error', text: describeError(error) }));
    }
  };

  form.addEventListener('submit', (event) => {
    event.preventDefault();
    void (async () => {
      try {
        const created = await createTask({ title: title.value, description: description.value.trim() || null, assigneeUserId: assignee.value || null, dueOn: due.value || null, proposalId: proposal.value || null });
        status.textContent = `Task “${created.title}” added.`;
        title.value = '';
        description.value = '';
        await refresh();
      } catch (error) {
        status.textContent = (error as { reasonCode?: string }).reasonCode === 'role.insufficient' ? 'Only a proposal writer, team manager or company admin can add tasks.' : describeError(error);
      }
    })();
  });
  mineOnly.addEventListener('change', () => void refresh());
  includeClosed.addEventListener('change', () => void refresh());
  await refresh();
}
