import type { SessionStatus } from "../api";

const LABEL: Record<SessionStatus, string> = {
  online: "Online",
  inactive: "Inactive",
  "signed-out": "Signed out",
};

// Online (seen in the last few minutes) · inactive (tab closed or idle, never
// signed out) · signed out.
export function SessionStatusDot({ status, withLabel = false }: { status: SessionStatus; withLabel?: boolean }) {
  const dot =
    status === "online"
      ? "bg-success ring-4 ring-success/15"
      : status === "inactive"
        ? "bg-muted-foreground/50"
        : "border border-muted-foreground/50 bg-transparent";
  return (
    <span className="inline-flex items-center gap-1.5">
      <span className={`inline-block h-2.5 w-2.5 shrink-0 rounded-full ${dot}`} aria-hidden />
      {withLabel ? (
        <span className={`text-xs font-semibold ${status === "online" ? "text-success" : "text-muted-foreground"}`}>
          {LABEL[status]}
        </span>
      ) : (
        <span className="sr-only">{LABEL[status]}</span>
      )}
    </span>
  );
}
