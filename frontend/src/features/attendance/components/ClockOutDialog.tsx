import { useState } from "react";
import { LoaderCircle, X } from "lucide-react";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";
import type { AttendanceRecord } from "../api";

export type ClockOutChoice = {
  /** The corrected clock-out time the employee is asking for, if any. */
  adjustment: { requestedTimeOut: string; reason: string } | null;
};

type Props = {
  today: AttendanceRecord;
  busy: boolean;
  error: string | null;
  onConfirm: (choice: ClockOutChoice) => void;
  onClose: () => void;
};

// Confirmation before clocking out, with a second tab for asking that the time
// be corrected.
//
// The clock-out ALWAYS happens at the real time. An adjustment is a pending
// request filed on top of it, which a supervisor approves (applies the
// corrected time) or rejects (leaves the record alone) — so nothing is lost
// either way, and there's never a window where the day has no clock-out.
//
// Closing cancels the clock-out entirely.
export function ClockOutDialog({ today, busy, error, onConfirm, onClose }: Props) {
  useBodyScrollLock();

  const [tab, setTab] = useState<"summary" | "adjust">("summary");
  const [time, setTime] = useState(() => toHhMm(new Date()));
  const [reason, setReason] = useState("");

  const projectedMin = today.timeIn
    ? Math.max(0, Math.round((Date.now() - new Date(today.timeIn).getTime()) / 60000))
    : 0;

  const requestedIso = hhMmToIso(time);
  const canAdjust = requestedIso !== null && reason.trim().length > 0 && !busy;

  // No onClick on the backdrop, deliberately: a stray tap while typing a
  // remark or picking a photo would discard the whole thing. Closing is the
  // X or Cancel, both explicit.
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/50 px-4 py-5 backdrop-blur-md sm:items-center">
      <section className="max-h-[calc(100vh-2.5rem)] w-full max-w-md overflow-y-auto rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)] sm:p-6">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
              Attendance
            </p>
            <h2 className="mt-1 text-xl font-black text-foreground">Clock out</h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="grid h-10 w-10 shrink-0 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground transition hover:text-foreground"
            aria-label="Cancel clock out"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="mt-5 flex gap-1 rounded-full bg-secondary/40 p-1" role="tablist">
          {(
            [
              ["summary", "Summary"],
              ["adjust", "Request adjustment"],
            ] as const
          ).map(([key, label]) => (
            <button
              key={key}
              type="button"
              role="tab"
              aria-selected={tab === key}
              onClick={() => setTab(key)}
              className={`flex-1 rounded-full px-3 py-2 text-xs font-bold transition ${
                tab === key
                  ? "bg-card text-foreground shadow-sm"
                  : "text-muted-foreground hover:text-foreground"
              }`}
            >
              {label}
            </button>
          ))}
        </div>

        {tab === "summary" ? (
          <div className="mt-5 grid gap-4">
            <dl className="grid gap-2 rounded-2xl border border-border/60 bg-surface-low/50 px-4 py-3">
              <Row label="Clocked in" value={clockTime(today.timeIn)} />
              <Row label="Clocking out" value={toDisplay(time)} />
              <Row label="On the clock" value={formatDuration(projectedMin)} />
            </dl>
            <p className="text-xs text-muted-foreground">
              Counted hours are capped at your shift length, and break time comes off the
              total &mdash; so this figure is time on the clock, not hours paid.
            </p>
            <Actions
              busy={busy}
              label="Clock out now"
              disabled={busy}
              onConfirm={() => onConfirm({ adjustment: null })}
              onClose={onClose}
            />
          </div>
        ) : (
          <div className="mt-5 grid gap-4">
            <p className="text-xs text-muted-foreground">
              You&rsquo;ll be clocked out now at{" "}
              <span className="font-semibold text-foreground">{toDisplay(toHhMm(new Date()))}</span>.
              Your supervisor decides whether to apply the corrected time below.
            </p>

            <label className="grid gap-1.5">
              <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">
                Corrected clock-out time
              </span>
              <input
                type="time"
                value={time}
                onChange={(event) => setTime(event.target.value)}
                className="h-12 rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              />
            </label>

            <label className="grid gap-1.5">
              <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">
                Reason
              </span>
              <textarea
                value={reason}
                onChange={(event) => setReason(event.target.value)}
                rows={3}
                placeholder="Why does the recorded time need correcting?"
                className="resize-none rounded-2xl border border-border bg-card px-4 py-3 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              />
            </label>

            {requestedIso !== null && reason.trim().length === 0 ? (
              <p className="text-xs text-muted-foreground">Add a reason to submit.</p>
            ) : null}

            <Actions
              busy={busy}
              label="Clock out & request"
              disabled={!canAdjust}
              onConfirm={() =>
                onConfirm({
                  adjustment: { requestedTimeOut: requestedIso!, reason: reason.trim() },
                })
              }
              onClose={onClose}
            />
          </div>
        )}

        {error ? <p className="mt-3 text-sm font-medium text-destructive">{error}</p> : null}
      </section>
    </div>
  );
}

function Actions({
  busy,
  label,
  disabled,
  onConfirm,
  onClose,
}: {
  busy: boolean;
  label: string;
  disabled: boolean;
  onConfirm: () => void;
  onClose: () => void;
}) {
  return (
    <div className="grid gap-2 sm:grid-cols-2">
      <button
        type="button"
        onClick={onConfirm}
        disabled={disabled}
        className="flex h-11 items-center justify-center gap-2 rounded-full bg-primary text-sm font-bold text-primary-foreground transition hover:opacity-90 disabled:opacity-60"
      >
        {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
        {label}
      </button>
      <button
        type="button"
        onClick={onClose}
        className="h-11 rounded-full border border-border bg-card text-sm font-bold text-foreground transition hover:bg-secondary/50"
      >
        Cancel
      </button>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-xs font-semibold tabular-nums text-foreground">{value}</dd>
    </div>
  );
}

function toHhMm(date: Date) {
  return `${String(date.getHours()).padStart(2, "0")}:${String(date.getMinutes()).padStart(2, "0")}`;
}

// Today's local date at hh:mm, as UTC ISO. Null when the field is incomplete,
// which is how the submit button stays disabled.
function hhMmToIso(hhMm: string): string | null {
  const match = /^(\d{1,2}):(\d{2})$/.exec(hhMm.trim());
  if (!match) return null;
  const hours = Number(match[1]);
  const minutes = Number(match[2]);
  if (hours > 23 || minutes > 59) return null;
  const at = new Date();
  at.setHours(hours, minutes, 0, 0);
  return at.toISOString();
}

function toDisplay(hhMm: string) {
  const iso = hhMmToIso(hhMm);
  return iso ? clockTime(iso) : "—";
}

function clockTime(iso: string | null) {
  if (!iso) return "—";
  return new Date(iso).toLocaleTimeString("en-US", { hour: "2-digit", minute: "2-digit" });
}

function formatDuration(totalMin: number) {
  const hours = Math.floor(totalMin / 60);
  const minutes = totalMin % 60;
  return hours > 0 ? `${hours}h ${String(minutes).padStart(2, "0")}m` : `${minutes}m`;
}
