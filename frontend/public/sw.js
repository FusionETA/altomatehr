// Minimal service worker: exists only to receive Push API events and turn
// them into a system notification, and to route a click on that notification
// back into the app. No caching / offline support — this app isn't a PWA,
// this is the smallest thing the Push API requires.

self.addEventListener("install", () => {
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(self.clients.claim());
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
