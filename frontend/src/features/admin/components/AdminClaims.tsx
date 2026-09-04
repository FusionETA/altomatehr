import { useCallback, useEffect, useMemo, useState } from "react";
import {
  getAllClaims,
  type Claim,
  type ClaimsExportFilters,
  type ClaimsImportResult,
} from "@/features/claims/api";
import { isReadyToPay, isStaleClaim, sumAmount } from "@/features/claims/lib/claim-insights";
import type { ClaimStatusFilter } from "@/features/claims/lib/claim-status";
import { formatCurrency } from "@/features/claims/lib/claim-formatters";
import { getEmployees } from "@/features/employees/api";
import { getAccounts, getProjects } from "@/features/settings/api";
import { OverflowTabList } from "@/shared/components/OverflowTabList";
import { getAdminOverview, type AdminOverview } from "../api";
import type { ClaimDrilldown } from "../lib/claims-drilldown";
import { AdminClaimsAttention } from "./AdminClaimsAttention";
import { AdminClaimsReadyToPay } from "./AdminClaimsReadyToPay";
import { ALL_PROJECTS, AdminClaimsTable } from "./AdminClaimsTable";
import { ClaimsImportReport, ClaimsMonthEndActions } from "./ClaimsMonthEndActions";

// The claims admin dashboard, in the order an admin needs it: what requires a
// decision, then what is owed, then every claim behind both.
//
// The first tab is labelled "Overview" but is deliberately NOT a summary of
// totals — it leads with what is late and with whom. Its badge carries the
// stale count so the tab itself says whether anything needs looking at.

type ClaimsTab = "overview" | "pay" | "all";

export function AdminClaims() {
  const [claims, setClaims] = useState<Claim[]>([]);
  const [overview, setOverview] = useState<AdminOverview | null>(null);
  const [projectNames, setProjectNames] = useState<Map<string, string>>(new Map());
  const [employeeEmails, setEmployeeEmails] = useState<Map<string, string>>(new Map());
  const [accountLabels, setAccountLabels] = useState<Map<string, string>>(new Map());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [tab, setTab] = useState<ClaimsTab>("overview");
  const [importReport, setImportReport] = useState<ClaimsImportResult | null>(null);
  const [drilldown, setDrilldown] = useState<ClaimDrilldown | null>(null);
  const [status, setStatus] = useState<ClaimStatusFilter>("ALL");
  const [search, setSearch] = useState("");
  const [projectId, setProjectId] = useState(ALL_PROJECTS);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);

    // Claims are the page; the rest are labels. A missing label list degrades to
    // ids rather than failing the whole dashboard.
    return Promise.all([
      getAllClaims(),
      getAdminOverview().catch(() => null),
      getProjects().catch(() => []),
      getEmployees().catch(() => []),
      getAccounts().catch(() => []),
    ])
      .then(([allClaims, adminOverview, projects, employees, accounts]) => {
        setClaims(allClaims);
        setOverview(adminOverview);
        setProjectNames(new Map(projects.map((project) => [project.id, project.name])));
        setEmployeeEmails(new Map(employees.map((employee) => [employee.id, employee.email])));
        setAccountLabels(
          new Map(accounts.map((account) => [account.id, `${account.code} · ${account.name}`])),
        );
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const staleCount = useMemo(() => claims.filter((claim) => isStaleClaim(claim)).length, [claims]);
  const readyToPay = useMemo(() => claims.filter(isReadyToPay), [claims]);

  // Clicking a number opens the claims behind it — and drops any status filter
  // that would silently hide some of them.
  function openDrilldown(next: ClaimDrilldown) {
    setDrilldown(next);
    setStatus("ALL");
    setSearch("");
    setProjectId(ALL_PROJECTS);
    setTab("all");
  }

  const exportFilters: ClaimsExportFilters = useMemo(() => {
    // On the payment run, the export IS the run: approved claims the employee
    // paid for themselves. Anything else would hand payroll rows it must not pay.
    if (tab === "pay") return { status: "APPROVED", paymentType: "PERSONAL" };

    return {
      // A "Pending" filter also covers SUBMITTED in the UI; the export takes a
      // single status, so it exports the PENDING ones.
      status: status === "ALL" ? undefined : status,
      projectId: projectId === ALL_PROJECTS ? undefined : projectId,
    };
  }, [tab, status, projectId]);

  const filterSummary = useMemo(() => {
    if (tab === "pay") {
      return `The payment run — ${readyToPay.length} approved out-of-pocket claim${
        readyToPay.length === 1 ? "" : "s"
      }, ${formatCurrency(sumAmount(readyToPay))}`;
    }

    const parts: string[] = [];
    if (status !== "ALL") parts.push(`${status.toLowerCase()} claims`);
    if (projectId !== ALL_PROJECTS) parts.push(projectNames.get(projectId) ?? "one project");

    const base = parts.length > 0 ? `Filtered to ${parts.join(" · ")}` : "Every claim in the org";

    // Be straight about it: the export speaks the API's filters, not the
    // client-side subset a card click produced.
    return drilldown ? `${base} — a drill-through view isn't part of the export` : base;
  }, [tab, readyToPay, status, projectId, projectNames, drilldown]);

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between sm:gap-6">
        <OverflowTabList<ClaimsTab>
          items={[
            { id: "overview", label: "Overview", badge: staleCount },
            { id: "pay", label: "Ready to pay", badge: readyToPay.length },
            { id: "all", label: "All claims" },
          ]}
          value={tab}
          onChange={setTab}
          className="sm:max-w-md"
          ariaLabel="Claims dashboard views"
        />

        <div className="shrink-0 pb-1">
          <ClaimsMonthEndActions
            filters={exportFilters}
            filterSummary={filterSummary}
            onImported={() => void load()}
            onReport={setImportReport}
          />
        </div>
      </div>

      {importReport ? (
        <ClaimsImportReport report={importReport} onDismiss={() => setImportReport(null)} />
      ) : null}

      {tab === "pay" ? (
        loading ? (
          <section className="rounded-[28px] border border-border/70 bg-card/90 p-6 text-sm text-muted-foreground shadow-ambient backdrop-blur-sm">
            Loading claims…
          </section>
        ) : (
          <AdminClaimsReadyToPay
            claims={claims}
            employeeEmails={employeeEmails}
            onDrill={openDrilldown}
          />
        )
      ) : tab === "overview" ? (
        error ? (
          <section className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
            Error: {error}
          </section>
        ) : loading ? (
          <section className="rounded-[28px] border border-border/70 bg-card/90 p-6 text-sm text-muted-foreground shadow-ambient backdrop-blur-sm">
            Loading claims…
          </section>
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
          status={status}
          onStatusChange={setStatus}
          search={search}
          onSearchChange={setSearch}
          projectId={projectId}
          onProjectChange={setProjectId}
          projectNames={projectNames}
          employeeEmails={employeeEmails}
          accountLabels={accountLabels}
        />
      )}
    </div>
  );
}
