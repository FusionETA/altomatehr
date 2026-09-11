// Service worker: two jobs sharing one file, since a single origin/scope can
// only have one active worker.
//   1. Push API — turn a push event into a system notification, route a
//      click on it back into the app.
//   2. PWA installability + resilience — cache the app shell and static
//      build assets so a flaky connection or offline moment doesn't blank
//      the app, and so browsers consider it installable.
// It deliberately never touches API calls (see the fetch handler below) —
// those are live data (and the SSE stream), not something to cache or replay.

// Bumped to v2 when the favicon artwork changed: icons are served
// cache-first, so without a new cache name an installed app would show the
// old mark for one more load. activate deletes every cache but this one.
const RUNTIME_CACHE = "altomatehr-runtime-v2";
const APP_SHELL = ["/", "/manifest.json", "/apple-touch-icon.png", "/icons/icon-192.png", "/icons/icon-512.png"];

self.addEventListener("install", (event) => {
  self.skipWaiting();
  event.waitUntil(
    caches.open(RUNTIME_CACHE).then((cache) => cache.addAll(APP_SHELL).catch(() => {})),
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    Promise.all([
      self.clients.claim(),
      caches
        .keys()
        .then((keys) => Promise.all(keys.filter((key) => key !== RUNTIME_CACHE).map((key) => caches.delete(key)))),
    ]),
  );
});

self.addEventListener("fetch", (event) => {
  const req = event.request;
  if (req.method !== "GET") return; // never cache/replay a mutation

  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return;
  if (url.pathname.startsWith("/api/")) return; // backend calls (incl. the SSE stream) always go live

  // Page navigations: try the network first (so a redeploy is seen right
  // away), fall back to the cached shell when offline.
  if (req.mode === "navigate") {
    event.respondWith(fetch(req).catch(() => caches.match(req).then((cached) => cached || caches.match("/"))));
    return;
  }

  // Static build assets (JS/CSS/fonts/icons): serve from cache immediately
  // if we have it, refresh the cache from the network in the background.
  event.respondWith(
    caches.open(RUNTIME_CACHE).then(async (cache) => {
      const cached = await cache.match(req);
      const network = fetch(req)
        .then((res) => {
          if (res.ok) cache.put(req, res.clone());
          return res;
        })
        .catch(() => cached);
      return cached || network;
    }),
  );
});

self.addEventListener("push", (event) => {
  if (!event.data) return;

  let payload;
  try {
    payload = event.data.json();
  } catch {
    return;
  }

  const { title, body, url } = payload;
  event.waitUntil(
    self.registration.showNotification(title || "AltomateHR", {
      body: body || "",
      icon: "/brand-icon.png",
      badge: "/brand-icon.png",
      data: { url: url || "/" },
    }),
  );
});

// Clicking the OS notification focuses an already-open tab if there is one,
// otherwise opens a new one. The app itself decides what to do with the
// path (see NotificationBell's onNavigate) — this only gets a tab open.
self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const targetUrl = event.notification.data?.url || "/";

  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((clients) => {
      for (const client of clients) {
        if ("focus" in client) return client.focus();
      }
      if (self.clients.openWindow) return self.clients.openWindow(targetUrl);
    }),
  );
});
