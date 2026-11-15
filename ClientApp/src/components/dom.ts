// DOM helpers. Text is always set via textContent; untrusted data is never written as HTML.

type Attributes = Record<string, string>;

export function el<K extends keyof HTMLElementTagNameMap>(
  tag: K,
  options: { text?: string; className?: string; attrs?: Attributes } = {},
  children: (Node | string)[] = [],
): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  if (options.text !== undefined) node.textContent = options.text;
  if (options.className) node.className = options.className;
  for (const [name, value] of Object.entries(options.attrs ?? {})) node.setAttribute(name, value);
  for (const child of children) node.append(child);
  return node;
}

export function requireElement<T extends Element>(selector: string, type: new () => T): T {
  const found = document.querySelector(selector);
  if (!(found instanceof type)) {
    throw new Error(`Required element ${selector} is missing.`);
  }
  return found;
}

export function formatDate(value: string | null): string {
  if (!value) return 'Not stated';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Not stated' : date.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
}

export function describeError(error: unknown): string {
  if (error instanceof Error) {
    const reason = (error as { reasonCode?: string }).reasonCode;
    return reason ? `${error.message} (${reason})` : error.message;
  }
  return 'Something went wrong.';
}

/** Date-only input value (yyyy-mm-dd) to an ISO instant at the start or end of that UTC day. */
export function dateInputToIso(value: string, endOfDay = false): string | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
  return `${value}T${endOfDay ? '23:59:59' : '00:00:00'}Z`;
}

export function link(href: string, text: string, className?: string): HTMLAnchorElement {
  return el('a', { text, attrs: { href }, ...(className ? { className } : {}) });
}

/** Renders the source freshness banner shared by discovery pages. Fixture data is always labeled. */
export function renderSourceStatus(
  target: HTMLElement,
  sources: { sourceKey: string; isLiveSource: boolean; lastSuccessfulSyncAt: string | null; lastErrorCode: string | null }[],
): void {
  target.replaceChildren();
  if (sources.length === 0) {
    target.textContent = 'No opportunity source has been synchronized yet.';
    return;
  }
  target.classList.toggle('fixture', sources.some((s) => !s.isLiveSource));
  for (const source of sources) {
    const label = source.isLiveSource ? 'Live source' : 'DEMO FIXTURE DATA — not live government data';
    const error = source.lastErrorCode ? ` Last error: ${source.lastErrorCode}.` : '';
    target.append(el('p', { text: `${label} (${source.sourceKey}). Last updated ${formatDate(source.lastSuccessfulSyncAt)}.${error}` }));
  }
}
