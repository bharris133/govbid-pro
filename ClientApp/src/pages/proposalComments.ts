import { addComment, deleteComment, listComments } from '../api/collaboration.js';
import { describeError, el, formatDate, link } from '../components/dom.js';
import type { ProposalComment, ProposalDetail } from '../models/api.js';

/** Discussion and task shortcut for a proposal (P1.6). */
export async function renderProposalComments(target: HTMLElement, proposal: ProposalDetail): Promise<void> {
  const status = el('p', { attrs: { id: 'comment-status', role: 'status', 'aria-live': 'polite' } });
  const list = el('div', { attrs: { id: 'comment-list' } });
  const body = el('textarea', { attrs: { id: 'comment-body', rows: '3', maxlength: '4000', required: '' } });
  const about = el('select', { attrs: { id: 'comment-requirement' } }, [
    el('option', { text: 'The whole proposal', attrs: { value: '' } }),
    ...proposal.requirements.map((r) => el('option', { text: `Requirement ${r.reference}`, attrs: { value: r.id } })),
  ]);

  const show = (comments: ProposalComment[]): void => {
    list.replaceChildren(
      comments.length === 0
        ? el('p', { className: 'muted', text: 'No comments yet.' })
        : el('ul', { className: 'plain-list' }, comments.map((c) => {
            const meta = `${c.author} · ${formatDate(c.createdAt)}${c.requirementReference ? ` · on requirement ${c.requirementReference}` : ''}`;
            const children: Node[] = [el('p', { className: 'muted', text: meta }), c.deleted ? el('p', { className: 'muted', text: 'This comment was deleted.' }) : el('p', { className: 'preserve', text: c.body })];
            if (c.canDelete) {
              const remove = el('button', { text: 'Delete', className: 'secondary', attrs: { type: 'button', 'aria-label': `Delete comment by ${c.author} from ${formatDate(c.createdAt)}` } });
              remove.addEventListener('click', () => {
                void (async () => {
                  try {
                    await deleteComment(proposal.id, c.id);
                    show(await listComments(proposal.id));
                    status.textContent = 'Comment deleted.';
                  } catch (error) {
                    status.textContent = describeError(error);
                  }
                })();
              });
              children.push(remove);
            }
            return el('li', { className: 'panel' }, children);
          })),
    );
  };

  const form = el('form', { attrs: { id: 'comment-form' } }, [
    el('div', { className: 'field' }, [el('label', { text: 'Add a comment', attrs: { for: 'comment-body' } }), body]),
    el('div', { className: 'field' }, [el('label', { text: 'About', attrs: { for: 'comment-requirement' } }), about]),
    el('button', { text: 'Post comment', attrs: { type: 'submit' } }),
  ]);
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    void (async () => {
      try {
        show(await addComment(proposal.id, body.value, about.value || null));
        body.value = '';
        status.textContent = 'Comment posted.';
      } catch (error) {
        status.textContent = describeError(error);
      }
    })();
  });

  target.replaceChildren(
    el('section', { attrs: { 'aria-labelledby': 'comments-heading' } }, [
      el('h2', { text: 'Comments', attrs: { id: 'comments-heading' } }),
      el('p', {}, [link(`/Tasks?proposalId=${encodeURIComponent(proposal.id)}`, 'Tasks for this proposal')]),
      list,
      form,
      status,
    ]),
  );
  try {
    show(await listComments(proposal.id));
  } catch (error) {
    list.replaceChildren(el('p', { className: 'error', text: describeError(error) }));
  }
}
