import { useCallback, useEffect, useState } from "react";

// Puts the shell's current view in the address bar, so the browser Back button
// steps back through the app instead of leaving it.
//
// This app has no router, deliberately (see App.tsx). Rather than introduce
// one, this mirrors the shape both shells ALREADY hold — a parent tab and an
// optional sub-page — into a single query parameter, and turns `popstate` back
// into the same setState they were doing anyway. The shells keep owning their
// own state; this only keeps the URL and the history stack in step with it.
//
// A query parameter rather than a path (`/?v=company/manage-employee`) because
// the path then stays `/`, so nginx serves index.html for every view with no
// try_files rule to deploy and nothing to 404 on a refresh.
//
// Coexists with shared/lib/xero-callback.ts, which strips its own parameter via
// replaceState at module load — that runs before the first render, so the value
// read here is already clean, and preserving unknown parameters below means a
// future one-shot flag survives a navigation.

export const NAV_PARAM = "v";

export type UrlNav = {
  parent: string;
  // Null for a tab with no sub-pages.
  child: string | null;
};

// The caller validates: only it knows which ids exist. Anything unrecognised in
// the URL — a stale bookmark, a typo, someone editing the address bar — must
// come back as a view that really exists rather than rendering an empty shell.
export type NormaliseNav = (nav: UrlNav) => UrlNav;

function parse(search: string, normalise: NormaliseNav, fallback: UrlNav): UrlNav {
  const raw = new URLSearchParams(search).get(NAV_PARAM);
  if (!raw) return fallback;

  const [parent, child] = raw.split("/", 2);
  if (!parent) return fallback;

  return normalise({ parent, child: child || null });
}

// Rebuilt from the CURRENT location each time rather than from a stored base,
// so any other parameter on the URL survives being navigated over.
function urlFor(nav: UrlNav, fallback: UrlNav): string {
  const params = new URLSearchParams(window.location.search);

  // The default view gets a clean URL: landing on the app should not
  // immediately rewrite the address bar to something to copy around.
  if (nav.parent === fallback.parent && nav.child === fallback.child) {
    params.delete(NAV_PARAM);
  } else {
    params.set(NAV_PARAM, nav.child ? `${nav.parent}/${nav.child}` : nav.parent);
  }

  // URLSearchParams escapes the separator to %2F. It is legal either way, but
  // a URL someone might paste into Slack should read as company/manage-employee.
  // A literal "/" in a query value needs no encoding (RFC 3986 §3.4).
  const query = params.toString().replace(/%2F/g, "/");
  return `${window.location.pathname}${query ? `?${query}` : ""}${window.location.hash}`;
}

export function useUrlNav(
  fallback: UrlNav,
  normalise: NormaliseNav,
): [UrlNav, (next: UrlNav) => void] {
  // Read once, at mount. Deliberately not kept in sync with the URL beyond
  // popstate: the shell is the owner while the tab is open, and re-reading on
  // every render would fight it.
  const [nav, setNav] = useState<UrlNav>(() =>
    parse(window.location.search, normalise, fallback),
  );

  // If the URL asked for something that does not exist — a stale bookmark, a
  // parameter left behind by a previous session, an ownerOnly view the signed-in
  // user cannot have — the shell has already fallen back to a real view. Put
  // that in the address bar too, so it cannot keep claiming to be somewhere the
  // app is not. replaceState, not push: correcting a URL is not a navigation
  // and must not become a Back step that just re-corrects itself.
  useEffect(() => {
    const canonical = urlFor(nav, fallback);
    const current = `${window.location.pathname}${window.location.search}${window.location.hash}`;
    if (canonical !== current) window.history.replaceState(null, "", canonical);
    // Mount only: afterwards `go` and popstate keep the two in step, and
    // re-running this on every nav change would fight them.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    // Back and Forward both land here. The URL is the source of truth for
    // this one event, so the answer is re-parsed rather than popped off a
    // stack we would otherwise have to keep.
    const onPop = () => setNav(parse(window.location.search, normalise, fallback));

    window.addEventListener("popstate", onPop);
    return () => window.removeEventListener("popstate", onPop);
    // `normalise` and `fallback` are values the shells build inline, so they
    // are new objects on every render; depending on them would re-subscribe
    // constantly. Neither changes meaningfully for the life of the shell.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const go = useCallback(
    (next: UrlNav) => {
      const resolved = normalise(next);
      setNav(resolved);

      // pushState, not replaceState: each view the admin opens is a place they
      // can go Back from — which is the entire point of this hook.
      window.history.pushState(null, "", urlFor(resolved, fallback));
    },
    // Same reasoning as above.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  );

  return [nav, go];
}
