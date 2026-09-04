import type { Claim } from "../api";

// Claims analysis shared by the admin dashboard's cards and its drill-through
// table. Pure functions over a claim list — no fetching, so a number on a card
// and the rows behind it are always computed from the same data.

// A claim pending this long is late enough that someone should be asked about
// it. Mirrors the backend's StaleAfterDays so the card and the API agree.
export const STALE_AFTER_DAYS = 7;

// Complete months averaged to answer "is this month normal?". Three is enough
// to smooth out a quiet month without reaching back into last year's rates.
const BASELINE_MONTHS = 3;

// Spend this much above the baseline and it stops being noise.
const SPIKE_PCT = 50;
const DROP_PCT = -25;

// How far back an overturned approval still counts. Mirrors the backend's
// OverturnedWindowDays so the card's total and its drill-through agree.
export const OVERTURNED_WINDOW_DAYS = 90;

export const UNASSIGNED_PROJECT = "Unassigned";

// The API serializes DateTimes without a timezone marker ("2026-08-11T09:21:17"),
// but the server computed them in UTC. Left to the browser they are read as
// LOCAL time, which in UTC+8 makes a claim look up to a day older than the
// backend says it is — so the overview card and this table would disagree.
// Everything here parses and compares in UTC, exactly as the backend does.
function serverTime(value: string): number {
  if (!value) return Number.NaN;
  const hasZone = /[Zz]$|[+-]\d{2}:?\d{2}$/.test(value);
  return new Date(hasZone ? value : `${value}Z`).getTime();
}

function submittedTime(claim: Claim) {
  return serverTime(claim.submittedAt || claim.spentAt);
}

// Which date a claim is filed under. Finance reconciles on when the money was
// spent, payroll on when it was submitted — the backend export draws the same
// distinction, so the table has to as well.
export type ClaimDateBasis = "spent" | "submitted";

// The claim's date as a UTC yyyy-MM-dd key. Comparing these as strings is both
// timezone-safe and exactly what the backend does (it compares .Date), so a
// range filtered here and the same range exported agree on the boundaries.
export function claimDateKey(claim: Claim, basis: ClaimDateBasis) {
  const raw = basis === "submitted" ? claim.submittedAt || claim.spentAt : claim.spentAt;
  const time = serverTime(raw);
  return Number.isNaN(time) ? "" : new Date(time).toISOString().slice(0, 10);
}

export function isPendingClaim(claim: Claim) {
  return claim.status === "PENDING" || claim.status === "SUBMITTED";
}

// Age in whole days since the claim was filed. Matches the backend's
// (int)(UtcNow - SubmittedAt).TotalDays.
export function claimAgeDays(claim: Claim, now: Date = new Date()) {
  const submitted = submittedTime(claim);
  if (Number.isNaN(submitted)) return 0;
  return Math.max(0, Math.floor((now.getTime() - submitted) / 86_400_000));
}

export function isStaleClaim(claim: Claim, now: Date = new Date()) {
  return isPendingClaim(claim) && claimAgeDays(claim, now) >= STALE_AFTER_DAYS;
}

// Over the account's spend limit AND still awaiting a decision — an approved
// over-limit claim has already been accepted, so it needs no attention.
export function isOverLimitPending(claim: Claim) {
  return claim.exceedsLimit && isPendingClaim(claim);
}

// Rejected claims never became spend, so they are excluded everywhere spend is
// totalled — exactly as they are in the backend's project-spend card.
function isSpend(claim: Claim) {
  return claim.status !== "REJECTED";
}

// ─── Ready to pay ────────────────────────────────────────────────────────────

// An approved claim the employee paid for themselves: the money left THEIR
// pocket, so the org owes it back. COMPANY-paid claims are deliberately not
// here — that spend already left a company account, so there is nothing to
// reimburse and counting it would overstate what is owed.
//
// APPROVED and REVIEWED both count: REVIEWED is the settled state in the
// production schema, and this app leaves APPROVED terminal, so a claim in
// either state has finished its approval chain.
function isSettled(claim: Claim) {
  return claim.status === "APPROVED" || claim.status === "REVIEWED";
}

export function isReadyToPay(claim: Claim) {
  return claim.paymentType === "PERSONAL" && isSettled(claim);
}

