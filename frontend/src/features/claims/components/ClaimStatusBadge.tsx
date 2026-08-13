import { claimStatusLabels, type ClaimStatusFilter } from "../lib/claim-status";

export function ClaimStatusBadge({ status }: { status: string }) {
  const normalized = status as Exclude<ClaimStatusFilter, "ALL">;
  const label = claimStatusLabels[normalized] ?? status;
  const className =
    status === "REJECTED"
      ? "bg-destructive/10 text-destructive"
      : status === "APPROVED" || status === "REVIEWED"
        ? "bg-secondary text-secondary-foreground"
        : "bg-[#fff0db] text-[#8a4d00]";

  return (
    <span
      className={`inline-flex items-center rounded-full px-3.5 py-1.5 text-[11px] font-bold uppercase tracking-[0.16em] ${className}`}
    >
      {label}
    </span>
  );
}
