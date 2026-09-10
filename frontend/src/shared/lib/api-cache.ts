// A small read-through cache for GETs, so navigating back to a screen shows
// what it showed last time instead of a spinner.
//
// The rule is stale-while-revalidate: hand back the last known answer at once,
// then refresh it in the background and tell anyone listening. The user sees
// data immediately and it corrects itself a moment later, rather than seeing
// nothing until the network finishes.
//
// Deliberately NOT a full query library. It caches by request path, holds
// everything in memory, and forgets it all on logout. Anything cleverer —
// pagination-aware keys, retries, offline persistence — is a reason to adopt
// TanStack Query rather than to grow this file.

type Entry = { data: unknown; at: number };

const cache = new Map<string, Entry>();
// One in-flight request per path. Two components mounting at once ask for the
// same thing constantly (a shell and its page), and without this they each
// open a socket for the identical answer.
const inFlight = new Map<string, Promise<unknown>>();
const listeners = new Map<string, Set<() => void>>();

/** How long an entry may be served before a background refresh is forced. */
const FRESH_MS = 30_000;

function notify(path: string) {
  for (const fn of listeners.get(path) ?? []) fn();
}

export function peek<T>(path: string): { data: T; stale: boolean } | null {
  const hit = cache.get(path);
  if (!hit) return null;
  return { data: hit.data as T, stale: Date.now() - hit.at > FRESH_MS };
}

export function put(path: string, data: unknown) {
  cache.set(path, { data, at: Date.now() });
  notify(path);
}

/** Subscribe to changes for one path. Returns the unsubscribe. */
export function subscribe(path: string, fn: () => void) {
  const set = listeners.get(path) ?? new Set();
  set.add(fn);
  listeners.set(path, set);
  return () => {
    set.delete(fn);
    if (set.size === 0) listeners.delete(path);
  };
}

/** Share one in-flight request between concurrent callers of the same path. */
export function dedupe<T>(path: string, run: () => Promise<T>): Promise<T> {
  const existing = inFlight.get(path);
  if (existing) return existing as Promise<T>;
  const p = run().finally(() => inFlight.delete(path));
  inFlight.set(path, p);
  return p;
}

// Writes invalidate by first path segment: POST /claims/123/approve drops
// every cached /claims* read. Coarse on purpose — a claim decision changes the
// list, the counts and the dashboard, and enumerating which is how a cache
// starts lying. Refetching a handful of paths costs far less than showing a
// stale approval.
export function invalidateFor(path: string) {
  const segment = path.split("?")[0].split("/").filter(Boolean)[0];
  if (!segment) return clear();
  for (const key of [...cache.keys()]) {
    if (key.split("?")[0].split("/").filter(Boolean)[0] === segment) {
      cache.delete(key);
      notify(key);
    }
  }
}

/**
 * Drop everything. Called when the auth token changes: cached rows belong to
 * whoever was signed in, and serving one user's data to the next is the one
 * failure this cache must never have.
 */
export function clear() {
  const paths = [...cache.keys()];
  cache.clear();
  inFlight.clear();
  for (const p of paths) notify(p);
}
