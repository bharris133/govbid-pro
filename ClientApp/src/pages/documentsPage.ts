import { deleteDocument, downloadUrl, listDocuments, restoreDocument, uploadDocument } from '../api/documents.js';
import { describeError, el, formatDate, requireElement } from '../components/dom.js';
import { classificationLabels } from '../labels.js';
import type { DocumentSummary } from '../models/api.js';

const uploadErrorMessages: Record<string, string> = {
  'upload.declaration_missing': 'Confirm both statements before uploading.',
  'upload.file_type_not_allowed': 'This file type is not allowed, or the file does not match its extension.',
  'upload.size_not_allowed': 'The file is empty or larger than the allowed size.',
  'upload.declared_restricted_or_prohibited': 'Restricted or classified content cannot be uploaded.',
  'upload.malware_detected': 'The file was rejected by malware screening.',
};

function uploadMessage(error: unknown): string {
  const reason = (error as { reasonCode?: string }).reasonCode;
  if (reason?.startsWith('upload.suspected_restricted_content')) {
    return 'The file appears to contain security or CUI markings and was rejected. It was not stored. Contact your security officer if you need help.';
  }
  return (reason && uploadErrorMessages[reason]) ?? describeError(error);
}

function stateText(doc: DocumentSummary): string {
  if (doc.state === 'SoftDeleted') return `Deleted, recoverable until ${formatDate(doc.recoverableUntil)}`;
  if (doc.state === 'Quarantined') return 'Quarantined — access blocked';
  if (!doc.isDownloadable) return 'Awaiting malware scan — download unavailable';
  return 'Available';
}

export async function initDocumentsPage(): Promise<void> {
  const form = requireElement('#upload-form', HTMLFormElement);
  const status = requireElement('#upload-status', HTMLElement);
  const container = requireElement('#documents', HTMLElement);

  const refresh = async (): Promise<void> => {
    try {
      const documents = await listDocuments();
      if (documents.length === 0) {
        container.replaceChildren(el('p', { text: 'No documents uploaded yet.', className: 'muted' }));
        return;
      }
      const rows = documents.map((doc) => {
        const actions: Node[] = [];
        if (doc.isDownloadable) {
          actions.push(el('a', { text: 'Download', attrs: { href: downloadUrl(doc.id) } }));
        }
        if (doc.state === 'Active') {
          const del = el('button', { text: 'Delete', className: 'secondary', attrs: { type: 'button' } });
          del.addEventListener('click', () => void act(() => deleteDocument(doc.id), 'Document deleted. It can be restored during the recovery period.'));
          actions.push(del);
        }
        if (doc.state === 'SoftDeleted') {
          const restore = el('button', { text: 'Restore', className: 'secondary', attrs: { type: 'button' } });
          restore.addEventListener('click', () => void act(() => restoreDocument(doc.id), 'Document restored.'));
          actions.push(restore);
        }
        return el('tr', {}, [
          el('td', { text: doc.fileName }),
          el('td', { text: classificationLabels[doc.classification] }),
          el('td', { text: stateText(doc) }),
          el('td', { text: formatDate(doc.createdAt) }),
          el('td', {}, actions),
        ]);
      });
      const header = el('tr', {}, ['File', 'Classification', 'Status', 'Uploaded', 'Actions'].map((h) => el('th', { text: h, attrs: { scope: 'col' } })));
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

  form.addEventListener('submit', (event) => {
    event.preventDefault();
    status.textContent = 'Uploading…';
    void (async () => {
      try {
        await uploadDocument(new FormData(form));
        status.textContent = 'Upload complete.';
        form.reset();
        await refresh();
      } catch (error) {
        status.textContent = uploadMessage(error);
      }
    })();
  });

  await refresh();
}
