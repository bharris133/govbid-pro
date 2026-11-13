import type { ProblemDetails } from '../models/api.js';

/** Error raised for non-2xx responses, carrying RFC 9457 problem details when present. */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails | null;

  constructor(status: number, problem: ProblemDetails | null) {
    super(problem?.title ?? `HTTP ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  get reasonCode(): string | undefined {
    return this.problem?.reasonCode;
  }
}

/** Antiforgery token rendered by the Razor layout. Required for all unsafe (mutating) requests. */
function csrfToken(): string {
  const meta = document.querySelector<HTMLMetaElement>('meta[name="csrf-token"]');
  const token = meta?.content;
  if (!token) {
    throw new Error('Antiforgery token is missing from the page.');
  }
  return token;
}

async function parseProblem(response: Response): Promise<ProblemDetails | null> {
  const type = response.headers.get('content-type') ?? '';
  if (!type.includes('json')) {
    return null;
  }
  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    return null;
  }
}

async function ensureOk(response: Response): Promise<Response> {
  if (!response.ok) {
    throw new ApiError(response.status, await parseProblem(response));
  }
  return response;
}

/** Safe (GET) request. Cookies are same-origin HttpOnly; no tokens are stored in browser storage. */
export async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(path, {
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
  });
  await ensureOk(response);
  return (await response.json()) as T;
}

type UnsafeMethod = 'POST' | 'PUT' | 'PATCH' | 'DELETE';

/** Mutating JSON request with the antiforgery header. */
export async function sendJson<TResponse>(method: UnsafeMethod, path: string, body?: unknown): Promise<TResponse | null> {
  const headers: Record<string, string> = { Accept: 'application/json', 'X-CSRF-TOKEN': csrfToken() };
  const init: RequestInit = { method, credentials: 'same-origin', headers };
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
    init.body = JSON.stringify(body);
  }
  const response = await ensureOk(await fetch(path, init));
  return response.status === 204 ? null : ((await response.json()) as TResponse);
}

/** Mutating multipart request with the antiforgery header. */
export async function sendForm<TResponse>(path: string, form: FormData): Promise<TResponse> {
  const response = await ensureOk(
    await fetch(path, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { Accept: 'application/json', 'X-CSRF-TOKEN': csrfToken() },
      body: form,
    }),
  );
  return (await response.json()) as TResponse;
}
