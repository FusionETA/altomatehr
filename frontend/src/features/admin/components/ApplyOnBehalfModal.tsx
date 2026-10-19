import { type FormEvent, useEffect, useMemo, useState } from "react";
import { LoaderCircle, X } from "lucide-react";
import {
  adminEditLeave,
  applyLeaveOnBehalf,
  previewLeaveDays,
  type LeaveApplication,
  type LeaveDuration,
  type LeaveType,
} from "@/features/leave/api";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-white/80 px-4 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary";

function daysBetween(start: string, end: string): number | null {
  if (!start || !end) return null;
  const s = new Date(`${start}T00:00:00`);
  const e = new Date(`${end}T00:00:00`);
  if (e < s) return null;
  return Math.round((e.getTime() - s.getTime()) / 86_400_000) + 1;
}

// Today in the browser's time zone, as yyyy-MM-dd (the date inputs' format).
function todayIso(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

// Records leave for an employee directly — lands APPROVED, no approval chain.
// For corrections/backfills, not a substitute for the normal apply flow.
// With `editing`, it changes that leave instead (before it starts): same
// fields, and the leave keeps its status.
export function ApplyOnBehalfModal({
  employeeId,
  employeeLabel,
  types,
  editing = null,
  onClose,
  onCreated,
}: {
  employeeId: string;
  employeeLabel: string;
  types: LeaveType[];
  editing?: LeaveApplication | null;
  onClose: () => void;
  onCreated: (app: LeaveApplication) => void;
}) {
  const [leaveTypeId, setLeaveTypeId] = useState(editing?.leaveTypeId ?? types[0]?.id ?? "");
  const [startDate, setStartDate] = useState(editing?.startDate ?? "");
  const [endDate, setEndDate] = useState(editing?.endDate ?? "");
  const [duration, setDuration] = useState<LeaveDuration>(editing?.duration ?? "FULL_DAY");
  const [reason, setReason] = useState(editing?.reason ?? "");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const days = useMemo(() => daysBetween(startDate, endDate), [startDate, endDate]);
  // A half day only makes sense on a single date.
  const singleDay = !!startDate && startDate === endDate;
  const effectiveDuration: LeaveDuration = singleDay ? duration : "FULL_DAY";

  // What the leave will actually cost: the server's count of working days
  // (weekends and public holidays skipped), the figure the balance uses.
  const [workingDays, setWorkingDays] = useState<number | null>(null);
  useEffect(() => {
    setWorkingDays(null);
    if (!startDate || !endDate || days == null) return;
    let stale = false;
    const timer = window.setTimeout(() => {
      previewLeaveDays(startDate, endDate, effectiveDuration)
        .then((r) => {
          if (!stale) setWorkingDays(r.totalDays);
        })
        .catch(() => {});
    }, 250);
    return () => {
      stale = true;
      window.clearTimeout(timer);
    };
  }, [startDate, endDate, effectiveDuration, days]);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!leaveTypeId || !startDate || !endDate) return;
    setSaving(true);
    setError(null);
    try {
      const body = {
        leaveTypeId,
        startDate,
        endDate,
        duration: effectiveDuration,
        // Editing sends it even when empty, so clearing the reason clears it.
        reason: editing ? reason.trim() : reason.trim() || undefined,
      };
      const app = editing ? await adminEditLeave(editing.id, body) : await applyLeaveOnBehalf(employeeId, body);
      onCreated(app);
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : editing ? "Could not save the changes." : "Could not record this leave.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm">
      <div className="w-full max-w-[520px] overflow-hidden rounded-[32px] border border-white/40 bg-card/95 shadow-panel backdrop-blur-xl">
        <form onSubmit={handleSubmit} className="nice-scrollbar max-h-[90vh] overflow-y-auto p-6 sm:p-8">
          <div className="flex items-start justify-between gap-4 border-b border-border/60 pb-4">
            <div>
              <h2 className="text-2xl font-black text-foreground">
                {editing ? "Edit leave" : "Apply on behalf"}
              </h2>
              <p className="mt-1 text-sm text-muted-foreground">
                For <span className="font-semibold text-foreground">{employeeLabel}</span>
                {editing
                  ? editing.status === "APPROVED"
                    ? " — stays approved. The employee is told what changed."
                    : " — stays pending with whoever is reviewing it. The employee is told what changed."
                  : " — lands approved immediately, no review needed."}
              </p>
            </div>
            <button
              type="button"
              aria-label="Close"
              onClick={onClose}
              className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
            >
              <X className="h-4 w-4" />
            </button>
          </div>

          <div className="mt-5 space-y-4">
            <div className="space-y-2">
              <span className="text-sm font-semibold text-foreground">Leave type</span>
              <Select value={leaveTypeId} onValueChange={setLeaveTypeId}>
                <SelectTrigger>
                  <SelectValue placeholder="Select a type" />
                </SelectTrigger>
                <SelectContent searchPlaceholder="Search types…">
                  {types.map((t) => (
                    <SelectItem key={t.id} value={t.id}>
                      {t.name}
                      {t.paid ? "" : " (unpaid)"}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="grid gap-4 sm:grid-cols-2">
              <label className="block space-y-2">
                <span className="text-sm font-semibold text-foreground">Start date</span>
                <input
                  required
                  type="date"
                  min={editing ? todayIso() : undefined}
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                  className={INPUT}
                />
              </label>
              <label className="block space-y-2">
                <span className="text-sm font-semibold text-foreground">End date</span>
                <input
                  required
                  type="date"
                  min={startDate || undefined}
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                  className={INPUT}
                />
              </label>
            </div>

            {singleDay ? (
              <div className="space-y-2">
                <span className="text-sm font-semibold text-foreground">Duration</span>
                <Select value={duration} onValueChange={(v) => setDuration(v as LeaveDuration)}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="FULL_DAY">Full day</SelectItem>
                    <SelectItem value="MORNING">Morning (half day)</SelectItem>
                    <SelectItem value="AFTERNOON">Afternoon (half day)</SelectItem>
                  </SelectContent>
                </Select>
              </div>
            ) : null}
            {days != null ? (
              <p className="text-sm text-muted-foreground">
                Working days:{" "}
                <span className="font-semibold text-foreground">
                  {workingDays == null ? "…" : `${workingDays} day${workingDays === 1 ? "" : "s"}`}
                </span>
              </p>
            ) : null}

            <label className="block space-y-2">
              <span className="text-sm font-semibold text-foreground">
                Reason <span className="font-normal text-muted-foreground">(optional)</span>
              </span>
              <textarea
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                className="min-h-[88px] w-full rounded-2xl border border-border bg-white/80 px-4 py-3 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              />
            </label>
          </div>

          {error ? (
            <p className="mt-4 rounded-2xl border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive">
              {error}
            </p>
          ) : null}

          <div className="mt-6 flex justify-end gap-3">
            <button
              type="button"
              onClick={onClose}
              className="rounded-2xl bg-muted px-4 py-3 text-sm font-semibold text-muted-foreground transition hover:text-foreground"
            >
              Cancel
            </button>
            <button
              type="submit"
              disabled={saving || !leaveTypeId || !startDate || !endDate || days == null}
              className="inline-flex items-center justify-center gap-2 rounded-2xl bg-primary px-5 py-3 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:bg-primary/90 disabled:pointer-events-none disabled:opacity-50"
            >
              {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
              {editing ? "Save changes" : "Record leave"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
