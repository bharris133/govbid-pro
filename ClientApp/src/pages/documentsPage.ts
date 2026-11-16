import { deleteDocument, downloadUrl, listDocuments, listVersions, placeDocument, restoreDocument, uploadDocument, uploadVersion, versionDownloadUrl } from '../api/documents.js';
import { listProposals } from '../api/proposals.js';
import { describeError, el, formatDate, link, requireElement } from '../components/dom.js';
import { classificationLabels } from '../labels.js';
import type { DocumentSummary, DocumentVersion, ProposalSummary } from '../models/api.js';

const uploadErrorMessages: Record<string, string> = {
  'upload.declaration_missing': 'Confirm both statements before uploading.',
  'upload.file_type_not_allowed': 'This file type is not allowed, or the file does not match its extension.',
  'upload.size_not_allowed': 'The file is empty or larger than the allowed size.',
  'upload.declared_restricted_or_prohibited': 'Restricted or classified content cannot be uploaded.',
  'upload.malware_detected': 'The file was rejected by malware screening.',
  'upload.quota_exceeded': 'Your company has reached its document limit. Delete documents you no longer need.',
};

export function uploadMessage(error: unknown): string {
  const reason = (error as { reasonCode?: string }).reasonCode;
  if (reason?.startsWith('upload.suspected_restricted_content')) {
    return 'The file appears to contain security or CUI markings and was rejected. It was not stored. Contact your security officer if you need help.';
  }
  return (reason && uploadErrorMessages[reason]) ?? describeError(error);
}

export function stateText(doc: DocumentSummary): string {
  if (doc.state === 'SoftDeleted') return `Deleted, recoverable until ${formatDate(doc.recoverableUntil)}`;
  if (doc.state === 'Quarantined') return 'Quarantined — access blocked';
  if (!doc.isDownloadable) return 'Awaiting malware scan — download unavailable';
  return 'Available';
}

function versionLine(doc: DocumentSummary, v: DocumentVersion): HTMLElement {
  const label = `Version ${v.number}${v.isCurrent ? ' (current)' : ''}`;
  return el('li', {}, [
    v.isDownloadable ? link(versionDownloadUrl(doc.id, v.number), label) : el('span', { text: `${label} — download unavailable` }),
    ` · ${v.fileName} · ${v.uploadedBy} · ${formatDate(v.uploadedAt)}${v.note ? ` · “${v.note}”` : ''}`,
  ]);
}

function versionForm(doc: DocumentSummary, done: (message: string) => Promise<void>, fail: (message: string) => void): HTMLFormElement {
  const fileId = `v-file-${doc.id}`;
  const noteId = `v-note-${doc.id}`;
  const form = el('form', { className: 'panel', attrs: { enctype: 'multipart/form-data' } }, [
    el('div', { className: 'field' }, [el('label', { text: `New version of ${doc.fileName}`, attrs: { for: fileId } }), el('input', { attrs: { id: fileId, name: 'file', type: 'file', required: '' } })]),
    el('div', { className: 'field' }, [el('label', { text: 'What changed (optional)', attrs: { for: noteId } }), el('input', { attrs: { id: noteId, name: 'note', maxlength: '500' } })]),
    el('label', { className: 'check' }, [el('input', { attrs: { type: 'checkbox', name: 'affirmPermittedToUpload', value: 'true', required: '' } }), ' I am permitted to upload this file to GovBid Pro.']),
    el('label', { className: 'check' }, [el('input', { attrs: { type: 'checkbox', name: 'affirmNoProhibitedContent', value: 'true', required: '' } }), ' This file contains no classified information, CUI or FCI that must be safeguarded.']),
    el('button', { text: 'Upload new version', attrs: { type: 'submit' } }),
  ]);
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    void (async () => {
      try {
        const updated = await uploadVersion(doc.id, new FormData(form));
        await done(`Version ${updated.currentVersion} of ${updated.fileName} uploaded.`);
      } catch (error) {
        fail(uploadMessage(error));
      }
    })();
  });
  return form;
}

