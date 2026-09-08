import { getVapidPublicKey, subscribeToPush, unsubscribeFromPush } from "../api";

// The Push API wants the VAPID key as a raw Uint8Array, but the server hands
// it over as the URL-safe base64 string browsers actually exchange — this is
// the standard conversion every web-push integration needs.
function urlBase64ToUint8Array(base64: string): Uint8Array<ArrayBuffer> {
  const padding = "=".repeat((4 - (base64.length % 4)) % 4);
  const normalized = (base64 + padding).replace(/-/g, "+").replace(/_/g, "/");
  const raw = atob(normalized);
  const output = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) output[i] = raw.charCodeAt(i);
  return output;
}

export function isPushSupported(): boolean {
  return "serviceWorker" in navigator && "PushManager" in window && "Notification" in window;
}

export type PushStatus = "unsupported" | "denied" | "subscribed" | "unsubscribed";

export async function getPushStatus(): Promise<PushStatus> {
  if (!isPushSupported()) return "unsupported";
  if (Notification.permission === "denied") return "denied";

  const registration = await navigator.serviceWorker.getRegistration();
  const existing = await registration?.pushManager.getSubscription();
  return existing ? "subscribed" : "unsubscribed";
}

// Registers the service worker (idempotent — the browser no-ops a re-register
// of the same script), asks for permission if needed, subscribes via the
// Push API, and tells the backend about the new device. Throws on refusal or
// failure; the caller decides how to surface that.
export async function enablePush(): Promise<void> {
  if (!isPushSupported()) throw new Error("Push notifications aren't supported in this browser.");

  const permission = await Notification.requestPermission();
  if (permission !== "granted") throw new Error("Notification permission was not granted.");

  const registration = await navigator.serviceWorker.register("/sw.js");
  await navigator.serviceWorker.ready;

  const { publicKey } = await getVapidPublicKey();
  if (!publicKey) throw new Error("Push notifications aren't configured on the server yet.");

  const subscription =
    (await registration.pushManager.getSubscription()) ??
    (await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(publicKey),
    }));

  await subscribeToPush(subscription.toJSON());
}

export async function disablePush(): Promise<void> {
  const registration = await navigator.serviceWorker.getRegistration();
  const subscription = await registration?.pushManager.getSubscription();
  if (!subscription) return;

  const endpoint = subscription.endpoint;
  await subscription.unsubscribe();
  await unsubscribeFromPush(endpoint);
}
