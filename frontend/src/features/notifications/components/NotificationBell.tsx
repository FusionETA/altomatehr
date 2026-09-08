import { useCallback, useEffect, useRef, useState } from "react";
import { Bell, BellOff, BellRing, Loader2 } from "lucide-react";
import {
  getNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  type Notification,
} from "../api";
import { timeAgo } from "../lib/format";
import { disablePush, enablePush, getPushStatus, isPushSupported, type PushStatus } from "../lib/push";

// How often the badge refreshes itself while the bell just sits in the
// header. There's no realtime scope for notifications (unlike Claims/Leave/
// Attendance's SSE nudges), so this is a plain poll rather than a push-driven
// refetch — good enough for a bell, not worth a backend change on its own.
const POLL_MS = 45_000;

export function NotificationBell({ onNavigate }: { onNavigate?: (url: string) => void }) {
  const [open, setOpen] = useState(false);
  const [notifications, setNotifications] = useState<Notification[]>([]);
  const [unreadCount, setUnreadCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [pushStatus, setPushStatus] = useState<PushStatus>("unsupported");
  const [pushBusy, setPushBusy] = useState(false);
  const menuRef = useRef<HTMLDivElement | null>(null);

  const refresh = useCallback(() => {
    getNotifications()
      .then((res) => {
        setNotifications(res.notifications);
        setUnreadCount(res.unreadCount);
      })
      .catch(() => {
        /* the badge just stays at its last known value */
      })
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    refresh();
    const interval = window.setInterval(refresh, POLL_MS);
    return () => window.clearInterval(interval);
  }, [refresh]);

  useEffect(() => {
    if (isPushSupported()) getPushStatus().then(setPushStatus);
  }, []);

  useEffect(() => {
    if (!open) return;

    function handlePointerDown(event: PointerEvent) {
      if (!menuRef.current?.contains(event.target as Node)) setOpen(false);
    }
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setOpen(false);
    }

    document.addEventListener("pointerdown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("pointerdown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [open]);

  function toggleOpen() {
    setOpen((was) => {
      if (!was) refresh();
      return !was;
    });
  }

  function handleRowClick(notification: Notification) {
    if (!notification.read) {
      setNotifications((prev) =>
        prev.map((n) => (n.id === notification.id ? { ...n, read: true } : n)),
      );
      setUnreadCount((count) => Math.max(0, count - 1));
      markNotificationRead(notification.id).catch(() => refresh());
    }

    if (notification.url) {
      if (/^https?:\/\//i.test(notification.url)) {
        window.open(notification.url, "_blank", "noopener,noreferrer");
      } else {
        onNavigate?.(notification.url);
      }
    }
    setOpen(false);
  }

  function handleMarkAllRead() {
    setNotifications((prev) => prev.map((n) => ({ ...n, read: true })));
    setUnreadCount(0);
    markAllNotificationsRead().catch(() => refresh());
  }

  async function handleTogglePush() {
    setPushBusy(true);
    try {
      if (pushStatus === "subscribed") {
        await disablePush();
        setPushStatus("unsubscribed");
      } else {
        await enablePush();
        setPushStatus("subscribed");
      }
    } catch {
      setPushStatus(await getPushStatus());
    } finally {
      setPushBusy(false);
    }
  }

  return (
    <div ref={menuRef} className="relative">
      <button
        type="button"
        aria-label="Notifications"
        aria-expanded={open}
        onClick={toggleOpen}
        className="relative flex h-10 w-10 items-center justify-center rounded-full border border-border/60 bg-card/90 text-muted-foreground shadow-ambient transition hover:text-foreground"
      >
        <Bell className="h-4 w-4" />
        {unreadCount > 0 ? (
          <span className="absolute -right-0.5 -top-0.5 flex h-5 min-w-5 items-center justify-center rounded-full bg-destructive px-1 text-[10px] font-bold leading-none text-destructive-foreground">
            {unreadCount > 99 ? "99+" : unreadCount}
          </span>
        ) : null}
      </button>

      {open ? (
        <div className="absolute right-0 top-[calc(100%+0.6rem)] z-50 w-80 overflow-hidden rounded-2xl border border-border/70 bg-card/98 text-left shadow-[0_18px_48px_rgba(76,26,134,0.14)] backdrop-blur-xl sm:w-96">
          <div className="flex items-center justify-between border-b border-border/60 px-4 py-3">
            <p className="text-sm font-bold text-foreground">Notifications</p>
            {unreadCount > 0 ? (
              <button
                type="button"
                onClick={handleMarkAllRead}
                className="text-xs font-semibold text-primary hover:underline"
              >
                Mark all read
              </button>
            ) : null}
          </div>

          <div className="max-h-96 overflow-y-auto">
            {loading ? (
              <div className="flex items-center justify-center py-8 text-muted-foreground">
                <Loader2 className="h-4 w-4 animate-spin" />
              </div>
            ) : notifications.length === 0 ? (
              <p className="px-4 py-8 text-center text-sm text-muted-foreground">
                You're all caught up.
              </p>
            ) : (
              notifications.map((notification) => (
                <button
                  key={notification.id}
                  type="button"
                  onClick={() => handleRowClick(notification)}
                  className="flex w-full items-start gap-2.5 border-b border-border/40 px-4 py-3 text-left transition-colors last:border-b-0 hover:bg-muted"
                >
                  <span
                    className={`mt-1.5 h-2 w-2 shrink-0 rounded-full ${
                      notification.read ? "bg-transparent" : "bg-primary"
                    }`}
                  />
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-sm font-semibold text-foreground">
                      {notification.title}
                    </span>
                    <span className="mt-0.5 block text-xs text-muted-foreground [display:-webkit-box] [-webkit-box-orient:vertical] [-webkit-line-clamp:2] overflow-hidden">
                      {notification.body}
                    </span>
                    <span className="mt-1 block text-[11px] text-muted-foreground/80">
                      {timeAgo(notification.createdAt)}
                    </span>
                  </span>
                </button>
              ))
            )}
          </div>

          {isPushSupported() && pushStatus !== "denied" ? (
            <button
              type="button"
              onClick={handleTogglePush}
              disabled={pushBusy}
              className="flex w-full items-center gap-2.5 border-t border-border/60 px-4 py-2.5 text-left text-xs font-semibold text-muted-foreground transition hover:bg-muted hover:text-foreground disabled:opacity-60"
            >
              {pushBusy ? (
                <Loader2 className="h-3.5 w-3.5 shrink-0 animate-spin" />
              ) : pushStatus === "subscribed" ? (
                <BellOff className="h-3.5 w-3.5 shrink-0" />
              ) : (
                <BellRing className="h-3.5 w-3.5 shrink-0" />
              )}
              {pushStatus === "subscribed"
                ? "Turn off push notifications on this device"
                : "Enable push notifications on this device"}
            </button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
