import { useEffect, useMemo, useState } from "react";
import type { KeyboardEvent } from "react";
import {
  CheckCircle2,
  ChevronDown,
  Filter,
  LoaderCircle,
  SlidersHorizontal,
  TriangleAlert,
  X,
} from "lucide-react";
import { syncClaimToXero, type Claim } from "@/features/claims/api";
import { ClaimDetailsModal } from "@/features/claims/components/ClaimDetailsModal";
import { ClaimStatusBadge } from "@/features/claims/components/ClaimStatusBadge";
import { ClaimStatusTabs } from "@/features/claims/components/ClaimStatusTabs";
import { OverLimitBadge } from "@/features/claims/components/OverLimitBadge";
import {
  CLAIMS_PAGE_SIZE,
  PaginationControls,
} from "@/features/claims/components/PaginationControls";
import { formatCurrency, formatShortDate } from "@/features/claims/lib/claim-formatters";
import {
  claimAgeDays,
  isPendingClaim,
  STALE_AFTER_DAYS,
  sumAmount,
} from "@/features/claims/lib/claim-insights";
import { buildName, displayPerson } from "@/features/employee-portal/lib/employee-formatters";
import { SearchInput } from "@/shared/components/SearchInput";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { CARD_BARE } from "../lib/dashboard-styles";
import type { ClaimDrilldown } from "../lib/claims-drilldown";
import {
  ALL,
  EMPTY_FILTERS,
  activeAdvancedCount,
  hasAnyFilter,
  matchesFilters,
  type ClaimsFilters,
} from "../lib/claims-filters";
import { SkeletonRows } from "@/shared/components/Skeleton";

// Every claim in the org, and the place every number on the attention tab lands.
// Arriving here from a card carries that card's subset with it, named on a
// banner — so the admin always knows which question the rows are answering.

// Kept as an alias so callers that already import ALL_PROJECTS keep working.
export const ALL_PROJECTS = ALL;

const CONTROL =
  "h-11 w-full rounded-2xl border border-border/70 bg-card px-3 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2";
// The date row reads as a sentence — "Date spent · From … To …" — so its labels
// sit inline at label size rather than stacked above full-width inputs.
const DATE_LABEL = "text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground";
const DATE_INPUT =
  "h-11 rounded-2xl border border-border/70 bg-card px-3 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2";


// "Action" covers two different jobs depending on the row: decide a pending
// claim, or choose how an approved one gets settled.
const COLUMNS = ["Employee", "Claim", "Project", "Submitted", "Waiting", "Amount", "Status", "Action"];

