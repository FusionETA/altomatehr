import { useState } from "react";
import { RefreshCw, X } from "lucide-react";
import { useNewVersionAvailable } from "@/shared/lib/use-new-version";

// Tells someone with an old tab open that a newer version is live.
//
// It asks rather than reloading by itself: a reload in the middle of a claim
// or a leave form would throw away what they had typed. Dismissing hides it
// for this tab; the next deploy after that brings it back.
export function UpdateAvailableBanner() {
  const available = useNewVersionAvailable();
  const [dismissed, setDismissed] = useState(false);

  if (!available || dismissed) return null;

  return (
    <div className="pointer-events-none fixed inset-x-0 bottom-4 z-[60] flex justify-center px-4">
      <div
        role="status"
        className="pointer-events-auto flex w-full max-w-md items-center gap-3 rounded-2xl border border-border bg-card px-4 py-3 text-sm text-foreground shadow-ambient"
      >
        <RefreshCw className="size-4 shrink-0 text-primary" aria-hidden />
        <p className="min-w-0 flex-1">A new version of AltomateHR is available.</p>
        <button
          type="button"
          onClick={() => window.location.reload()}
          className="inline-flex h-9 shrink-0 items-center rounded-xl bg-primary px-3 text-xs font-semibold text-primary-foreground transition hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background"
        >
          Reload
        </button>
        <button
          type="button"
          onClick={() => setDismissed(true)}
          aria-label="Dismiss"
          className="inline-flex size-8 shrink-0 items-center justify-center rounded-lg text-muted-foreground transition hover:bg-muted/60 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          <X className="size-4" aria-hidden />
        </button>
      </div>
    </div>
  );
}
