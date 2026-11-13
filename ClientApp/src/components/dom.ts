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
