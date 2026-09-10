import { useCallback, useEffect, useMemo, useState } from "react";
import {
  getAllClaims,
  type Claim,
  type ClaimsExportFilters,
} from "@/features/claims/api";
import { isStaleClaim } from "@/features/claims/lib/claim-insights";
import { getEmployees } from "@/features/employees/api";
import { getAccounts, getProjects } from "@/features/settings/api";
import { OverflowTabList } from "@/shared/components/OverflowTabList";
import { getAdminOverview, type AdminOverview } from "../api";
import type { ClaimDrilldown } from "../lib/claims-drilldown";
import {
  describeFilters,
  toExportFilters,
  EMPTY_FILTERS,
  type ClaimsFilters,
} from "../lib/claims-filters";
import { AdminClaimsAttention } from "./AdminClaimsAttention";
import { AdminClaimsTable } from "./AdminClaimsTable";
import { ClaimSettings } from "./ClaimSettings";
import { ClaimsMonthEndActions } from "./ClaimsMonthEndActions";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonCards, SkeletonStats } from "@/shared/components/Skeleton";

// The claims admin dashboard, in the order an admin needs it: what requires a
// decision, then what is owed, then every claim behind both.
//
// The first tab is labelled "Overview" but is deliberately NOT a summary of
// totals — it leads with what is late and with whom. Its badge carries the
// stale count so the tab itself says whether anything needs looking at.

type ClaimsTab = "overview" | "all" | "settings";

export function AdminClaims() {
  const [claims, setClaims] = useState<Claim[]>([]);
  const [overview, setOverview] = useState<AdminOverview | null>(null);
  const [projectNames, setProjectNames] = useState<Map<string, string>>(new Map());
  const [employeeEmails, setEmployeeEmails] = useState<Map<string, string>>(new Map());
  const [accountLabels, setAccountLabels] = useState<Map<string, string>>(new Map());
  const [error, setError] = useState<string | null>(null);

  const [tab, setTab] = useState<ClaimsTab>("overview");
  const [drilldown, setDrilldown] = useState<ClaimDrilldown | null>(null);
  const [filters, setFilters] = useState<ClaimsFilters>(EMPTY_FILTERS);

  // Claims are the page; the rest are labels. A missing label list degrades to
  // ids rather than failing the whole dashboard, so only the claims query
  // gates `loading` or raises an error.
  const claimsQuery = useCachedQuery("/claims/all", getAllClaims);
  const overviewQuery = useCachedQuery("/admin/overview", getAdminOverview);
  const projectsQuery = useCachedQuery("/projects", getProjects);
  const employeesQuery = useCachedQuery("/employees", getEmployees);
  const accountsQuery = useCachedQuery("/accounts", getAccounts);
  const loading = claimsQuery.loading;

  // load() stays for the refetch after a decision — the four label lists are
  // unaffected by approving a claim, so it only re-reads the claims.
  const load = useCallback(() => claimsQuery.refresh(), [claimsQuery.refresh]);

  useEffect(() => {
    if (claimsQuery.data) setClaims(claimsQuery.data);
  }, [claimsQuery.data]);
  useEffect(() => {
    setOverview(overviewQuery.data ?? null);
  }, [overviewQuery.data]);
  useEffect(() => {
    const projects = projectsQuery.data ?? [];
    setProjectNames(new Map(projects.map((project) => [project.id, project.name])));
  }, [projectsQuery.data]);
  useEffect(() => {
    const employees = employeesQuery.data ?? [];
    setEmployeeEmails(new Map(employees.map((employee) => [employee.id, employee.email])));
  }, [employeesQuery.data]);
  useEffect(() => {
    const accounts = accountsQuery.data ?? [];
    setAccountLabels(
      new Map(accounts.map((account) => [account.id, `${account.code} · ${account.name}`])),
    );
  }, [accountsQuery.data]);
  useEffect(() => {
    if (claimsQuery.error) setError(claimsQuery.error);
  }, [claimsQuery.error]);

  const staleCount = useMemo(() => claims.filter((claim) => isStaleClaim(claim)).length, [claims]);

  // Clicking a number opens the claims behind it — and drops any status filter
  // that would silently hide some of them.
  function openDrilldown(next: ClaimDrilldown) {
    setDrilldown(next);
    // Clear the filters too: a status or date left over from a previous look
    // would silently hide part of the set the admin just clicked on.
    setFilters(EMPTY_FILTERS);
    setTab("all");
  }

  const exportFilters: ClaimsExportFilters = useMemo(
    () => toExportFilters(filters),
    [filters],
  );

  const filterSummary = useMemo(() => {
    const base = describeFilters(filters, projectNames, employeeEmails);

    // Be straight about it: the export speaks the API's filters, not the
    // client-side subset a card click produced.
    return drilldown ? `${base} — a drill-through view isn't part of the export` : base;
  }, [filters, projectNames, employeeEmails, drilldown]);

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between sm:gap-6">
        <OverflowTabList<ClaimsTab>
          items={[
            { id: "overview", label: "Overview", badge: staleCount },
            { id: "all", label: "All claims" },
            { id: "settings", label: "Settings" },
          ]}
          value={tab}
          onChange={setTab}
          // sm:flex-1 matters: without it this flex item shrink-wraps to its
          // own content, so OverflowTabList measures its tabs against a box
          // sized BY those tabs and sits permanently on the fit/collapse
          // boundary — sub-pixel font differences then decide whether the last
          // tab collapses into the overflow menu. AdminLeave already passes this.
          className="sm:max-w-md sm:flex-1"
          ariaLabel="Claims dashboard views"
        />

        {/* Export/Import act on the claims themselves, so they are hidden on the
            settings tab rather than offering to export a form. */}
        <div className={`shrink-0 pb-1 ${tab === "settings" ? "hidden" : ""}`}>
          <ClaimsMonthEndActions
            filters={exportFilters}
            filterSummary={filterSummary}
          />
        </div>
      </div>

      {tab === "settings" ? (
        <ClaimSettings />
      ) : tab === "overview" ? (
        error ? (
          <section className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
            Error: {error}
          </section>
        ) : loading ? (
          <div className="space-y-4">
            <SkeletonStats count={3} />
            <SkeletonCards count={3} />
          </div>
        ) : (
          <AdminClaimsAttention
            claims={claims}
            overview={overview}
            projectNames={projectNames}
            onDrill={openDrilldown}
          />
        )
      ) : (
        <AdminClaimsTable
          claims={claims}
          loading={loading}
          error={error}
          drilldown={drilldown}
          onClearDrilldown={() => setDrilldown(null)}
          filters={filters}
          onFiltersChange={setFilters}
          projectNames={projectNames}
          employeeEmails={employeeEmails}
          accountLabels={accountLabels}
          onDecided={() => void load()}
        />
      )}
    </div>
  );
}
