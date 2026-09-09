import { overtimeStatusLabels } from "../lib/overtime-status";
import type { OvertimeStatus } from "../api";

function statusClass(status: OvertimeStatus) {
  if (status === "APPROVED") return "bg-secondary text-secondary-foreground";
  if (status === "REJECTED") return "bg-destructive/10 text-destructive";
  if (status === "CANCELLED") return "bg-muted text-muted-foreground";
  return "bg-warning text-warning-foreground";
}

// Shared by the employee's own overtime list and the admin attendance view, so
// one status never renders two different ways depending on who is looking.
export function OvertimeStatusBadge({ status }: { status: OvertimeStatus }) {
  return (
    <span
      className={`inline-flex rounded-full px-3.5 py-1.5 text-[11px] font-bold uppercase tracking-[0.16em] ${statusClass(status)}`}
    >
      {overtimeStatusLabels[status]}
    </span>
  );
}
