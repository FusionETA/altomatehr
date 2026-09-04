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

// A download: the bytes plus the filename the server chose. Kept separate from
// apiGetBlob because a file the user saves needs a name, and only the
// Content-Disposition header knows it.
export type ApiFile = { blob: Blob; fileName: string };

async function requestFile(path: string, fallbackName: string): Promise<ApiFile> {
  const res = await fetch(`${API_URL}${path}`, {
    method: "GET",
    credentials: "include",
    headers: {
      ...(authToken ? { Authorization: `Bearer ${authToken}` } : {}),
    },
  });

  if (!res.ok) {
    let data: unknown;
    let msg = `GET ${path} failed: ${res.status}`;
    try {
      data = await res.json();
      msg = getErrorMessage(data, msg);
    } catch {
      /* body wasn't JSON */
    }
    throw new ApiError(msg, res.status, data);
  }

  return {
    blob: await res.blob(),
    fileName: parseFileName(res.headers.get("Content-Disposition")) ?? fallbackName,
  };
}

// Pulls the filename out of `attachment; filename="x.csv"`, preferring the
// RFC 5987 `filename*=UTF-8''…` form when the server sends both.
function parseFileName(header: string | null): string | null {
  if (!header) return null;

  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (encoded) {
    try {
      return decodeURIComponent(encoded[1]);
    } catch {
      /* fall through to the plain form */
    }
  }

  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain ? plain[1].trim() : null;
}

// Hands a downloaded file to the browser's save flow.
export function saveFile({ blob, fileName }: ApiFile) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

export const apiGet = <T>(path: string) => request<T>("GET", path);
export const apiPost = <T>(path: string, body?: unknown) => request<T>("POST", path, body);
export const apiPostForm = <T>(path: string, body: FormData) =>
  requestForm<T>("POST", path, body);
export const apiGetBlob = (path: string) => requestBlob(path);
export const apiGetFile = (path: string, fallbackName: string) =>
  requestFile(path, fallbackName);
export const apiPut = <T>(path: string, body?: unknown) => request<T>("PUT", path, body);
export const apiDelete = <T>(path: string) => request<T>("DELETE", path);