export async function initDocumentsPage(): Promise<void> {
  const form = requireElement('#upload-form', HTMLFormElement);
  const status = requireElement('#upload-status', HTMLElement);
  const container = requireElement('#documents', HTMLElement);
  const proposalSelect = requireElement('#u-proposal', HTMLSelectElement);
  const templatesOnly = requireElement('#templates-only', HTMLInputElement);
  const expanded = new Set<string>();
  let proposals: ProposalSummary[] = [];

  try {
    proposals = await listProposals(true);
    for (const p of proposals) proposalSelect.append(el('option', { text: p.title, attrs: { value: p.id } }));
    const preselect = new URLSearchParams(window.location.search).get('proposalId');
    if (preselect && proposals.some((p) => p.id === preselect)) proposalSelect.value = preselect;
  } catch {
    // The proposal picker is optional; uploads still work without it.
  }

  const refresh = async (): Promise<void> => {
    try {
      const documents = await listDocuments({ templates: templatesOnly.checked });
      if (documents.length === 0) {
        container.replaceChildren(el('p', { text: templatesOnly.checked ? 'No templates yet. Mark a document as a template to reuse it across proposals.' : 'No documents uploaded yet.', className: 'muted' }));
        return;
      }
      const rows: HTMLElement[] = [];
      for (const doc of documents) {
        const actions: Node[] = [];
        if (doc.isDownloadable) actions.push(el('a', { text: 'Download', attrs: { href: downloadUrl(doc.id) } }));
        if (doc.state === 'Active') {
          const versions = el('button', { text: expanded.has(doc.id) ? 'Hide versions' : 'Versions', className: 'secondary', attrs: { type: 'button', 'aria-expanded': String(expanded.has(doc.id)), 'aria-label': `Versions of ${doc.fileName}` } });
          versions.addEventListener('click', () => {
            if (!expanded.delete(doc.id)) expanded.add(doc.id);
            void refresh();
          });
          const template = el('button', { text: doc.isTemplate ? 'Remove template mark' : 'Mark as template', className: 'secondary', attrs: { type: 'button', 'aria-label': `${doc.isTemplate ? 'Remove template mark from' : 'Mark as template'} ${doc.fileName}` } });
          template.addEventListener('click', () => void act(() => placeDocument(doc.id, doc.proposalId, !doc.isTemplate), doc.isTemplate ? 'Template mark removed.' : 'Marked as a template.'));
          const del = el('button', { text: 'Delete', className: 'secondary', attrs: { type: 'button', 'aria-label': `Delete ${doc.fileName}` } });
          del.addEventListener('click', () => void act(() => deleteDocument(doc.id), 'Document deleted. It can be restored during the recovery period.'));
          actions.push(versions, template, del);
        }
        if (doc.state === 'SoftDeleted') {
          const restore = el('button', { text: 'Restore', className: 'secondary', attrs: { type: 'button', 'aria-label': `Restore ${doc.fileName}` } });
          restore.addEventListener('click', () => void act(() => restoreDocument(doc.id), 'Document restored.'));
          actions.push(restore);
        }
        const proposal = proposals.find((p) => p.id === doc.proposalId);
        rows.push(el('tr', {}, [
          el('td', {}, [
            el('span', { text: doc.fileName }),
            ...(doc.isTemplate ? [el('span', { text: 'Template', className: 'tag' })] : []),
            ...(proposal ? [el('div', { className: 'muted' }, ['Proposal: ', link(`/Proposals/Detail?id=${encodeURIComponent(proposal.id)}`, proposal.title)])] : []),
          ]),
          el('td', { text: `v${doc.currentVersion}` }),
          el('td', { text: classificationLabels[doc.classification] }),
          el('td', { text: stateText(doc) }),
          el('td', { text: formatDate(doc.updatedAt) }),
          el('td', { className: 'actions' }, actions),
        ]));
        if (expanded.has(doc.id) && doc.state === 'Active') {
          const list = el('ul', { className: 'plain-list' }, (await listVersions(doc.id)).map((v) => versionLine(doc, v)));
          const upload = versionForm(doc, async (message) => {
            status.textContent = message;
            await refresh();
          }, (message) => {
            status.textContent = message;
          });
          rows.push(el('tr', { className: 'detail' }, [el('td', { attrs: { colspan: '6' } }, [list, upload])]));
        }
      }
      const header = el('tr', {}, ['File', 'Version', 'Classification', 'Status', 'Updated', 'Actions'].map((h) => el('th', { text: h, attrs: { scope: 'col' } })));
      container.replaceChildren(el('div', { className: 'table-wrap' }, [el('table', {}, [el('thead', {}, [header]), el('tbody', {}, rows)])]));
    } catch (error) {
      container.replaceChildren(el('p', { text: describeError(error), className: 'error' }));
    }
  };

  const act = async (operation: () => Promise<void>, message: string): Promise<void> => {
    try {
      await operation();
      status.textContent = message;
      await refresh();
    } catch (error) {
      status.textContent = describeError(error);
    }
  };

  templatesOnly.addEventListener('change', () => void refresh());

  form.addEventListener('submit', (event) => {
    event.preventDefault();
    status.textContent = 'Uploading…';
    void (async () => {
      try {
        await uploadDocument(new FormData(form));
        status.textContent = 'Upload complete.';
        const keep = proposalSelect.value;
        form.reset();
        proposalSelect.value = keep;
        await refresh();
      } catch (error) {
        status.textContent = uploadMessage(error);
      }
    })();
  });

  await refresh();
}
