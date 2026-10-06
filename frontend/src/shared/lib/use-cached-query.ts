import { useCallback, useEffect, useRef, useState } from "react";
import * as cache from "./api-cache";

// Read a GET endpoint with its last answer available on the FIRST render.
//
// The plain `useEffect(() => getX().then(setX))` pattern always starts empty,
// so every navigation back to a screen shows a spinner over data the app
// already had a moment ago. This starts from the cache instead: if the path
// has been fetched before, `data` is populated before the first paint and
// `loading` is false, while a refresh runs quietly behind it.
//
// `path` must be the same string passed to apiGet — that is the cache key.
export function useCachedQuery<T>(
  path: string | null,
  fetcher: () => Promise<T>,
  options: {
    enabled?: boolean;
    // Re-read as soon as a write invalidates this path, instead of waiting to
    // be remounted. Off by default: a screen normally refreshes itself after
    // its own write, and every mounted query in a module re-fetching on every
    // write would be a lot of requests nobody asked for. For something that
    // must stay live wherever the write happened — a nav badge counting
    // approvals made on another screen — it is the point.
    refetchOnInvalidate?: boolean;
  } = {},
) {
  const enabled = options.enabled ?? path !== null;
  const refetchOnInvalidate = options.refetchOnInvalidate ?? false;
  const cached = path ? cache.peek<T>(path) : null;

  const [data, setData] = useState<T | undefined>(cached?.data);
  // Only the first visit waits. A revisit renders the old answer immediately
  // and swaps in the new one when it lands.
  const [loading, setLoading] = useState(enabled && cached === null);
  const [error, setError] = useState<string | null>(null);

  // Kept in a ref so a caller passing an inline arrow (almost all of them)
  // doesn't restart the request on every render.
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;

  // The path on screen now. An answer that lands after the caller has moved
  // to another path (a slow 2027 arriving once the user is back on 2026)
  // belongs to a screen nobody is looking at, and must not overwrite this one.
  const pathRef = useRef(path);
  pathRef.current = path;

  const load = useCallback(
    async (showSpinner: boolean) => {
      if (!enabled || !path) return;
      if (showSpinner) setLoading(true);
      try {
        const next = await fetcherRef.current();
        if (pathRef.current !== path) return;
        setData(next);
        setError(null);
      } catch (e) {
        if (pathRef.current !== path) return;
        // A failed refresh keeps the data already on screen: stale rows beat an
        // error page for something the user can still read. A failed FIRST load
        // has nothing to fall back on, so that one surfaces.
        setError(e instanceof Error ? e.message : "Could not load this.");
      } finally {
        if (pathRef.current === path) setLoading(false);
      }
    },
    [enabled, path],
  );

  useEffect(() => {
    if (!enabled || !path) return;
    const hit = cache.peek<T>(path);
    // `data` is seeded from the cache only on the first render. When the path
    // changes (a year picker, a different record) the screen has to switch to
    // THIS path's answer — or to nothing — here; otherwise a fresh cache hit
    // skips the fetch below and the previous path's rows stay on screen under
    // the new label.
    setData(hit?.data);
    setError(null);
    setLoading(hit === null);
    // Nothing cached  → fetch, with the spinner; it's the only honest thing to
    //                   show when there is nothing yet.
    // Cached but old   → show it and refresh behind it.
    // Cached and fresh → show it and don't ask again. Clicking between two
    //                    screens shouldn't re-query the same rows every few
    //                    seconds, and a write invalidates the path anyway.
    if (hit === null) void load(true);
    else if (hit.stale) void load(false);
    // Another component (or a write) updating this path updates this one too,
    // so two screens showing the same list can't disagree.
    return cache.subscribe(path, () => {
      const hit = cache.peek<T>(path);
      if (hit) setData(hit.data);
      // No entry means the path was just invalidated by a write. The data on
      // screen is kept while the new answer is fetched.
      else if (refetchOnInvalidate) void load(false);
    });
  }, [enabled, path, load, refetchOnInvalidate]);

  return {
    data,
    loading,
    error,
    /** Refetch now, keeping what's on screen while it runs. */
    refresh: useCallback(() => load(false), [load]),
  };
}