// A company-paid claim that has cleared approval. Not owed to anyone, but worth
// naming so an admin isn't left wondering where those claims went.
export function isSettledCompanySpend(claim: Claim) {
  return claim.paymentType === "COMPANY" && isSettled(claim);
}

// Group either side by person. Company-paid claims are grouped too — not
// because anyone is owed, but because the same employee's spend belongs
// together when an admin reviews what left the company account.
export function settledByEmployee(
  claims: Claim[],
  matches: (claim: Claim) => boolean,
  now: Date = new Date(),
): PayeeGroup[] {
  return readyToPayByEmployee(claims.filter(matches), now, () => true);
}

export type PayeeGroup = {
  employeeId: string;
  claimIds: string[];
  // The claims still to push to Xero — a payee may be part-synced if an
  // earlier run failed partway.
  unsyncedClaimIds: string[];
  syncedCount: number;
  failedCount: number;
  amount: number;
  // Days since the OLDEST of their claims was approved — how long this person
  // has been out of pocket with the decision already made.
  waitingDays: number;
};

// Grouped by person, because a reimbursement is one payment to one employee,
// not one payment per claim. Longest-waiting first: they have been carrying the
// cost since before anyone else.
export function readyToPayByEmployee(
  claims: Claim[],
  now: Date = new Date(),
  matches: (claim: Claim) => boolean = isReadyToPay,
): PayeeGroup[] {
  const groups = new Map<string, PayeeGroup>();

  for (const claim of claims) {
    if (!matches(claim)) continue;

    let group = groups.get(claim.employeeId);
    if (!group) {
      group = {
        employeeId: claim.employeeId,
        claimIds: [],
        unsyncedClaimIds: [],
        syncedCount: 0,
        failedCount: 0,
        amount: 0,
        waitingDays: 0,
      };
      groups.set(claim.employeeId, group);
    }

    group.claimIds.push(claim.id);
    if (claim.xeroSyncStatus === "SYNCED") group.syncedCount += 1;
    else {
      group.unsyncedClaimIds.push(claim.id);
      if (claim.xeroSyncStatus === "ERROR") group.failedCount += 1;
    }
    group.amount += claim.amount;
    group.waitingDays = Math.max(group.waitingDays, approvedAgeDays(claim, now));
  }

  return Array.from(groups.values()).sort(
    (a, b) => b.waitingDays - a.waitingDays || b.amount - a.amount,
  );
}

// Days since the claim was last decided. UpdatedAt is when approval landed, so
// for an approved claim this is how long it has been waiting on payment rather
// than on a decision.
export function approvedAgeDays(claim: Claim, now: Date = new Date()) {
  const decided = serverTime(claim.updatedAt);
  if (Number.isNaN(decided)) return claimAgeDays(claim, now);
  return Math.max(0, Math.floor((now.getTime() - decided) / 86_400_000));
}

export function sumAmount(claims: Claim[]) {
  return claims.reduce((total, claim) => total + claim.amount, 0);
}

// A rejection that a first-line approver had already waved through: the claim
// only reaches a step past 0 by being approved at step 0 first. Same rule the
// backend applies, so the trust card's total drills to exactly what it counted.
export function isOverturnedClaim(claim: Claim, now: Date = new Date()) {
  if (claim.status !== "REJECTED" || claim.currentStep <= 0) return false;

  const decidedAt = serverTime(claim.updatedAt);
  if (Number.isNaN(decidedAt)) return false;

  return now.getTime() - decidedAt <= OVERTURNED_WINDOW_DAYS * 86_400_000;
}

// Submitted in the current month and not rejected — the claims that make up
// the month's spend total. Months are UTC, matching the backend's grouping.
export function isSpendThisMonth(claim: Claim, now: Date = new Date()) {
  return isSpend(claim) && submittedMonthKey(claim) === monthKey(now);
}

// ─── Spend by project ────────────────────────────────────────────────────────

export type SpendTrend = "spike" | "typical" | "down" | "new";

export type ProjectSpendRow = {
  projectId: string | null;
  project: string;
  total: number;
  count: number;
  // Mean monthly spend across the previous BASELINE_MONTHS complete months.
  baseline: number;
  // Null when there is no baseline to compare against (nothing spent before).
  changePct: number | null;
  trend: SpendTrend;
};

