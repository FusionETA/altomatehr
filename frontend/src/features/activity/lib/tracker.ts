import { sendActivity, sendActivityOnExit, type ActivityEventIn } from "../api";

// The browser half of user activity (Fusioneta-only usage tracking).
//
// Records two things, and nothing else:
//   - page views: the shell's view id, reported by usePageTracking
//   - clicks on elements marked data-track="some.name" — the NAME is sent,
//     never the element's text, what's on screen, or anything typed
// Changes (saves, approvals, deletes) are recorded by the server itself.
//
// Events are buffered and sent every 10 s, or at 20, or when the tab is hidden.
// While the tab is visible an empty batch goes every minute as a heartbeat, so
// the session reads "online". Who, which company and which session all come
// from the access token server-side.

const FLUSH_MS = 10_000;
const HEARTBEAT_MS = 60_000;
const FLUSH_AT = 20;
const MAX_BATCH = 50;
// Mirrors the server's check, so a bad name is dropped here rather than sent.
const NAME = /^[A-Za-z0-9][A-Za-z0-9._/:-]{0,159}$/;

let queue: ActivityEventIn[] = [];
let running = false;
let lastSentAt = 0;
let lastPage: string | null = null;
let flushTimer: ReturnType<typeof setInterval> | undefined;
let heartbeatTimer: ReturnType<typeof setInterval> | undefined;

export function startActivityTracking() {
  if (running) return;
  running = true;
  lastSentAt = Date.now();
  document.addEventListener("click", onClick, { capture: true });
  document.addEventListener("visibilitychange", onVisibilityChange);
  window.addEventListener("pagehide", onPageHide);
  flushTimer = setInterval(() => void flush(), FLUSH_MS);
  heartbeatTimer = setInterval(heartbeat, HEARTBEAT_MS);
}

// Before signing out: send what's left while the token is still valid.
export async function stopActivityTracking() {
  if (!running) return;
  running = false;
  document.removeEventListener("click", onClick, { capture: true });
  document.removeEventListener("visibilitychange", onVisibilityChange);
  window.removeEventListener("pagehide", onPageHide);
  clearInterval(flushTimer);
  clearInterval(heartbeatTimer);
  await flush();
  queue = [];
  lastPage = null;
}

export function trackPage(name: string) {
  // The same view re-rendering, or Back landing where it was, isn't a new visit.
  if (name === lastPage) return;
  lastPage = name;
  record("page", name);
}

export function trackClick(name: string) {
  record("click", name);
}

function record(kind: ActivityEventIn["kind"], name: string) {
  if (!running || !NAME.test(name)) return;
  queue.push({ kind, name, at: new Date().toISOString() });
  if (queue.length >= FLUSH_AT) void flush();
}

function onClick(event: MouseEvent) {
  const target = event.target instanceof Element ? event.target : null;
  const name = target?.closest("[data-track]")?.getAttribute("data-track");
  if (name) trackClick(name);
}

function onVisibilityChange() {
  if (document.visibilityState === "hidden") flushOnExit();
  else heartbeat();
}

function onPageHide() {
  flushOnExit();
}

async function flush() {
  if (queue.length === 0) return;
  const events = queue.splice(0, MAX_BATCH);
  lastSentAt = Date.now();
  try {
    await sendActivity(events);
  } catch {
    /* tracking must never bother anyone: a failed batch is dropped */
  }
}

function flushOnExit() {
  if (queue.length === 0) return;
  const events = queue.splice(0, MAX_BATCH);
  lastSentAt = Date.now();
  sendActivityOnExit(events);
}

function heartbeat() {
  if (!running || document.visibilityState !== "visible") return;
  if (Date.now() - lastSentAt < HEARTBEAT_MS - 5_000) return;
  if (queue.length > 0) {
    void flush();
    return;
  }
  lastSentAt = Date.now();
  sendActivity([]).catch(() => {});
}
