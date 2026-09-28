import { useState } from "react";
import { LifeBuoy, LoaderCircle } from "lucide-react";
import { exitSupport } from "@/features/auth/api";

// Shown across the top of the app for as long as a superadmin is acting inside
// a customer's company, so nobody forgets whose data they are changing — with
// the way out one click away.
export function SupportModeBanner({ organizationName }: { organizationName: string | null | undefined }) {
  const [leaving, setLeaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function leave() {
    setLeaving(true);
    setError(null);
    try {
      await exitSupport();
      // The refresh cookie is back on your own company; reload into it.
      window.location.assign(window.location.pathname);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not exit support mode.");
      setLeaving(false);
    }
  }

  return (
    <div className="border-b border-amber-400/70 bg-amber-100 px-4 py-2 text-amber-950 dark:border-amber-500/50 dark:bg-amber-900/40 dark:text-amber-50 print:hidden">
      <div className="mx-auto flex w-full max-w-6xl flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-start gap-2 text-sm">
          <LifeBuoy className="mt-0.5 h-4 w-4 shrink-0" />
          <p>
            <span className="font-bold">Support mode</span> · acting as{" "}
            <span className="font-semibold">{organizationName ?? "this company"}</span>.{" "}
            <span className="text-xs opacity-90">
              Your actions show as "System (Support)" in its activity log; the real person is
              recorded internally.
            </span>
          </p>
        </div>
        <div className="flex items-center gap-2">
          {error ? <span className="text-xs font-medium text-destructive">{error}</span> : null}
          <button
            type="button"
            onClick={() => void leave()}
            disabled={leaving}
            className="inline-flex shrink-0 items-center gap-2 rounded-full border border-amber-500/70 bg-white/70 px-3 py-1.5 text-xs font-bold text-amber-950 transition hover:bg-white disabled:opacity-60 dark:bg-amber-950/40 dark:text-amber-50"
          >
            {leaving ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : null}
            Exit support mode
          </button>
        </div>
      </div>
    </div>
  );
}
