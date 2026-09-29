import type { LeaveApplication, LeaveStatus } from "../api";

const STYLES: Record<LeaveStatus, string> = {
  PENDING: "bg-warning text-warning-foreground",
  APPROVED: "bg-secondary text-secondary-foreground",
  REJECTED: "bg-destructive/10 text-destructive",
  CANCELLED: "bg-muted text-muted-foreground",
};

const LABELS: Record<LeaveStatus, string> = {
  PENDING: "Pending",
  APPROVED: "Approved",
  REJECTED: "Rejected",
  CANCELLED: "Cancelled",
};

export function LeaveStatusBadge({ status }: { status: LeaveStatus }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-3.5 py-1.5 text-[11px] font-bold uppercase tracking-[0.16em] ${STYLES[status]}`}
    >
      {LABELS[status]}
    </span>
  );
}

// Beside the status badge on APPROVED leave: a cancellation is waiting on an
// approver (the leave still stands until the last one agrees), or was declined.
export function LeaveCancellationBadge({ application }: { application: LeaveApplication }) {
  if (application.status !== "APPROVED") return null;
  if (application.cancellationStatus === "PENDING") {
    return (
      <span className="inline-flex items-center rounded-full bg-warning px-3.5 py-1.5 text-[11px] font-bold uppercase tracking-[0.16em] text-warning-foreground">
        Cancellation requested
      </span>
    );
  }
  if (application.cancellationStatus === "REJECTED") {
    return (
      <span className="inline-flex items-center rounded-full bg-muted px-3.5 py-1.5 text-[11px] font-bold uppercase tracking-[0.16em] text-muted-foreground">
        Cancellation declined
      </span>
    );
  }
  return null;
}
