import type { LeaveStatus } from "../api";

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
