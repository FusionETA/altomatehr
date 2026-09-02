import { useCallback, useEffect, useState } from "react";
import { Coffee, LoaderCircle, Play } from "lucide-react";
import { endBreak, getBreaks, startBreak, type AttendanceBreak } from "../api";

type Props = {
  /** Today's record. Breaks hang off it, so there is nothing to show without one. */
  recordId: string | null;
  /** Breaks only make sense while the shift is open. */
  clockedIn: boolean;
  /** Lets the parent refresh totals once a break closes. */
  onChange?: () => void;
};

// Start / end a break on today's session, with today's breaks listed underneath.
//
// Break time is deducted from counted hours, so this is the difference between
// an accurate day and an assumed one: when nothing is recorded the server falls
// back to the shift's unpaid break, which is a guess. Recording real breaks
// replaces the guess.
export function BreakControl({ recordId, clockedIn, onChange }: Props) {
  const [breaks, setBreaks] = useState<AttendanceBreak[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    if (!recordId) return;
    getBreaks(recordId)
      .then(setBreaks)
      .catch(() => setBreaks([]));
  }, [recordId]);

  useEffect(load, [load]);

  const open = breaks.find((b) => !b.endedAt) ?? null;
  const finished = breaks.filter((b) => b.endedAt);
  const totalMin = finished.reduce((sum, b) => sum + (b.durationMin ?? 0), 0);

  // Nothing to start a break against.
  if (!recordId || !clockedIn) return null;

  async function toggle() {
    setBusy(true);
    setError(null);
    try {
      // Location is best-effort: a denied prompt must not block recording a
      // break, since an unrecorded break costs the employee accuracy.
      const coords = await currentCoords();
      if (open) await endBreak(coords);
      else await startBreak(coords);
      load();
      onChange?.();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mt-3 rounded-[24px] border border-border/60 bg-surface-low/50 p-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
            Break
          </p>
          <p className="mt-0.5 text-sm font-bold text-foreground">
            {open ? `On break since ${clockTime(open.startedAt)}` : totalLabel(totalMin)}
          </p>
        </div>
        <button
          type="button"
          onClick={toggle}
          disabled={busy}
          className={`flex items-center gap-2 rounded-full px-4 py-2 text-xs font-bold transition disabled:opacity-60 ${
            open
              ? "bg-primary text-primary-foreground hover:opacity-90"
              : "border border-border bg-card text-foreground hover:bg-secondary/50"
          }`}
        >
          {busy ? (
            <LoaderCircle className="h-4 w-4 animate-spin" />
          ) : open ? (
            <Play className="h-4 w-4" />
          ) : (
            <Coffee className="h-4 w-4" />
          )}
          {open ? "End break" : "Start break"}
        </button>
      </div>

      {finished.length > 0 ? (
        <ul className="mt-3 space-y-1 border-t border-border/50 pt-2.5">
          {finished.map((b) => (
            <li key={b.id} className="flex items-baseline justify-between gap-3 text-xs">
              <span className="text-muted-foreground">
                {clockTime(b.startedAt)} &ndash; {clockTime(b.endedAt)}
                {b.approvalStatus === "PENDING" ? (
                  <span className="ml-1.5 text-[10px] opacity-70">awaiting approval</span>
                ) : null}
              </span>
              <span className="font-semibold tabular-nums text-foreground">
                {b.durationMin ?? 0}m
              </span>
            </li>
          ))}
        </ul>
      ) : null}

      {error ? <p className="mt-2 text-xs font-medium text-destructive">{error}</p> : null}
    </div>
  );
}

function totalLabel(totalMin: number) {
  if (totalMin === 0) return "None recorded today";
  return `${totalMin}m taken today`;
}

function clockTime(iso?: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleTimeString("en-US", { hour: "2-digit", minute: "2-digit" });
}

// Resolves to an empty object rather than rejecting — see toggle().
function currentCoords(): Promise<{ lat?: number; lng?: number }> {
  if (!navigator.geolocation) return Promise.resolve({});
  return new Promise((resolve) => {
    navigator.geolocation.getCurrentPosition(
      (pos) => resolve({ lat: pos.coords.latitude, lng: pos.coords.longitude }),
      () => resolve({}),
      { timeout: 5000 },
    );
  });
}
