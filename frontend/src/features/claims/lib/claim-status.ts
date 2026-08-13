import type { Claim } from "../api";

export type ClaimStatusFilter = "ALL" | "SUBMITTED" | "PENDING" | "APPROVED" | "REVIEWED" | "REJECTED";

export const visibleClaimStatuses: Exclude<ClaimStatusFilter, "ALL">[] = [
  "PENDING",
  "APPROVED",
  "REVIEWED",
  "REJECTED",
];

export const claimStatusLabels: Record<Exclude<ClaimStatusFilter, "ALL">, string> = {
  SUBMITTED: "Pending",
  PENDING: "Pending",
  APPROVED: "Approved",
  REVIEWED: "Reviewed",
  REJECTED: "Rejected",
};

export function claimMatchesStatus(claim: Claim, status: ClaimStatusFilter) {
  if (status === "ALL") return true;
  if (status === "PENDING") return claim.status === "PENDING" || claim.status === "SUBMITTED";
  return claim.status === status;
}
