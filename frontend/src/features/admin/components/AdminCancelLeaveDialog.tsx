import { useState } from "react";
import { createPortal } from "react-dom";
import { LoaderCircle } from "lucide-react";
import { adminCancelLeave, type LeaveApplication } from "@/features/leave/api";
import { formatDateRange } from "@/features/leave/lib/leave-formatters";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";

// Confirms an admin withdrawing leave that was already APPROVED. Same shape as
// the employee's own cancel dialog — what is about to happen, then the
// destructive action and the way out — plus an optional reason, because the
// employee is notified and "cancelled by an admin" alone invites a question.
//
// Stays open on failure (unlike the employee's): the admin is mid-decision
// with a reason typed, and closing would throw that away.
export function AdminCancelLeaveDialog({
  application,
  employeeLabel,
  typeName,
  unpaid,
  onKeep,
  onCancelled,
}: {
  application: LeaveApplication;
  employeeLabel: string;
  typeName: string;
  unpaid: boolean;
  onKeep: () => void;
  onCancelled: (updated: LeaveApplication) => void;
}) {
  useBodyScrollLock();
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const days = `${application.totalDays} ${application.totalDays === 1 ? "day" : "days"}`;

  async function confirm() {
    setBusy(true);
    setError(null);
    try {
      onCancelled(await adminCancelLeave(application.id, reason.trim() || undefined));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not cancel this leave.");
      setBusy(false);
    }
  }

  return createPortal(
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 px-4 py-5 backdrop-blur-md">
      <section className="max-h-[calc(100vh-2.5rem)] w-full max-w-md overflow-y-auto rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)] sm:p-6">
        <h2 className="text-xl font-black text-foreground">Cancel this approved leave?</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          The {days} go back into {employeeLabel}'s {typeName} balance, and they're notified.
          This can't be undone — they would need to apply again.
        </p>

        <div className="mt-4 rounded-2xl border border-border/60 bg-surface-low p-4">
          <p className="text-base font-black text-foreground">{typeName}</p>
          <p className="mt-0.5 text-sm text-muted-foreground">
            {employeeLabel} · {formatDateRange(application.startDate, application.endDate)} · {days}
          </p>
        </div>

        {unpaid ? (
          <p className="mt-3 rounded-2xl border border-warning/30 bg-warning/10 px-4 py-3 text-xs leading-5 text-warning-foreground">
            Unpaid leave: an open payroll draft covering these dates will ask to be run again. A
            payroll that's already submitted is not changed.
          </p>
        ) : null}

        <label className="mt-4 block space-y-2">
          <span className="text-sm font-bold text-foreground">
            Reason <span className="font-medium text-muted-foreground">(optional)</span>
          </span>
          <textarea
            value={reason}
            disabled={busy}
            maxLength={1000}
            onChange={(event) => setReason(event.target.value)}
            placeholder="Shown to the employee, e.g. Trip postponed."
            className="min-h-24 w-full resize-none rounded-[18px] border border-border bg-card px-4 py-3 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:opacity-60"
          />
        </label>
        {error ? <p className="mt-2 text-sm font-semibold text-destructive">{error}</p> : null}

        <div className="mt-5 grid gap-2 sm:grid-cols-2">
          <button
            type="button"
            disabled={busy}
            onClick={() => void confirm()}
            className="flex h-11 items-center justify-center gap-2 rounded-full bg-destructive text-sm font-bold text-destructive-foreground transition hover:opacity-90 disabled:opacity-60"
          >
            {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
            Cancel leave
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={onKeep}
            className="h-11 rounded-full border border-border bg-card text-sm font-bold text-foreground transition hover:bg-secondary/50 disabled:opacity-60"
          >
            Keep it
          </button>
        </div>
      </section>
    </div>,
    document.body,
  );
}
