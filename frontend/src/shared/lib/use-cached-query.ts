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
  options: { enabled?: boolean } = {},
) {
  const enabled = options.enabled ?? path !== null;
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

  const load = useCallback(
    async (showSpinner: boolean) => {
      if (!enabled || !path) return;
      if (showSpinner) setLoading(true);
      try {
        const next = await fetcherRef.current();
        setData(next);
        setError(null);
      } catch (e) {
        // A failed refresh keeps the data already on screen: stale rows beat an
        // error page for something the user can still read. A failed FIRST load
        // has nothing to fall back on, so that one surfaces.
        setError(e instanceof Error ? e.message : "Could not load this.");
      } finally {
        setLoading(false);
      }
    },
    [enabled, path],
  );

  useEffect(() => {
    if (!enabled || !path) return;
    const hit = cache.peek<T>(path);
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
    });
  }, [enabled, path, load]);

  return {
    data,
    loading,
    error,
    /** Refetch now, keeping what's on screen while it runs. */
    refresh: useCallback(() => load(false), [load]),
  };
}
