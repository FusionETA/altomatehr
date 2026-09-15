import { type FormEvent, useMemo, useState } from "react";
import { LoaderCircle, X } from "lucide-react";
import { applyLeave, type LeaveApplication, type LeaveBalance, type LeaveType } from "../api";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-white/80 px-4 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary";

// Inclusive calendar-day span — mirrors the backend's TotalDays.
function daysBetween(start: string, end: string): number | null {
  if (!start || !end) return null;
  const s = new Date(`${start}T00:00:00`);
  const e = new Date(`${end}T00:00:00`);
  if (e < s) return null;
  return Math.round((e.getTime() - s.getTime()) / 86_400_000) + 1;
}

// Days read better as "12" than "12.0", but a half day has to survive.
const formatDays = (n: number) => (Number.isInteger(n) ? String(n) : n.toFixed(1));

export function ApplyLeaveModal({
  types,
  balances,
  onClose,
  onCreated,
}: {
  types: LeaveType[];
  /** The caller already holds these; without them this form asks people to
   *  guess how many days they have left. */
  balances: LeaveBalance[];
  onClose: () => void;
  onCreated: (app: LeaveApplication) => void;
}) {
  const [leaveTypeId, setLeaveTypeId] = useState(types[0]?.id ?? "");
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [reason, setReason] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const days = useMemo(() => daysBetween(startDate, endDate), [startDate, endDate]);

  const balanceByType = useMemo(
    () => new Map(balances.map((b) => [b.leaveTypeId, b])),
    [balances],
  );
  const selectedBalance = balanceByType.get(leaveTypeId) ?? null;

  // The server refuses an application that exceeds the balance. Saying so here
  // means the refusal isn't the first time somebody learns it — but it stays a
  // warning, not a block: the balance shown is today's, and the server decides
  // against the start date, which can differ on a pro-rated type.
  const overBalance =
    selectedBalance !== null &&
    selectedBalance.paid &&
    days !== null &&
    days > selectedBalance.remainingDays;

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!leaveTypeId || !startDate || !endDate) return;
    setSaving(true);
    setError(null);
    try {
      const app = await applyLeave({
        leaveTypeId,
        startDate,
        endDate,
        reason: reason.trim() || undefined,
      });
      onCreated(app);
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not submit your leave.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm">
      <div className="w-full max-w-[520px] overflow-hidden rounded-[32px] border border-white/40 bg-card/95 shadow-panel backdrop-blur-xl">
        <form onSubmit={handleSubmit} className="nice-scrollbar max-h-[90vh] overflow-y-auto p-6 sm:p-8">
          <div className="flex items-start justify-between gap-4 border-b border-border/60 pb-4">
            <div>
              <h2 className="text-2xl font-black text-foreground">Apply for leave</h2>
              <p className="mt-1 text-sm text-muted-foreground">Pick a type and your dates.</p>
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
                  {types.map((t) => {
                    const b = balanceByType.get(t.id);
                    return (
                      <SelectItem key={t.id} value={t.id}>
                        {t.name}
                        {!t.paid
                          ? " (unpaid)"
                          : b
                            ? ` · ${formatDays(b.remainingDays)} left`
                            : ""}
                      </SelectItem>
                    );
                  })}
                </SelectContent>
              </Select>

              {/* The number people actually open this form wondering about. */}
              {selectedBalance ? (
                selectedBalance.paid ? (
                  <p className="text-xs text-muted-foreground">
                    <span className="font-semibold text-foreground">
                      {formatDays(selectedBalance.remainingDays)} day
                      {selectedBalance.remainingDays === 1 ? "" : "s"} left
                    </span>{" "}
                    of {formatDays(selectedBalance.entitlementDays)} · {formatDays(selectedBalance.takenDays)}{" "}
                    taken
                    {selectedBalance.pendingDays > 0
                      ? ` · ${formatDays(selectedBalance.pendingDays)} awaiting approval`
                      : ""}
                  </p>
                ) : (
                  <p className="text-xs text-muted-foreground">
                    Unpaid — no balance to draw down.{" "}
                    {formatDays(selectedBalance.takenDays)} day
                    {selectedBalance.takenDays === 1 ? "" : "s"} taken so far.
                  </p>
                )
              ) : null}
            </div>

            <div className="grid gap-4 sm:grid-cols-2">
              <label className="block space-y-2">
                <span className="text-sm font-semibold text-foreground">Start date</span>
                <input
                  required
                  type="date"
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

            {days != null ? (
              <p className="text-sm text-muted-foreground">
                Duration: <span className="font-semibold text-foreground">{days} day{days === 1 ? "" : "s"}</span>
              </p>
            ) : null}

            {overBalance && selectedBalance ? (
              <p className="rounded-2xl border border-warning bg-warning/40 px-4 py-3 text-sm font-medium text-warning-foreground">
                That's {formatDays(days!)} day{days === 1 ? "" : "s"} against{" "}
                {formatDays(selectedBalance.remainingDays)} left — the server will refuse this.
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
              Submit
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
