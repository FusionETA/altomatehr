import { useEffect, useState } from "react";
import { BellRing, X } from "lucide-react";
import { enablePush, getPushStatus, isPushSupported } from "../lib/push";

// Once someone enables or dismisses this, don't ask again on every app open —
// they can still turn push on/off later from the bell dropdown.
const DISMISSED_KEY = "altomatehr-push-prompt-dismissed";

export function EnableNotificationsPrompt() {
  const [visible, setVisible] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!isPushSupported() || localStorage.getItem(DISMISSED_KEY)) return;
    getPushStatus().then((status) => {
      if (status === "unsubscribed") setVisible(true);
    });
  }, []);

  function dismiss() {
    localStorage.setItem(DISMISSED_KEY, "1");
    setVisible(false);
  }

  async function handleEnable() {
    setBusy(true);
    try {
      await enablePush();
    } catch {
      /* browser permission denied or subscribe failed — don't keep nagging */
    } finally {
      localStorage.setItem(DISMISSED_KEY, "1");
      setBusy(false);
      setVisible(false);
    }
  }

  if (!visible) return null;

  return (
    <div className="fixed inset-x-4 bottom-32 z-[60] mx-auto max-w-sm rounded-2xl border border-border/70 bg-card/98 p-4 shadow-[0_18px_48px_rgba(76,26,134,0.18)] backdrop-blur-xl sm:inset-x-auto sm:right-6 sm:bottom-6">
      <div className="flex items-start gap-3">
        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary">
          <BellRing className="h-4 w-4" />
        </div>
        <div className="min-w-0 flex-1">
          <p className="text-sm font-bold text-foreground">Turn on notifications</p>
          <p className="mt-0.5 text-xs text-muted-foreground">
            Get notified about approvals, claims and leave updates as they happen.
          </p>
          <div className="mt-3 flex items-center gap-2">
            <button
              type="button"
              onClick={handleEnable}
              disabled={busy}
              className="rounded-full bg-primary px-3.5 py-1.5 text-xs font-semibold text-primary-foreground transition hover:opacity-90 disabled:opacity-60"
            >
              {busy ? "Enabling…" : "Enable"}
            </button>
            <button
              type="button"
              onClick={dismiss}
              className="rounded-full px-3.5 py-1.5 text-xs font-semibold text-muted-foreground transition hover:bg-muted"
            >
              Not now
            </button>
          </div>
        </div>
        <button
          type="button"
          aria-label="Dismiss"
          onClick={dismiss}
          className="shrink-0 rounded-full p-1 text-muted-foreground transition hover:bg-muted hover:text-foreground"
        >
          <X className="h-3.5 w-3.5" />
        </button>
      </div>
    </div>
  );
}
