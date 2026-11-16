import { copyDocument, downloadUrl, listDocuments } from '../api/documents.js';
import { describeError, el, formatDate, link } from '../components/dom.js';
import type { ProposalDetail } from '../models/api.js';
import { stateText } from './documentsPage.js';

/** Files linked to a proposal, template copy and exports (P1.5). */
export async function renderProposalFiles(target: HTMLElement, proposal: ProposalDetail, closed: boolean): Promise<void> {
  const id = encodeURIComponent(proposal.id);
  const status = el('p', { attrs: { id: 'files-status', role: 'status', 'aria-live': 'polite' } });
  const list = el('div', { attrs: { id: 'proposal-files' } });
  const templateSelect = el('select', { attrs: { id: 'template-select' } });
  const copy = el('button', { text: 'Add a copy to this proposal', className: 'secondary', attrs: { type: 'submit' } });
  const templateForm = el('form', { className: 'inline-form' }, [el('label', { text: 'Start from a company template', attrs: { for: 'template-select' } }), templateSelect, copy]);

  const refresh = async (): Promise<void> => {
    const [files, templates] = await Promise.all([listDocuments({ proposalId: proposal.id }), listDocuments({ templates: true })]);
    const visible = files.filter((f) => f.state !== 'SoftDeleted');
    list.replaceChildren(
      visible.length === 0
        ? el('p', { className: 'muted', text: 'No files are linked to this proposal yet.' })
        : el('ul', { className: 'plain-list' }, visible.map((f) => el('li', {}, [
            f.isDownloadable ? link(downloadUrl(f.id), f.fileName) : el('span', { text: f.fileName }),
            ` · v${f.currentVersion} · ${stateText(f)} · updated ${formatDate(f.updatedAt)}`,
          ]))),
    );
    const usable = templates.filter((t) => t.isDownloadable);
    templateSelect.replaceChildren(...usable.map((t) => el('option', { text: t.fileName, attrs: { value: t.id } })));
    templateForm.hidden = closed || usable.length === 0;
  };

  templateForm.addEventListener('submit', (event) => {
    event.preventDefault();
    void (async () => {
      try {
        await copyDocument(templateSelect.value, proposal.id);
        status.textContent = 'Template copied into this proposal.';
        await refresh();
      } catch (error) {
        status.textContent = describeError(error);
      }
    })();
  });

  target.replaceChildren(
    el('section', { attrs: { 'aria-labelledby': 'files-heading' } }, [
      el('h2', { text: 'Files and exports', attrs: { id: 'files-heading' } }),
      list,
      templateForm,
      el('p', {}, [link(`/Documents?proposalId=${id}`, 'Upload or manage files for this proposal')]),
      el('p', { attrs: { id: 'proposal-exports' } }, [
        'Export: ',
        link(`/api/v1/proposals/${id}/exports/compliance-matrix.csv`, 'Compliance matrix (CSV)'),
        ' · ',
        link(`/api/v1/proposals/${id}/exports/outline.md`, 'Working outline (Markdown)'),
      ]),
      status,
    ]),
  );
  try {
    await refresh();
  } catch (error) {
    list.replaceChildren(el('p', { className: 'error', text: describeError(error) }));
  }
}
