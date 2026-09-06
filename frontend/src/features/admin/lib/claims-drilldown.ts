import type { Claim } from "@/features/claims/api";
import {
  isOverLimitPending,
  isOverturnedClaim,
  isPendingClaim,
  isReadyToPay,
  isSpendThisMonth,
  isStaleClaim,
  OVERTURNED_WINDOW_DAYS,
  STALE_AFTER_DAYS,
} from "@/features/claims/lib/claim-insights";

// A drill-through: the named subset of claims behind a number the admin
// clicked. Every figure on the dashboard produces one of these, so no total is
// a dead end — the table can always show the rows it was counted from.
export type ClaimDrilldown = {
  label: string;
  // Optional second line: who or what narrowed it, shown on the table's banner.
  detail?: string;
  matches: (claim: Claim) => boolean;
};

export const stalePendingDrilldown = (): ClaimDrilldown => ({
  label: `Pending over ${STALE_AFTER_DAYS} days`,
  matches: (claim) => isStaleClaim(claim),
});

export const awaitingApprovalDrilldown = (): ClaimDrilldown => ({
  label: "Awaiting approval",
  matches: isPendingClaim,
});

export const overLimitDrilldown = (): ClaimDrilldown => ({
  label: "Over limit, still pending",
  matches: isOverLimitPending,
});

// Everything the org currently owes its employees.
export const readyToPayDrilldown = (): ClaimDrilldown => ({
  label: "Ready to pay",
  detail: "Approved, paid personally",
  matches: isReadyToPay,
});

// Everything that made up this month's spend, across every project.
export function monthSpendDrilldown(now: Date = new Date()): ClaimDrilldown {
  return {
    label: "Spend this month",
    detail: now.toLocaleDateString("en-MY", { month: "long", year: "numeric" }),
    matches: (claim) => isSpendThisMonth(claim, now),
  };
}

// Every overturned approval in the window, not just the named approvers' —
// the trust card's total covers the whole org.
export const overturnedDrilldown = (): ClaimDrilldown => ({
  label: "Overturned approvals",
  detail: `Last ${OVERTURNED_WINDOW_DAYS} days`,
  matches: (claim) => isOverturnedClaim(claim),
});

// Used wherever the backend already told us exactly which claims a number came
// from — stale claims grouped by approver, or a supervisor's overturned ones.
export function claimIdsDrilldown(
  ids: string[],
  label: string,
  detail?: string,
): ClaimDrilldown {
  const wanted = new Set(ids);
  return { label, detail, matches: (claim) => wanted.has(claim.id) };
}

// This month's spend on one project — the rows behind a project-spend bar.
// Shares isSpendThisMonth with the card that produced the figure, so the bar
// and the rows behind it can never be counted two different ways.
export function projectSpendDrilldown(
  projectId: string | null,
  projectName: string,
  now: Date = new Date(),
): ClaimDrilldown {
  return {
    label: `${projectName} spend`,
    detail: now.toLocaleDateString("en-MY", { month: "long", year: "numeric" }),
    matches: (claim) => claim.projectId === projectId && isSpendThisMonth(claim, now),
  };
}