function monthKey(date: Date) {
  return `${date.getUTCFullYear()}-${date.getUTCMonth()}`;
}

// The month a claim landed in, as a UTC key.
function submittedMonthKey(claim: Claim) {
  return monthKey(new Date(submittedTime(claim)));
}

// This month's spend per project, each row carrying the previous months'
// average so the admin can tell a busy month from an unusual one.
export function projectSpendThisMonth(
  claims: Claim[],
  projectNames: Map<string, string>,
  now: Date = new Date(),
): ProjectSpendRow[] {
  const thisMonth = monthKey(now);

  const baselineKeys = new Set(
    Array.from({ length: BASELINE_MONTHS }, (_, index) =>
      monthKey(new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - (index + 1), 1))),
    ),
  );

  const rows = new Map<string, ProjectSpendRow & { baselineTotal: number }>();
  const keyOf = (claim: Claim) => claim.projectId ?? UNASSIGNED_PROJECT;

  for (const claim of claims) {
    if (!isSpend(claim)) continue;

    const key = keyOf(claim);
    const month = submittedMonthKey(claim);
    const inThisMonth = month === thisMonth;
    if (!inThisMonth && !baselineKeys.has(month)) continue;

    let row = rows.get(key);
    if (!row) {
      row = {
        projectId: claim.projectId ?? null,
        project: claim.projectId
          ? projectNames.get(claim.projectId) ?? UNASSIGNED_PROJECT
          : UNASSIGNED_PROJECT,
        total: 0,
        count: 0,
        baseline: 0,
        baselineTotal: 0,
        changePct: null,
        trend: "new",
      };
      rows.set(key, row);
    }

    if (inThisMonth) {
      row.total += claim.amount;
      row.count += 1;
    } else {
      row.baselineTotal += claim.amount;
    }
  }

  return Array.from(rows.values())
    // A project that spent nothing this month has nothing to explain — the card
    // is about where money is going now.
    .filter((row) => row.count > 0)
    .map(({ baselineTotal, ...row }) => {
      const baseline = baselineTotal / BASELINE_MONTHS;
      const changePct = baseline > 0 ? ((row.total - baseline) / baseline) * 100 : null;

      return {
        ...row,
        baseline,
        changePct,
        trend: trendFor(changePct),
      };
    })
    .sort((a, b) => b.total - a.total);
}

function trendFor(changePct: number | null): SpendTrend {
  if (changePct === null) return "new";
  if (changePct >= SPIKE_PCT) return "spike";
  if (changePct <= DROP_PCT) return "down";
  return "typical";
}

// ─── Stale claims grouped by who is sitting on them ──────────────────────────

export type StuckWithApprover = {
  approver: string;
  claimIds: string[];
  amount: number;
  oldestDays: number;
  // True for the bucket holding claims with no approver on their current step.
  unassigned: boolean;
};

// The label used for claims stuck with nobody. Not a person — a routing gap.
export const NO_APPROVER = "No approver assigned";

type StaleClaimLike = {
  id: string;
  amount: number;
  daysPending: number;
  currentApprovers: string[];
};

// Groups the overview's stale claims by their current approver, worst first.
// A claim awaiting any of several approvers counts against each of them: they
// are all equally able to unblock it.
export function stuckWithApprovers(claims: StaleClaimLike[]): StuckWithApprover[] {
  const groups = new Map<string, StuckWithApprover>();

  for (const claim of claims) {
    const approvers = claim.currentApprovers.length > 0 ? claim.currentApprovers : [NO_APPROVER];

    for (const approver of approvers) {
      let group = groups.get(approver);
      if (!group) {
        group = {
          approver,
          claimIds: [],
          amount: 0,
          oldestDays: 0,
          unassigned: approver === NO_APPROVER,
        };
        groups.set(approver, group);
      }

      group.claimIds.push(claim.id);
      group.amount += claim.amount;
      group.oldestDays = Math.max(group.oldestDays, claim.daysPending);
    }
  }

  return Array.from(groups.values()).sort(
    // Unrouted claims first — nobody is coming for those on their own — then by
    // how long the worst one has been waiting.
    (a, b) =>
      Number(b.unassigned) - Number(a.unassigned) ||
      b.oldestDays - a.oldestDays ||
      b.claimIds.length - a.claimIds.length,
  );
}
