import { apiGet, apiPost } from "@/shared/lib/api-client";

// Mirrors backend/Modules/Notifications/Entities/NotificationType.cs.
export type NotificationType =
  | "CLAIM_SUBMITTED"
  | "CLAIM_REVIEWED"
  | "LEAVE_SUBMITTED"
  | "LEAVE_REVIEWED"
  | "ATTENDANCE_APPROVAL"
  | "OVERTIME_SUBMITTED"
  | "OVERTIME_REVIEWED"
  | "APPROVAL_DIGEST"
  | "PARTNER_MESSAGE";

export type Notification = {
  id: string;
  type: NotificationType;
  title: string;
  body: string;
  url: string | null;
  read: boolean;
  createdAt: string;
};

export type NotificationList = {
  notifications: Notification[];
  unreadCount: number;
};

export const getNotifications = () => apiGet<NotificationList>("/notifications");

export const markNotificationRead = (id: string) =>
  apiPost<{ ok: boolean }>("/notifications/read", { id });

export const markAllNotificationsRead = () =>
  apiPost<{ ok: boolean }>("/notifications/read", { all: true });

// Web push — separate concern from the bell above, but the same feature owns
// both (see backend/Modules/Notifications, which pairs Notification and
// WebPushSubscription the same way).
export const getVapidPublicKey = () => apiGet<{ publicKey: string }>("/push/public-key");

export const subscribeToPush = (subscription: PushSubscriptionJSON) =>
  apiPost<{ ok: boolean }>("/push/subscribe", {
    endpoint: subscription.endpoint,
    keys: { p256dh: subscription.keys?.p256dh, auth: subscription.keys?.auth },
  });

export const unsubscribeFromPush = (endpoint: string) =>
  apiPost<{ ok: boolean }>("/push/unsubscribe", { endpoint });
