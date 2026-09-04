import { useEffect, useMemo, useState } from "react";
import type { KeyboardEvent } from "react";
import { Filter, X } from "lucide-react";
import type { Claim } from "@/features/claims/api";
import { ClaimDetailsModal } from "@/features/claims/components/ClaimDetailsModal";
import { ClaimStatusBadge } from "@/features/claims/components/ClaimStatusBadge";
import { ClaimStatusTabs } from "@/features/claims/components/ClaimStatusTabs";
import { OverLimitBadge } from "@/features/claims/components/OverLimitBadge";
import {
  CLAIMS_PAGE_SIZE,
  PaginationControls,
} from "@/features/claims/components/PaginationControls";
import { formatCurrency, formatShortDate } from "@/features/claims/lib/claim-formatters";
import { claimMatchesStatus, type ClaimStatusFilter } from "@/features/claims/lib/claim-status";
import {
  claimAgeDays,
  isPendingClaim,
  STALE_AFTER_DAYS,
  sumAmount,
} from "@/features/claims/lib/claim-insights";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
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

// Every claim in the org, and the place every number on the attention tab lands.
// Arriving here from a card carries that card's subset with it, named on a
// banner — so the admin always knows which question the rows are answering.

export const ALL_PROJECTS = "ALL";

const COLUMNS = ["Employee", "Claim", "Project", "Submitted", "Waiting", "Amount", "Status"];

export function AdminClaimsTable({
  claims,
  loading,
  error,
  drilldown,
  onClearDrilldown,
  status,
  onStatusChange,
  search,
  onSearchChange,
  projectId,
  onProjectChange,
  projectNames,
  employeeEmails,
  accountLabels,
}: {
  claims: Claim[];
  loading: boolean;
  error: string | null;
  drilldown: ClaimDrilldown | null;
  onClearDrilldown: () => void;
  status: ClaimStatusFilter;
  onStatusChange: (status: ClaimStatusFilter) => void;
  search: string;
  onSearchChange: (search: string) => void;
  projectId: string;
  onProjectChange: (projectId: string) => void;
  projectNames: Map<string, string>;
  employeeEmails: Map<string, string>;
  accountLabels: Map<string, string>;
}) {
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<Claim | null>(null);

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
    const query = search.trim().toLowerCase();

    return claims
      .filter((claim) => {
        if (drilldown && !drilldown.matches(claim)) return false;
        if (!claimMatchesStatus(claim, status)) return false;
        if (projectId !== ALL_PROJECTS && (claim.projectId ?? "") !== projectId) return false;
        if (query.length === 0) return true;

        return [
          claim.claimNumber,
          claim.title,
          employeeName(claim),
          employeeEmail(claim),
          claim.category,
          projectLabel(claim),
        ]
          .filter(Boolean)
          .join(" ")
          .toLowerCase()
          .includes(query);
      })
      .sort((a, b) => (b.submittedAt || b.spentAt).localeCompare(a.submittedAt || a.spentAt));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [claims, drilldown, status, projectId, search, projectNames, employeeEmails]);

  useEffect(() => {
    setPage(1);
  }, [search, status, projectId, drilldown]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / CLAIMS_PAGE_SIZE));
  useEffect(() => {
    if (page > totalPages) setPage(totalPages);
  }, [page, totalPages]);

  const paginated = useMemo(
    () => filtered.slice((page - 1) * CLAIMS_PAGE_SIZE, page * CLAIMS_PAGE_SIZE),
    [filtered, page],
  );

  const hasFilters =
    status !== "ALL" || search.trim().length > 0 || projectId !== ALL_PROJECTS || drilldown !== null;

  function clearAll() {
    onStatusChange("ALL");
    onSearchChange("");
    onProjectChange(ALL_PROJECTS);
    onClearDrilldown();
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

        <section className={`${CARD_BARE} p-5 sm:p-6`}>
          <div className="flex flex-col gap-4 sm:flex-row sm:items-center">
            <SearchInput
              value={search}
              onChange={onSearchChange}
              placeholder="Search by claim, employee, or project"
              className="sm:max-w-sm"
              inputClassName="h-12"
            />
            <Select value={projectId} onValueChange={onProjectChange}>
              <SelectTrigger className="h-12 rounded-2xl bg-card sm:max-w-[240px]">
                <SelectValue placeholder="All projects" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL_PROJECTS}>All projects</SelectItem>
                {Array.from(projectNames.entries()).map(([id, name]) => (
                  <SelectItem key={id} value={id}>
                    {name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <ClaimStatusTabs value={status} onChange={onStatusChange} className="mt-4" />

          <div className="mt-4 flex flex-col gap-2 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
            <p>
              Showing <span className="font-semibold text-foreground">{filtered.length}</span> of{" "}
              <span className="font-semibold text-foreground">{claims.length}</span> claims ·{" "}
              <span className="font-semibold text-foreground">
                {formatCurrency(sumAmount(filtered))}
              </span>
            </p>
            {hasFilters ? (
              <button
                type="button"
                onClick={clearAll}
                className="w-fit rounded-full border border-border/60 bg-card px-4 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground"
              >
                Clear filters
              </button>
            ) : null}
          </div>
        </section>

        {loading ? (
          <section className={`${CARD_BARE} p-6 text-sm text-muted-foreground`}>
            Loading claims…
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
                        className="h-12 px-4 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground first:pl-6 last:pr-6"
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
                      <td className="p-4 pr-6 align-middle">
                        <div className="flex flex-col items-start gap-1.5">
                          <ClaimStatusBadge status={claim.status} />
                          {claim.exceedsLimit ? <OverLimitBadge /> : null}
                        </div>
                      </td>
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
