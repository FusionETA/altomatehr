import { apiOpenStream } from "./api-client";

// Mirrors backend/Modules/Realtime/Dtos/RealtimeEventDto.cs.
export type RealtimeScope = "CLAIMS" | "ATTENDANCE" | "LEAVE";
export type RealtimeAction = "SUBMITTED" | "UPDATED" | "APPROVED" | "REJECTED" | "CANCELLED" | "DELETED";

export type RealtimeEvent = {
  scope: RealtimeScope;
  action: RealtimeAction;
  entityId: string | null;
  at: string;
};

type Listener = (event: RealtimeEvent) => void;

const listeners = new Set<Listener>();

let abortController: AbortController | null = null;
let reconnectTimer: number | null = null;
let reconnectDelayMs = 1000;
const MAX_RECONNECT_DELAY_MS = 30_000;

// Subscribes to every realtime event (filter by `scope` in the callback, or
// use the `useRealtimeEvent` hook). Returns an unsubscribe function.
export function subscribeRealtime(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

// Opens the SSE connection for the current session. Safe to call once after
// login/refresh; call `disconnectRealtime` on logout. A second call while
// already connected is a no-op.
export function connectRealtime() {
  if (abortController) return;
  reconnectDelayMs = 1000;
  open();
}

export function disconnectRealtime() {
  if (reconnectTimer !== null) {
    window.clearTimeout(reconnectTimer);
    reconnectTimer = null;
  }
  abortController?.abort();
  abortController = null;
}

function open() {
  const controller = new AbortController();
  abortController = controller;

  void run(controller.signal);
}

async function run(signal: AbortSignal) {
  try {
    const res = await apiOpenStream("/realtime/stream", signal);
    reconnectDelayMs = 1000; // connected — reset backoff for the next drop

    const reader = res.body!.getReader();
    const decoder = new TextDecoder();
    let buffer = "";

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const frames = buffer.split("\n\n");
      buffer = frames.pop() ?? ""; // last chunk may be incomplete — keep it

      for (const frame of frames) {
        const line = frame.trim();
        if (!line || line.startsWith(":")) continue; // comment (`connected`/`ping`)

        const payload = line.startsWith("data:") ? line.slice(5).trim() : null;
        if (!payload) continue;

        try {
          const event = JSON.parse(payload) as RealtimeEvent;
          listeners.forEach((listener) => listener(event));
        } catch {
          /* malformed frame — skip it, the connection is still good */
        }
      }
    }
  } catch {
    /* network drop, server restart, or intentional abort — handled below */
  }

  if (signal.aborted) return; // disconnectRealtime() was called — don't reconnect

  abortController = null;
  reconnectTimer = window.setTimeout(() => {
    reconnectTimer = null;
    open();
  }, reconnectDelayMs);
  reconnectDelayMs = Math.min(reconnectDelayMs * 2, MAX_RECONNECT_DELAY_MS);
}
