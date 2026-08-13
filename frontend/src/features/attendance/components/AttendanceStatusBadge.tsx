import type { AttendanceStatus } from "../api";

const STYLES: Record<AttendanceStatus, string> = {
  CLOCKED_IN: "bg-secondary text-secondary-foreground",
  ON_TIME: "bg-secondary text-secondary-foreground",
  CLOCKED_OUT: "bg-muted text-muted-foreground",
  LATE: "bg-amber-100 text-amber-800",
  MISSING: "bg-muted text-muted-foreground",
  ON_LEAVE: "bg-primary/10 text-primary",
};

const LABELS: Record<AttendanceStatus, string> = {
  CLOCKED_IN: "Clocked in",
  CLOCKED_OUT: "Clocked out",
  ON_TIME: "On time",
  LATE: "Late",
  MISSING: "Not clocked in",
  ON_LEAVE: "On leave",
};

export function AttendanceStatusBadge({ status }: { status: AttendanceStatus }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-3.5 py-1.5 text-[11px] font-bold uppercase tracking-[0.16em] ${STYLES[status]}`}
    >
      {LABELS[status]}
    </span>
  );
}