export function AdminClaimsTable({
  claims,
  loading,
  error,
  drilldown,
  onClearDrilldown,
  filters,
  onFiltersChange,
  projectNames,
  employeeEmails,
  accountLabels,
  onDecided,
}: {
  claims: Claim[];
  loading: boolean;
  error: string | null;
  drilldown: ClaimDrilldown | null;
  onClearDrilldown: () => void;
  filters: ClaimsFilters;
  onFiltersChange: (filters: ClaimsFilters) => void;
  projectNames: Map<string, string>;
  employeeEmails: Map<string, string>;
  accountLabels: Map<string, string>;
  // A decision changes the claim server-side, so the page re-reads rather than
  // this table patching a row it does not own.
  onDecided: () => void;
}) {
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<Claim | null>(null);
  // Collapsed by default. The panel is far denser than it was — no label rows,
  // dates on one line — but six controls on screen still competes with the
  // claims themselves, which are what the page is for.
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [actionError, setDecideError] = useState<string | null>(null);

  async function syncToXero(claim: Claim) {
    setBusyId(claim.id);
    setDecideError(null);
    try {
      await syncClaimToXero(claim.id);   // stage comes from Claims → Settings
      onDecided();
    } catch (e) {
      setDecideError(e instanceof Error ? e.message : "Could not push the claim to Xero.");
    } finally {
      setBusyId(null);
    }
  }

  // This table never decides a claim. An admin is oversight, not a link in the
  // chain of command — they hold no seat in any approval chain, so there is no
  // claim here that is theirs to approve. What an admin needs from this column
  // is different: who a claim is waiting on, when nobody can move it, and how
  // an approved one gets paid.
  //
  // (Approve and Reject used to live here, gated on the viewer's own canAct.
  // They could never render, because the router leaves administrative seats out
  // of every chain — see AdminNeverApprovesTests. Deciding happens in the
  // approver's own queue, which is where the seat is.)
  function rowActions(claim: Claim) {
    // Approved: the decision left is how it gets PAID, which is where the old
    // "Ready to pay" tab's job now lives. The ROUTE is not chosen here — it is
    // an org policy under Settings, stamped on the claim when it was created —
    // so this shows which route the claim is on, plus the one action it needs.
    if (claim.status === "APPROVED") {
      return <ClaimPayout claim={claim} busy={busyId === claim.id} onSync={syncToXero} />;
    }

    if (claim.status !== "PENDING") return <span className="text-muted-foreground">—</span>;

    const waiting = claim.awaitingApprovers ?? [];

    // No approver at the step it reached: a routing fault, not a queue. An
    // admin is the person who can fix it, so it is called out here loudest.
    if (waiting.length === 0) {
      return <span className="text-xs font-semibold text-destructive">Nobody can approve</span>;
    }

    // Wraps within the column rather than widening it. `title` keeps the full
    // list reachable when several approvers push it onto three lines.
    const names = waiting.map(displayPerson).join(", ");
    return (
      <span
        title={`Waiting on ${names}`}
        className="block text-xs leading-snug text-muted-foreground"
      >
        With {names}
      </span>
    );
  }

  const set = <K extends keyof ClaimsFilters>(key: K, value: ClaimsFilters[K]) =>
    onFiltersChange({ ...filters, [key]: value });

  const employeeName = (claim: Claim) => {
    const email = claim.employeeEmail ?? employeeEmails.get(claim.employeeId);
    return email ? buildName(email) : claim.employeeId;
  };
  const employeeEmail = (claim: Claim) =>
    claim.employeeEmail ?? employeeEmails.get(claim.employeeId) ?? "";
  const projectLabel = (claim: Claim) =>
    claim.projectId ? projectNames.get(claim.projectId) ?? "Unassigned" : "Unassigned";
  const accountLabel = (claim: Claim) =>
    claim.chartOfAccountId
      ? accountLabels.get(claim.chartOfAccountId) ?? "Not assigned"
      : "Not assigned";

  const filtered = useMemo(() => {
    const labels = { projectName: projectLabel, employeeEmail };

    return claims
      .filter((claim) => {
        if (drilldown && !drilldown.matches(claim)) return false;
        return matchesFilters(claim, filters, labels);
      })
      .sort((a, b) => (b.submittedAt || b.spentAt).localeCompare(a.submittedAt || a.spentAt));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [claims, drilldown, filters, projectNames, employeeEmails]);

  useEffect(() => {
    setPage(1);
  }, [filters, drilldown]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / CLAIMS_PAGE_SIZE));
  useEffect(() => {
    if (page > totalPages) setPage(totalPages);
  }, [page, totalPages]);

  const paginated = useMemo(
    () => filtered.slice((page - 1) * CLAIMS_PAGE_SIZE, page * CLAIMS_PAGE_SIZE),
    [filtered, page],
  );

  // The figure an admin is usually here for: how much of what they filtered to
  // still needs a decision.
  const pendingCount = filtered.filter(isPendingClaim).length;
  const advancedCount = activeAdvancedCount(filters);
  const hasFilters = hasAnyFilter(filters) || drilldown !== null;

  function clearAll() {
    onFiltersChange(EMPTY_FILTERS);
    onClearDrilldown();
    setFiltersOpen(false);
  }

  function openOnKey(event: KeyboardEvent, claim: Claim) {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      setSelected(claim);
    }
  }

  // The age of a decided claim is history; only a claim still waiting has a
  // number anyone can act on.
  function waiting(claim: Claim) {
    if (!isPendingClaim(claim)) return <span className="text-muted-foreground">—</span>;

    const days = claimAgeDays(claim);
    return (
      <span
        className={`font-bold tabular-nums ${
          days >= STALE_AFTER_DAYS ? "text-tertiary" : "text-muted-foreground"
        }`}
      >
        {days}d
      </span>
    );
  }

  return (
    <>
      <div className="space-y-4 sm:space-y-6">
        {drilldown ? (
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-[24px] border border-primary/30 bg-primary/5 px-5 py-4">
            <div className="flex min-w-0 items-center gap-3">
              <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
                <Filter className="h-4 w-4" />
              </span>
              <div className="min-w-0">
                <p className="truncate text-sm font-bold text-foreground">
                  {drilldown.label}
                  {drilldown.detail ? (
                    <span className="font-medium text-muted-foreground"> · {drilldown.detail}</span>
                  ) : null}
                </p>
                <p className="text-xs text-muted-foreground">
                  {filtered.length} claim{filtered.length === 1 ? "" : "s"} ·{" "}
                  {formatCurrency(sumAmount(filtered))}
                </p>
              </div>
            </div>
            <button
              type="button"
              onClick={onClearDrilldown}
              className="inline-flex h-9 items-center gap-1.5 rounded-full border border-border/60 bg-card px-3.5 text-xs font-bold text-muted-foreground transition hover:text-foreground"
            >
              <X className="h-3.5 w-3.5" />
              Show all claims
            </button>
          </div>
        ) : null}

        {/* Filters follow the production claims screen: one full-width search,
            then controls that say what they do instead of carrying a label row
            above them. Dropping the labels and compacting the dates is what
            makes it affordable to show every filter at once — the old version
            hid them behind a "Filters" toggle precisely because six labelled
            fields pushed the claims off the screen. */}
        <section className={`${CARD_BARE} space-y-3 p-5 sm:p-6`}>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
            <SearchInput
              value={filters.search}
              onChange={(value) => set("search", value)}
              placeholder="Search by claim, employee, or project"
              className="sm:flex-1"
              inputClassName="h-12"
            />

            <button
              type="button"
              aria-expanded={filtersOpen}
              aria-controls="claims-filter-panel"
              onClick={() => setFiltersOpen((open) => !open)}
              className={`inline-flex h-12 shrink-0 items-center gap-2 rounded-2xl border px-4 text-sm font-bold shadow-sm transition ${
                filtersOpen || advancedCount > 0
                  ? "border-primary/40 bg-primary/5 text-primary"
                  : "border-border/70 bg-card text-muted-foreground hover:text-foreground"
              }`}
            >
              <SlidersHorizontal className="h-4 w-4" />
              Filters
              {/* The count is what makes collapsing safe: a filter you cannot
                  see is still narrowing the list, and this says so. */}
              {advancedCount > 0 ? (
                <span className="flex h-5 min-w-5 items-center justify-center rounded-full bg-primary px-1.5 text-[11px] font-black text-primary-foreground">
                  {advancedCount}
                </span>
              ) : null}
              <ChevronDown
                className={`h-4 w-4 transition-transform ${filtersOpen ? "rotate-180" : ""}`}
              />
            </button>
          </div>

          <div id="claims-filter-panel" hidden={!filtersOpen} className="space-y-3">
          <div className="grid gap-3 sm:grid-cols-3">
            <Select value={filters.projectId} onValueChange={(v) => set("projectId", v)}>
              <SelectTrigger className={CONTROL} aria-label="Project">
                <SelectValue placeholder="All projects" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL}>All projects</SelectItem>
                {Array.from(projectNames.entries()).map(([id, name]) => (
                  <SelectItem key={id} value={id}>
                    {name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <Select value={filters.employeeId} onValueChange={(v) => set("employeeId", v)}>
              <SelectTrigger className={CONTROL} aria-label="Employee">
                <SelectValue placeholder="Everyone" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL}>Everyone</SelectItem>
                {Array.from(employeeEmails.entries()).map(([id, email]) => (
                  <SelectItem key={id} value={id}>
                    {buildName(email)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <Select value={filters.paymentType} onValueChange={(v) => set("paymentType", v)}>
              <SelectTrigger className={CONTROL} aria-label="Paid with">
                <SelectValue placeholder="Any source" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL}>Any source</SelectItem>
                {/* PERSONAL is money the org owes back; COMPANY already left a
                    company account. */}
                <SelectItem value="PERSONAL">Own money</SelectItem>
                <SelectItem value="COMPANY">Company money</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {/* Which date the range applies to. Finance reconciles on spend
                date, payroll on submission date — so it sits with the dates it
                governs rather than in the row of entity filters above. */}
            <Select
              value={filters.dateBasis}
              onValueChange={(v) => set("dateBasis", v as "spent" | "submitted")}
            >
              <SelectTrigger className={`${CONTROL} w-auto min-w-[9.5rem]`} aria-label="Date counted as">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="spent">Date spent</SelectItem>
                <SelectItem value="submitted">Date submitted</SelectItem>
              </SelectContent>
            </Select>

            <span className={DATE_LABEL}>From</span>
            <input
              type="date"
              aria-label="From date"
              value={filters.from}
              max={filters.to || undefined}
              onChange={(event) => set("from", event.target.value)}
              className={DATE_INPUT}
            />

            <span className={DATE_LABEL}>To</span>
            <input
              type="date"
              aria-label="To date"
              value={filters.to}
              min={filters.from || undefined}
              onChange={(event) => set("to", event.target.value)}
              className={DATE_INPUT}
            />

            {hasFilters ? (
              <button
                type="button"
                onClick={clearAll}
                className="ml-auto inline-flex h-11 items-center gap-1.5 rounded-full border border-border/60 bg-card px-4 text-xs font-bold text-muted-foreground transition-colors hover:text-foreground"
              >
                <X className="h-3.5 w-3.5" />
                Clear filters
              </button>
            ) : null}
          </div>
          </div>

          <ClaimStatusTabs value={filters.status} onChange={(value) => set("status", value)} />

          {/* What the filters left, and what it means — production puts the
              headline figures out to the right rather than folding one total
              into the sentence, and "how many still need a decision" is the
              number an admin is usually here for. */}
          <div className="flex flex-col gap-2 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
            <p>
              Showing <span className="font-semibold text-foreground">{filtered.length}</span> of{" "}
              <span className="font-semibold text-foreground">{claims.length}</span> claims
            </p>
            <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs sm:text-sm">
              <span>
                <span className="font-semibold text-foreground">{pendingCount}</span> need review
              </span>
              <span aria-hidden>·</span>
              <span>
                <span className="font-semibold text-foreground">
                  {formatCurrency(sumAmount(filtered))}
                </span>{" "}
                total
              </span>
            </p>
          </div>
        </section>

        {actionError ? (
          <section className="flex items-start justify-between gap-3 rounded-[28px] border border-destructive/20 bg-destructive/5 p-4">
            {/* break-words and a height cap: the backend now sends one sentence,
                but an unrecognised payload still falls back to a trimmed body,
                and that must not be able to push the claims table off screen. */}
            <p className="nice-scrollbar max-h-32 overflow-y-auto break-words text-sm font-medium text-destructive">
              {actionError}
            </p>
            <button
              type="button"
              onClick={() => setDecideError(null)}
              aria-label="Dismiss error"
              className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-destructive/70 transition hover:bg-destructive/10 hover:text-destructive"
            >
              <X className="h-3.5 w-3.5" />
            </button>
          </section>
        ) : null}

        {loading ? (
          <section className={CARD_BARE}>
            <table className="w-full text-sm">
              <tbody>
                <SkeletonRows rows={6} widths={["w-36", "w-44", "w-24", "w-20", "w-16"]} />
              </tbody>
            </table>
          </section>
        ) : null}

        {error ? (
          <section className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
            Error: {error}
          </section>
        ) : null}

        {!loading && !error && filtered.length === 0 ? (
          <section className={`${CARD_BARE} p-8 text-center`}>
            <p className="text-lg font-bold text-foreground">No claims match this filter.</p>
            <p className="mt-2 text-sm text-muted-foreground">
              Try a different status, or clear the filters to see every claim.
            </p>
          </section>
        ) : null}

        {/* Mobile cards */}
        {!loading && !error && filtered.length > 0 ? (
          <div className="grid gap-3 md:hidden">
            {paginated.map((claim) => (
              <article
                key={claim.id}
                role="button"
                tabIndex={0}
                onClick={() => setSelected(claim)}
                onKeyDown={(event) => openOnKey(event, claim)}
                className={`${CARD_BARE} cursor-pointer space-y-4 p-4 transition hover:border-primary/40 focus-visible:border-primary/50 focus-visible:outline-none`}
              >
                <div className="flex items-start justify-between gap-4">
                  <div className="min-w-0">
                    <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                      {claim.claimNumber}
                    </p>
                    <p className="mt-1 text-base font-black text-foreground">{claim.title}</p>
                    <p className="text-sm text-muted-foreground">{employeeName(claim)}</p>
                  </div>
                  <div className="flex shrink-0 flex-col items-end gap-1.5">
                    <ClaimStatusBadge status={claim.status} />
                    {claim.exceedsLimit ? <OverLimitBadge /> : null}
                  </div>
                </div>
                <div className="grid grid-cols-3 gap-3 rounded-2xl bg-surface-low p-4">
                  <div>
                    <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                      Project
                    </p>
                    <p className="mt-1 truncate text-sm font-semibold text-foreground">
                      {projectLabel(claim)}
                    </p>
                  </div>
                  <div>
                    <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                      Waiting
                    </p>
                    <p className="mt-1 text-sm">{waiting(claim)}</p>
                  </div>
                  <div>
                    <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">
                      Amount
                    </p>
                    <p className="mt-1 text-sm font-semibold text-foreground">
                      {formatCurrency(claim.amount, claim.currency)}
                    </p>
                  </div>
                </div>
              </article>
            ))}
          </div>
        ) : null}

        {/* Desktop table */}
        {!loading && !error && filtered.length > 0 ? (
          <section className={`hidden md:block ${CARD_BARE}`}>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[960px] caption-bottom text-sm">
                <thead>
                  <tr className="border-b border-border/60">
                    {COLUMNS.map((column) => (
                      <th
                        key={column}
                        // The last column holds either buttons or a list of
                        // approver names. Left unbounded, a day with three
                        // approvers stretched the table past the viewport and
                        // pushed everything behind a horizontal scrollbar.
                        className={`h-12 px-4 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground first:pl-6 last:pr-6 ${
                          column === "Action" ? "w-[200px]" : ""
                        }`}
                      >
                        {column}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {paginated.map((claim) => (
                    <tr
                      key={claim.id}
                      tabIndex={0}
                      onClick={() => setSelected(claim)}
                      onKeyDown={(event) => openOnKey(event, claim)}
                      className="cursor-pointer border-b border-border/60 transition-colors hover:bg-muted/70 focus-visible:bg-muted/70 focus-visible:outline-none"
                    >
                      <td className="p-4 pl-6 align-middle">
                        <p className="font-bold text-foreground">{employeeName(claim)}</p>
                        <p className="text-xs text-muted-foreground">{employeeEmail(claim)}</p>
                      </td>
                      <td className="p-4 align-middle">
                        <p className="text-xs uppercase tracking-[0.18em] text-muted-foreground">
                          {claim.claimNumber}
                        </p>
                        <p className="mt-1 font-bold text-foreground">{claim.title}</p>
                      </td>
                      <td className="p-4 align-middle text-muted-foreground">
                        {projectLabel(claim)}
                      </td>
                      <td className="p-4 align-middle">
                        {formatShortDate(claim.submittedAt || claim.spentAt)}
                      </td>
                      <td className="p-4 align-middle">{waiting(claim)}</td>
                      <td className="p-4 align-middle font-semibold text-foreground">
                        {formatCurrency(claim.amount, claim.currency)}
                      </td>
                      <td className="p-4 align-middle">
                        <div className="flex flex-col items-start gap-1.5">
                          <ClaimStatusBadge status={claim.status} />
                          {claim.exceedsLimit ? <OverLimitBadge /> : null}
                        </div>
                      </td>
                      <td className="w-[200px] p-4 pr-6 align-middle">{rowActions(claim)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <PaginationControls
              className="flex flex-col gap-3 px-5 pb-5 sm:flex-row sm:items-center sm:justify-between sm:px-6 sm:pb-6"
              currentPage={page}
              totalItems={filtered.length}
              onPageChange={setPage}
            />
          </section>
        ) : null}

        {!loading && !error && filtered.length > 0 ? (
          <div className="md:hidden">
            <PaginationControls
              className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"
              currentPage={page}
              totalItems={filtered.length}
              onPageChange={setPage}
            />
          </div>
        ) : null}
      </div>

      {selected ? (
        <ClaimDetailsModal
          claim={selected}
          accountLabel={accountLabel(selected)}
          projectLabel={projectLabel(selected)}
          employeeLabel={employeeName(selected)}
          onClose={() => setSelected(null)}
        />
      ) : null}
    </>
  );
}

// Where an approved claim's money went — a status, not a to-do list.
//
// Approval settles the claim on its own (ClaimsService.SettleAsync), so there is
// no "Sync to Xero" button in the happy path: by the time a row reads APPROVED
// the push has already been attempted. The only action left is a retry, and it
// only appears when that attempt actually failed.
//
// The route itself is set once under Claims → Settings and stamped onto each
// claim at creation. A per-row picker invited exactly the mistake the guards
// exist to stop — re-routing a claim already billed in Xero, and paying the
// same receipt twice.
function ClaimPayout({
  claim,
  busy,
  onSync,
}: {
  claim: Claim;
  busy: boolean;
  onSync: (claim: Claim) => void;
}) {
  if (claim.xeroSyncStatus === "SYNCED") {
    return (
      <span className="inline-flex items-center gap-1.5 text-xs font-semibold text-secondary-foreground">
        <CheckCircle2 className="h-3.5 w-3.5 shrink-0" />
        In Xero{claim.xeroBillRef ? ` · ${claim.xeroBillRef}` : ""}
      </span>
    );
  }

  // Nothing to push on the payroll route — it is collected by the payroll
  // reimbursement export under Export, so a button here would do nothing.
  if (claim.settlement === "PAYROLL") {
    return <span className="text-xs text-muted-foreground">In the payroll run</span>;
  }

  // The push failed — this is the one case that still needs a human, because
  // the fix is usually elsewhere (recode the account, connect Xero, subscribe
  // to the currency) and only then is a retry worth anything.
  if (claim.xeroSyncStatus === "ERROR") {
    return (
      <div className="space-y-1" onClick={(event) => event.stopPropagation()}>
        <button
          type="button"
          disabled={busy}
          onClick={() => onSync(claim)}
          className="inline-flex items-center gap-1.5 rounded-full bg-destructive/10 px-3 py-1.5 text-xs font-semibold text-destructive transition hover:bg-destructive/20 disabled:opacity-50"
        >
          {busy ? <LoaderCircle className="h-3 w-3 animate-spin" /> : null}
          Retry Xero sync
        </button>

        {claim.xeroSyncError ? (
          <p className="flex items-start gap-1 text-xs font-medium text-destructive">
            <TriangleAlert className="mt-0.5 h-3 w-3 shrink-0" />
            {claim.xeroSyncError}
          </p>
        ) : null}
      </div>
    );
  }

  // Approved, routed to Xero, no bill and no error yet.
  //
  // Do NOT read a cause into this. It covers claims approved before approval
  // started settling them, claims approved while Xero was disconnected, and
  // claims whose push has not returned — SettleAsync deliberately leaves all of
  // them NOT_SYNCED rather than marking them failed. An earlier version of this
  // asserted "Xero not connected" and was simply wrong for the backlog rows.
  //
  // These are the one case that still wants a manual push, and that does not
  // contradict the settle-on-approval rule: nothing approved from now on lands
  // here, so the button is for catching up, not for the normal path.
  return (
    <div className="space-y-1" onClick={(event) => event.stopPropagation()}>
      <button
        type="button"
        disabled={busy}
        onClick={() => onSync(claim)}
        className="inline-flex items-center gap-1.5 rounded-full border border-border/60 bg-card px-3 py-1.5 text-xs font-semibold text-muted-foreground transition hover:border-primary/40 hover:text-primary disabled:opacity-50"
      >
        {busy ? <LoaderCircle className="h-3 w-3 animate-spin" /> : null}
        Push to Xero
      </button>
      <p className="text-xs text-muted-foreground">Not in Xero yet</p>
    </div>
  );
}
