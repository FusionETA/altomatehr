// Generic HTTP layer — knows HOW to call the backend, nothing feature-specific.
const API_URL = import.meta.env.VITE_API_URL ?? "http://localhost:5001";

// The ACCESS token, held in memory. Set after login; attached below.
let authToken: string | null = null;
export function setAuthToken(token: string | null) {
  authToken = token;
}

// Thrown on any non-2xx response. Carries the parsed body so callers can branch
// on a server `code` (e.g. the off-site attendance case) — not just the message.
export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;
  readonly body?: unknown;

  constructor(message: string, status: number, body?: unknown) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.body = body;
    if (body && typeof body === "object" && "code" in body) {
      const c = (body as { code?: unknown }).code;
      if (typeof c === "string") this.code = c;
    }
  }
}

function getErrorMessage(data: unknown, fallback: string) {
  if (!data || typeof data !== "object") return fallback;

  const body = data as Record<string, unknown>;

  if (typeof body.message === "string") return body.message;
  if (typeof body.detail === "string") return body.detail;
  if (typeof body.title === "string") return body.title;

  return fallback;
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(`${API_URL}${path}`, {
    method,
    credentials: "include", // send/receive cookies (the refresh cookie) cross-origin
    headers: {
      "Content-Type": "application/json",
      ...(authToken ? { Authorization: `Bearer ${authToken}` } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  if (!res.ok) {
    let data: unknown;
    let msg = `${method} ${path} failed: ${res.status}`;
    try {
      data = await res.json();
      msg = getErrorMessage(data, msg);
    } catch {
      /* body wasn't JSON */
    }
    throw new ApiError(msg, res.status, data);
  }

  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

async function requestForm<T>(method: string, path: string, body: FormData): Promise<T> {
  const res = await fetch(`${API_URL}${path}`, {
    method,
    credentials: "include",
    headers: {
      ...(authToken ? { Authorization: `Bearer ${authToken}` } : {}),
    },
    body,
  });

  if (!res.ok) {
    let data: unknown;
    let msg = `${method} ${path} failed: ${res.status}`;
    try {
      data = await res.json();
      msg = getErrorMessage(data, msg);
    } catch {
      /* body wasn't JSON */
    }
    throw new ApiError(msg, res.status, data);
  }

  return (await res.json()) as T;
}

async function requestBlob(path: string): Promise<Blob> {
  const res = await fetch(`${API_URL}${path}`, {
    method: "GET",
    credentials: "include",
    headers: {
      ...(authToken ? { Authorization: `Bearer ${authToken}` } : {}),
    },
  });

  if (!res.ok) {
    let msg = `GET ${path} failed: ${res.status}`;
    try {
      const data = await res.json();
      msg = getErrorMessage(data, msg);
    } catch {
      /* body wasn't JSON */
    }
    throw new Error(msg);
  }

  return res.blob();
}

export const apiGet = <T>(path: string) => request<T>("GET", path);
export const apiPost = <T>(path: string, body?: unknown) => request<T>("POST", path, body);
export const apiPostForm = <T>(path: string, body: FormData) =>
  requestForm<T>("POST", path, body);
export const apiGetBlob = (path: string) => requestBlob(path);
export const apiPut = <T>(path: string, body?: unknown) => request<T>("PUT", path, body);
export const apiDelete = <T>(path: string) => request<T>("DELETE", path);
