import { useEffect, useState } from "react";

// Whether a newer build of the app has been deployed since this tab loaded.
//
// A single-page app keeps running the code it loaded until the page is
// refreshed, however many times the server is redeployed underneath it. The
// employee portal is exactly the page people leave open all day, so a fix can
// be live and still absent on their screen — the clock-in location dialog was
// "only there after a reload" for precisely this reason.
//
// Each build writes its id into the bundle and into /version.json (see
// vite.config.ts). This compares the two: on a timer, and whenever the tab
// comes back into view, which is when a stale tab is most likely to be used.

const BUILD_ID: string | undefined = import.meta.env.VITE_BUILD_ID;
const CHECK_EVERY_MS = 10 * 60 * 1000;

async function deployedBuild(): Promise<string | null> {
  try {
    // no-store on the request, so neither the browser nor anything in between
    // answers from a cache. The service worker leaves this path alone too.
    const res = await fetch("/version.json", { cache: "no-store" });
    if (!res.ok) return null;
    const body = (await res.json()) as { build?: unknown };
    return typeof body.build === "string" ? body.build : null;
  } catch {
    // Offline or mid-deploy: say nothing rather than nag on a guess.
    return null;
  }
}

export function useNewVersionAvailable(): boolean {
  const [available, setAvailable] = useState(false);

  useEffect(() => {
    // Dev has no version.json and reloads itself anyway.
    if (!import.meta.env.PROD || !BUILD_ID) return;

    let cancelled = false;
    const check = async () => {
      if (cancelled || document.visibilityState !== "visible") return;
      const deployed = await deployedBuild();
      if (!cancelled && deployed && deployed !== BUILD_ID) setAvailable(true);
    };

    const onVisible = () => {
      if (document.visibilityState === "visible") void check();
    };

    const timer = window.setInterval(() => void check(), CHECK_EVERY_MS);
    document.addEventListener("visibilitychange", onVisible);
    window.addEventListener("focus", onVisible);

    return () => {
      cancelled = true;
      window.clearInterval(timer);
      document.removeEventListener("visibilitychange", onVisible);
      window.removeEventListener("focus", onVisible);
    };
  }, []);

  return available;
}
