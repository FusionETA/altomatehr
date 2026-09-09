// Registers the app-shell/caching service worker unconditionally so the app
// is installable and has some resilience to flaky networks — independent of
// push.ts, which registers the SAME file (idempotent) only when the user
// opts into push notifications.
//
// Skipped outside production: in `npm run dev` this would cache Vite's
// module requests and fight with HMR.
export function registerServiceWorker() {
  if (!import.meta.env.PROD) return;
  if (!("serviceWorker" in navigator)) return;

  window.addEventListener("load", () => {
    navigator.serviceWorker.register("/sw.js").catch(() => {
      /* offline support is a nice-to-have, not worth surfacing a failure for */
    });
  });
}
