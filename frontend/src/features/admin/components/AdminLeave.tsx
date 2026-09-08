import { useCallback, useEffect, useMemo, useState } from "react";
import type { ChangeEvent } from "react";
import {
  ArrowRight,
  CalendarCheck,
  ChevronDown,
  Clock3,
  Download,
  LoaderCircle,
  Sparkles,
  SlidersHorizontal,
  Upload,
  Users,
  X,
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import {
  exportAllLeaveSummary,
  exportLeaveBulkZip,
  getAllLeaveBalances,
  getLeaveImportTemplate,
  getAllLeaveApplications,
  getLeaveOverview,
  getLeaveTypes,
  importLeave,
  runLeaveMonthlyAccrual,
  runLeaveYearRollover,
  type EmployeeLeaveBalances,
  type LeaveApplication,
  type LeaveBalance,
  type LeaveOverview,
  type LeaveType,
  type TabularImportResult,
} from "@/features/leave/api";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { LeaveStatusBadge } from "@/features/leave/components/LeaveStatusBadge";
import { LeaveDetailsModal } from "@/features/leave/components/LeaveDetailsModal";
import { formatDateRange, relativeDaysAgo } from "@/features/leave/lib/leave-formatters";
import {
  leaveMatchesStatus,
  leaveStatusLabels,
  visibleLeaveStatuses,
  type LeaveStatusFilter,
} from "@/features/leave/lib/leave-status";
import { StatusFilterTabs } from "@/shared/components/StatusFilterTabs";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import {
  CLAIMS_PAGE_SIZE,
  PaginationControls,
} from "@/features/claims/components/PaginationControls";
import { SearchInput } from "@/shared/components/SearchInput";
import { OverflowTabList } from "@/shared/components/OverflowTabList";
import { EmployeeLeaveModal } from "./EmployeeLeaveModal";
import { LeaveTypesSettings } from "./LeaveTypesSettings";
import { CardHead, EmptyState } from "./DashboardCard";
import {
  ACTION_MENU_EYEBROW,
  ACTION_MENU_ITEM,
  ActionMenu,
} from "./ActionMenu";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const TILE = "rounded-2xl border border-border/60 bg-surface-low p-4";

const CURRENT_YEAR = new Date().getFullYear();
const YEAR_OPTIONS = [CURRENT_YEAR - 1, CURRENT_YEAR, CURRENT_YEAR + 1];

type AdminLeaveTab = "overview" | "history" | "balances" | "types";

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

export function AdminLeave() {
  const [tab, setTab] = useState<AdminLeaveTab>("overview");
  const [year, setYear] = useState(CURRENT_YEAR);
  const [overview, setOverview] = useState<LeaveOverview | null>(null);
  const [balancesRows, setBalancesRows] = useState<EmployeeLeaveBalances[]>([]);
  const [types, setTypes] = useState<LeaveType[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [searchTerm, setSearchTerm] = useState("");
  const [selectedApplication, setSelectedApplication] = useState<LeaveApplication | null>(null);
  const [selectedEmployee, setSelectedEmployee] = useState<EmployeeLeaveBalances | null>(null);
  const [importOpen, setImportOpen] = useState(false);
  // Which pill is open, if any — only ever one at a time.
  const [menu, setMenu] = useState<"export" | "import" | null>(null);
  const [allApplications, setAllApplications] = useState<LeaveApplication[]>([]);
  const [historyLoading, setHistoryLoading] = useState(true);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [historyStatus, setHistoryStatus] = useState<LeaveStatusFilter>("ALL");

  // Jump to History with the rows behind a clicked number already selected.
  function drillTo(status: LeaveStatusFilter) {
    setHistoryStatus(status);
    setTab("history");
  }
  const [exportBusy, setExportBusy] = useState<"csv" | "xlsx" | "zip" | null>(null);
  const [maintenanceBusy, setMaintenanceBusy] = useState<"rollover" | "accrual" | null>(null);
  const [maintenanceResult, setMaintenanceResult] = useState<string | null>(null);

  useEffect(() => {
    setLoading(true);
    setError(null);
    Promise.all([getLeaveOverview(year), getAllLeaveBalances(year), getLeaveTypes()])
      .then(([ov, bal, ty]) => {
        setOverview(ov);
        setBalancesRows(bal.data);
        setTypes(ty);
      })
      .catch((e: unknown) => setError(message(e, "Could not load leave data.")))
      .finally(() => setLoading(false));
  }, [year]);

  // History is a separate fetch: it's the whole org's applications, and the
  // overview shouldn't wait on it to render.
  const loadHistory = useCallback(() => {
    setHistoryLoading(true);
    setHistoryError(null);
    getAllLeaveApplications()
      .then(setAllApplications)
      // Swallowing this used to render "No leave applications yet" over a
      // request that had failed — the screen said the org had never taken
      // leave. A failure has to look like a failure.
      .catch((e: unknown) => setHistoryError(message(e, "Could not load leave history.")))
      .finally(() => setHistoryLoading(false));
  }, []);

  useEffect(loadHistory, [loadHistory]);

  const activeTypes = useMemo(() => types.filter((t) => !t.isArchived), [types]);
  const typeName = (id: string) => types.find((t) => t.id === id)?.name ?? "Leave";

  const tabs = useMemo(
    () => [
      { id: "overview" as AdminLeaveTab, label: "Overview", badge: overview?.totals.pending },
      { id: "history" as AdminLeaveTab, label: "History" },
      { id: "balances" as AdminLeaveTab, label: "Balances" },
      { id: "types" as AdminLeaveTab, label: "Types" },
    ],
    [overview?.totals.pending],
  );

  const filteredBalances = useMemo(() => {
    const q = searchTerm.trim().toLowerCase();
    if (!q) return balancesRows;
    return balancesRows.filter((r) =>
      [r.email, buildName(r.email), r.role].join(" ").toLowerCase().includes(q),
    );
  }, [balancesRows, searchTerm]);

  function updateEmployeeBalances(employeeId: string, balances: LeaveBalance[]) {
    setBalancesRows((cur) => cur.map((r) => (r.userId === employeeId ? { ...r, balances } : r)));
    setSelectedEmployee((cur) => (cur && cur.userId === employeeId ? { ...cur, balances } : cur));
  }

  async function handleExport(kind: "csv" | "xlsx" | "zip") {
    setExportBusy(kind);
    setError(null);
    try {
      if (kind === "zip") await exportLeaveBulkZip(year);
      else await exportAllLeaveSummary(kind, year);
    } catch (e) {
      setError(message(e, "Could not export."));
    } finally {
      setExportBusy(null);
    }
  }

  async function runMaintenance(kind: "rollover" | "accrual") {
    setMaintenanceBusy(kind);
    setMaintenanceResult(null);
    setError(null);
    try {
      const result =
        kind === "rollover" ? await runLeaveYearRollover(year) : await runLeaveMonthlyAccrual();
      setMaintenanceResult(typeof result === "string" ? result : JSON.stringify(result));
    } catch (e) {
      setError(message(e, "Could not run this job."));
    } finally {
      setMaintenanceBusy(null);
    }
  }

  return (
    <div className="space-y-4 sm:space-y-6">
      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {/* Tabs and the actions share one row, as on the claims screen — the
          actions belong to the section you're looking at, not to a band of
          their own below it. */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between sm:gap-6">
        <OverflowTabList
          items={tabs}
          value={tab}
          onChange={setTab}
          variant="underline"
          className="border-b border-border/50 sm:max-w-md sm:flex-1"
          ariaLabel="Leave sections"
        />

        <div className="flex shrink-0 flex-wrap items-center gap-2 pb-1 sm:justify-end">
        <select
          value={year}
          onChange={(e) => setYear(Number(e.target.value))}
          className="h-10 rounded-xl border border-border/70 bg-card px-3 text-sm font-semibold text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          {YEAR_OPTIONS.map((y) => (
            <option key={y} value={y}>
              {y}
            </option>
          ))}
        </select>
        {/* Same Export / Import pills as the claims screen. Three loose export
            buttons put every format on screen at once when an admin only ever
            wants one of them. */}
        <ActionMenu
          label="Export"
          icon={<Download className="h-3.5 w-3.5" />}
          open={menu === "export"}
          onOpenChange={(next) => setMenu(next ? "export" : null)}
          busy={exportBusy !== null}
          disabled={exportBusy !== null}
        >
          <p className={`px-3 pb-1.5 pt-1 ${ACTION_MENU_EYEBROW}`}>Leave {year} as</p>
          {EXPORT_KINDS.map((kind) => (
            <button
              key={kind}
              type="button"
              disabled={exportBusy !== null}
              onClick={() => {
                setMenu(null);
                void handleExport(kind);
              }}
              className={ACTION_MENU_ITEM}
            >
              {exportBusy === kind ? (
                <LoaderCircle className="h-3.5 w-3.5 shrink-0 animate-spin" />
              ) : (
                <Download className="h-3.5 w-3.5 shrink-0" />
              )}
              {EXPORT_LABELS[kind]}
            </button>
          ))}
        </ActionMenu>

        <ActionMenu
          label="Import"
          icon={<Upload className="h-3.5 w-3.5" />}
          open={menu === "import"}
          onOpenChange={(next) => setMenu(next ? "import" : null)}
        >
          <p className={`px-3 pb-1.5 pt-1 ${ACTION_MENU_EYEBROW}`}>Entitlements</p>
          <button
            type="button"
            onClick={() => {
              setMenu(null);
              setImportOpen(true);
            }}
            className={ACTION_MENU_ITEM}
          >
            <Upload className="h-3.5 w-3.5 shrink-0" />
            Upload a file
          </button>
        </ActionMenu>
        </div>
      </div>

      {loading ? <section className={`${CARD} text-sm text-muted-foreground`}>Loading…</section> : null}

      {!loading && tab === "overview" && overview ? (
        <>
          <div className="grid gap-3 sm:grid-cols-3">
            <StatCard
              icon={Clock3}
              onClick={() => drillTo("PENDING")}
              label="Pending approvals"
              value={overview.totals.pending}
              sublabel="Waiting on a supervisor"
              tone="text-tertiary"
              toneBg="bg-tertiary/10"
            />
            <StatCard
              icon={Users}
              label="On leave today"
              value={overview.onLeaveToday.length}
              sublabel="Across the whole org"
            />
            <StatCard
              icon={CalendarCheck}
              onClick={() => drillTo("APPROVED")}
              label="Approved this year"
              value={overview.totals.approved}
              sublabel={`${overview.totals.rejected} rejected · ${overview.totals.cancelled} cancelled`}
            />
          </div>

          <div className="grid gap-4 lg:grid-cols-2">
            <OnLeaveTodayPanel entries={overview.onLeaveToday} />
            <DaysUsedByTypePanel items={overview.daysUsedByType} />
            <MaintenancePanel
              year={year}
              busy={maintenanceBusy}
              result={maintenanceResult}
              onRun={runMaintenance}
            />
          </div>
        </>
      ) : null}

      {tab === "history" ? (
        <HistoryTab
          applications={allApplications}
          loading={historyLoading}
          typeName={typeName}
          onSelect={setSelectedApplication}
          status={historyStatus}
          onStatusChange={setHistoryStatus}
          error={historyError}
          onRetry={loadHistory}
          types={activeTypes}
        />
      ) : null}

      {!loading && tab === "balances" ? (
        <BalancesTab
          rows={filteredBalances}
          total={balancesRows.length}
          types={activeTypes}
          searchTerm={searchTerm}
          onSearchChange={setSearchTerm}
          onSelect={setSelectedEmployee}
        />
      ) : null}

      {tab === "types" ? <LeaveTypesSettings /> : null}

      {selectedApplication ? (
        <LeaveDetailsModal
          application={selectedApplication}
          typeName={typeName(selectedApplication.leaveTypeId)}
          employeeLabel={
            selectedApplication.employeeEmail ? buildName(selectedApplication.employeeEmail) : undefined
          }
          showAudit
          onClose={() => setSelectedApplication(null)}
        />
      ) : null}

      {selectedEmployee ? (
        <EmployeeLeaveModal
          employee={selectedEmployee}
          types={types}
          year={year}
          onClose={() => setSelectedEmployee(null)}
          onBalancesUpdated={updateEmployeeBalances}
        />
      ) : null}

      {importOpen ? <ImportLeaveModal onClose={() => setImportOpen(false)} /> : null}
    </div>
  );
}

// ─── Overview tab pieces ─────────────────────────────────────────────────────

function StatCard({
  icon: Icon,
  label,
  value,
  sublabel,
  tone = "text-primary",
  toneBg = "bg-primary/10",
  onClick,
}: {
  icon: LucideIcon;
  label: string;
  value: number;
  sublabel?: string;
  tone?: string;
  toneBg?: string;
  // Drills into History filtered to the rows behind the number, so no figure on
  // this dashboard is a dead end. Omitted where there's nothing to drill to.
  onClick?: () => void;
}) {
  // Nothing to drill into at zero, so the tile stops being a button rather
  // than opening a filtered list with nothing in it. Same rule as the claims
  // tiles.
  const drillable = onClick !== undefined && value > 0;
  const Wrapper = drillable ? "button" : "div";
  return (
    <Wrapper
      type={drillable ? "button" : undefined}
      onClick={drillable ? onClick : undefined}
      className={`group ${CARD} ${
        drillable ? "w-full cursor-pointer text-left transition hover:border-primary/40" : ""
      }`}
    >
      <div className="flex items-start justify-between gap-3">
        <p className="text-3xl font-black tabular-nums text-foreground">{value}</p>
        {/* The icon doubles as the hover affordance: it fades out and the arrow
            takes its place, so the two never compete for the same corner. */}
        <div
          className={`relative flex h-[42px] w-[42px] shrink-0 items-center justify-center rounded-2xl ${toneBg} ${tone}`}
        >
          <Icon
            className={`h-[18px] w-[18px] transition-opacity ${
              drillable ? "group-hover:opacity-0" : ""
            }`}
          />
          {drillable ? (
            <ArrowRight className="absolute h-[18px] w-[18px] opacity-0 transition-opacity group-hover:opacity-100" />
          ) : null}
        </div>
      </div>
      <p className="mt-2 text-sm font-bold text-foreground">{label}</p>
      {sublabel ? <p className="mt-0.5 text-xs text-muted-foreground">{sublabel}</p> : null}
    </Wrapper>
  );
}


// Org-wide leave history, built like the claims table: status tabs, a search
// box, a collapsible filter panel, and pagination.
//
// It reads /leave/all rather than the overview's recentApplications, which is
// capped at ten — a history you can filter has to be able to reach the
// eleventh. A status arriving from a clicked stat card lands here preselected.
function HistoryTab({
  applications,
  loading,
  error,
  onRetry,
  typeName,
  types,
  onSelect,
  status,
  onStatusChange,
}: {
  applications: LeaveApplication[];
  loading: boolean;
  error: string | null;
  onRetry: () => void;
  typeName: (id: string) => string;
  types: LeaveType[];
  onSelect: (application: LeaveApplication) => void;
  status: LeaveStatusFilter;
  onStatusChange: (status: LeaveStatusFilter) => void;
}) {
  const [search, setSearch] = useState("");
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [typeId, setTypeId] = useState(ALL_FILTER);
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [page, setPage] = useState(1);

  // What the collapsed panel is hiding. Shown on the toggle, because a filter
  // you can't see is still narrowing the list.
  const advancedCount =
    (typeId !== ALL_FILTER ? 1 : 0) + (from ? 1 : 0) + (to ? 1 : 0);
  const anyFilter = advancedCount > 0 || search.trim().length > 0 || status !== "ALL";

  function clearAll() {
    setSearch("");
    setTypeId(ALL_FILTER);
    setFrom("");
    setTo("");
    onStatusChange("ALL");
  }

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return applications.filter((a) => {
      if (!leaveMatchesStatus(a, status)) return false;
      if (typeId !== ALL_FILTER && a.leaveTypeId !== typeId) return false;
      // Overlap, not containment: a range that straddles the window is leave
      // taken in it, and asking for March shouldn't hide leave that began in
      // February and ran into it.
      if (from && a.endDate.slice(0, 10) < from) return false;
      if (to && a.startDate.slice(0, 10) > to) return false;
      if (!q) return true;
      const name = a.employeeEmail ? buildName(a.employeeEmail) : a.employeeId;
      return `${name} ${a.employeeEmail ?? ""}`.toLowerCase().includes(q);
    });
  }, [applications, status, typeId, from, to, search]);

  // Back to page 1 whenever the set changes under you, or you can be left on
  // page 4 of a list that now has one.
  useEffect(() => {
    setPage(1);
  }, [status, search, typeId, from, to]);

  const totalPages = Math.max(1, Math.ceil(filtered.length / CLAIMS_PAGE_SIZE));
  useEffect(() => {
    if (page > totalPages) setPage(totalPages);
  }, [page, totalPages]);

  const visible = useMemo(
    () => filtered.slice((page - 1) * CLAIMS_PAGE_SIZE, page * CLAIMS_PAGE_SIZE),
    [filtered, page],
  );

  return (
    <div className="space-y-4">
      {/* Same order as the claims table: search and the Filters toggle on top,
          the fields under them, then the status tabs across the full width, then
          what the filters left you with. */}
      <section className={`${CARD} !p-5 sm:!p-6`}>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <SearchInput
            value={search}
            onChange={setSearch}
            placeholder="Search by employee"
            className="sm:max-w-sm sm:flex-1"
            inputClassName="h-12"
            clearLabel="Clear employee search"
          />

          <div className="flex items-center">
            <button
              type="button"
              aria-expanded={filtersOpen}
              aria-controls="leave-filter-panel"
              onClick={() => setFiltersOpen((open) => !open)}
              className={`inline-flex h-12 shrink-0 items-center gap-2 rounded-2xl border px-4 text-sm font-bold shadow-sm transition ${
                filtersOpen || advancedCount > 0
                  ? "border-primary/40 bg-primary/5 text-primary"
                  : "border-border/70 bg-card text-muted-foreground hover:text-foreground"
              }`}
            >
              <SlidersHorizontal className="h-4 w-4" />
              Filters
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
        </div>

        <div
          id="leave-filter-panel"
          hidden={!filtersOpen}
          className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3"
        >
          <FilterField label="Leave type">
            <Select value={typeId} onValueChange={setTypeId}>
              <SelectTrigger className={FILTER_CONTROL}>
                <SelectValue placeholder="All types" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL_FILTER}>All types</SelectItem>
                {types.map((t) => (
                  <SelectItem key={t.id} value={t.id}>
                    {t.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </FilterField>

          <FilterField label="Taken from">
            <input
              type="date"
              value={from}
              onChange={(e) => setFrom(e.target.value)}
              className={FILTER_CONTROL}
            />
          </FilterField>

          <FilterField label="Taken to">
            <input
              type="date"
              value={to}
              onChange={(e) => setTo(e.target.value)}
              className={FILTER_CONTROL}
            />
          </FilterField>
        </div>

        <StatusFilterTabs
          value={status}
          onChange={onStatusChange}
          statuses={visibleLeaveStatuses}
          labels={leaveStatusLabels}
          className="mt-4"
          ariaLabel="Leave status"
        />

        <div className="mt-4 flex flex-col gap-2 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
          <p>
            Showing <span className="font-semibold text-foreground">{filtered.length}</span> of{" "}
            <span className="font-semibold text-foreground">{applications.length}</span>{" "}
            applications ·{" "}
            <span className="font-semibold text-foreground">
              {filtered.reduce((sum, a) => sum + a.totalDays, 0)}d
            </span>
          </p>
          {anyFilter ? (
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

      {error ? (
        <section className={`${CARD} text-center`}>
          <p className="text-sm font-medium text-destructive">{error}</p>
          <button
            type="button"
            onClick={onRetry}
            className="mt-3 inline-flex h-10 items-center rounded-full bg-primary px-4 text-sm font-bold text-primary-foreground transition hover:opacity-90"
          >
            Try again
          </button>
        </section>
      ) : loading ? (
        <section className={`${CARD} text-sm text-muted-foreground`}>Loading history…</section>
      ) : filtered.length === 0 ? (
        <section className={`${CARD} text-center`}>
          <p className="text-sm text-muted-foreground">
            {applications.length === 0
              ? "No leave applications yet."
              : "No application matches these filters."}
          </p>
        </section>
      ) : (
        <section className={`${CARD} !p-0`}>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[720px] caption-bottom text-sm">
              <thead>
                <tr className="border-b border-border/60">
                  {["Employee", "Type", "Dates", "Days", "Submitted", "Status"].map((h) => (
                    <th
                      key={h}
                      className="h-12 px-4 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground first:pl-6"
                    >
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {visible.map((a) => (
                  <tr
                    key={a.id}
                    tabIndex={0}
                    onClick={() => onSelect(a)}
                    className="cursor-pointer border-b border-border/60 transition-colors last:border-0 hover:bg-muted/70 focus-visible:bg-muted/70 focus-visible:outline-none"
                  >
                    <td className="p-4 pl-6 align-middle font-bold text-foreground">
                      {a.employeeEmail ? buildName(a.employeeEmail) : a.employeeId}
                    </td>
                    <td className="p-4 align-middle">{typeName(a.leaveTypeId)}</td>
                    <td className="p-4 align-middle">{formatDateRange(a.startDate, a.endDate)}</td>
                    <td className="p-4 align-middle tabular-nums">{a.totalDays}</td>
                    <td className="p-4 align-middle">{relativeDaysAgo(a.createdAt)}</td>
                    <td className="p-4 align-middle">
                      <LeaveStatusBadge status={a.status} />
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
            itemNoun="applications"
          />
        </section>
      )}
    </div>
  );
}

const ALL_FILTER = "ALL";
const FILTER_CONTROL =
  "h-11 w-full rounded-2xl border border-border bg-card px-3 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary";

function FilterField({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="grid gap-1.5">
      <span className="text-[11px] font-bold uppercase tracking-[0.14em] text-muted-foreground">
        {label}
      </span>
      {children}
    </label>
  );
}

function OnLeaveTodayPanel({ entries }: { entries: LeaveOverview["onLeaveToday"] }) {
  return (
    <section className={CARD}>
      <CardHead title="On leave today" />
      <div className="space-y-2">
        {entries.length === 0 ? (
          <EmptyState text="Nobody is on approved leave today." />
        ) : (
          entries.map((e) => (
            <div key={`${e.employeeId}-${e.leaveTypeId}`} className={TILE}>
              <div className="flex items-center justify-between gap-3">
                <p className="truncate text-sm font-bold text-foreground">{e.email ?? e.employeeId}</p>
                <p className="text-xs text-muted-foreground">
                  {e.leaveTypeName} · {formatDateRange(e.startDate.slice(0, 10), e.endDate.slice(0, 10))}
                </p>
              </div>
            </div>
          ))
        )}
      </div>
    </section>
  );
}

function DaysUsedByTypePanel({ items }: { items: LeaveOverview["daysUsedByType"] }) {
  const sorted = [...items].sort((a, b) => b.daysUsed - a.daysUsed);
  const max = Math.max(1, ...sorted.map((i) => i.daysUsed));

  return (
    <section className={CARD}>
      <CardHead title="Days used by type" meta="This year" />
      <div className="space-y-3">
        {sorted.length === 0 ? (
          <EmptyState text="No leave taken this year yet." />
        ) : (
          sorted.map((item) => (
            <div key={item.leaveTypeId} className={TILE}>
              <div className="flex items-baseline justify-between gap-3">
                <p className="truncate text-sm font-bold text-foreground">{item.name}</p>
                <p className="text-base font-black tabular-nums text-foreground">{item.daysUsed}d</p>
              </div>
              <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-border/60">
                <div
                  className="h-full rounded-full bg-primary"
                  style={{ width: `${Math.round((item.daysUsed / max) * 100)}%` }}
                />
              </div>
            </div>
          ))
        )}
      </div>
    </section>
  );
}

function MaintenancePanel({
  year,
  busy,
  result,
  onRun,
}: {
  year: number;
  busy: "rollover" | "accrual" | null;
  result: string | null;
  onRun: (kind: "rollover" | "accrual") => void;
}) {
  return (
    <section className={CARD}>
      <CardHead title="Maintenance" meta="Background jobs" />
      <p className="mb-3 text-xs text-muted-foreground">
        These normally run on a schedule — use these only to trigger one on demand.
      </p>
      <div className="flex flex-wrap gap-2">
        <button
          type="button"
          disabled={busy !== null}
          onClick={() => onRun("rollover")}
          className="inline-flex items-center gap-2 rounded-full border border-border/60 bg-card px-4 py-2 text-xs font-bold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
        >
          {busy === "rollover" ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : null}
          Run year rollover ({year})
        </button>
        <button
          type="button"
          disabled={busy !== null}
          onClick={() => onRun("accrual")}
          className="inline-flex items-center gap-2 rounded-full border border-border/60 bg-card px-4 py-2 text-xs font-bold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
        >
          {busy === "accrual" ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : null}
          Run monthly accrual
        </button>
      </div>
      {result ? (
        <p className="mt-3 truncate rounded-xl bg-surface-low px-3 py-2 text-xs text-muted-foreground">
          {result}
        </p>
      ) : null}
    </section>
  );
}

// ─── Balances tab ────────────────────────────────────────────────────────────

function BalancesTab({
  rows,
  total,
  types,
  searchTerm,
  onSearchChange,
  onSelect,
}: {
  rows: EmployeeLeaveBalances[];
  total: number;
  types: LeaveType[];
  searchTerm: string;
  onSearchChange: (value: string) => void;
  onSelect: (employee: EmployeeLeaveBalances) => void;
}) {
  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <p className="text-sm text-muted-foreground">
          Showing <span className="font-semibold text-foreground">{rows.length}</span> of{" "}
          <span className="font-semibold text-foreground">{total}</span> employees
        </p>
        <SearchInput
          value={searchTerm}
          onChange={onSearchChange}
          placeholder="Search by employee"
          className="max-w-sm"
        />
      </div>

      {rows.length === 0 ? (
        <section className={`${CARD} text-center`}>
          <p className="text-sm text-muted-foreground">No employees match this search.</p>
        </section>
      ) : (
        <section className={`${CARD} !p-0`}>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[720px] caption-bottom text-sm">
              <thead>
                <tr className="border-b border-border/60">
                  <th className="h-12 px-6 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground">
                    Employee
                  </th>
                  {types.map((t) => (
                    <th
                      key={t.id}
                      className="h-12 px-4 text-right text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground"
                    >
                      {t.code}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr
                    key={row.userId}
                    tabIndex={0}
                    onClick={() => onSelect(row)}
                    className="cursor-pointer border-b border-border/60 transition-colors hover:bg-muted/70 focus-visible:bg-muted/70 focus-visible:outline-none"
                  >
                    <td className="p-4 pl-6 align-middle">
                      <p className="font-bold text-foreground">{buildName(row.email)}</p>
                      <p className="text-xs text-muted-foreground">{row.email}</p>
                    </td>
                    {types.map((t) => {
                      const b = row.balances.find((x) => x.leaveTypeId === t.id);
                      return (
                        <td key={t.id} className="p-4 text-right align-middle tabular-nums">
                          {b ? (
                            <span className={b.remainingDays <= 0 ? "text-destructive" : "text-foreground"}>
                              {b.remainingDays}/{b.entitlementDays}
                            </span>
                          ) : (
                            <span className="text-muted-foreground">—</span>
                          )}
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}
    </div>
  );
}

// ─── Page-level export/import ───────────────────────────────────────────────

// Formats the leave export offers. "PDF ZIP" is one PDF per employee, bundled.
const EXPORT_KINDS = ["csv", "xlsx", "zip"] as const;
const EXPORT_LABELS: Record<(typeof EXPORT_KINDS)[number], string> = {
  csv: "CSV",
  xlsx: "XLSX",
  zip: "PDF ZIP",
};

function ImportLeaveModal({ onClose }: { onClose: () => void }) {
  const [format, setFormat] = useState<"csv" | "xlsx">("xlsx");
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [downloadingTemplate, setDownloadingTemplate] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<TabularImportResult | null>(null);

  async function handleTemplate() {
    setDownloadingTemplate(true);
    try {
      await getLeaveImportTemplate(format);
    } catch (e) {
      setError(message(e, "Could not download the template."));
    } finally {
      setDownloadingTemplate(false);
    }
  }

  function handleFileChange(e: ChangeEvent<HTMLInputElement>) {
    setFile(e.target.files?.[0] ?? null);
    setResult(null);
    setError(null);
  }

  async function handleSubmit() {
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      const res = await importLeave(file);
      setResult(res);
    } catch (e) {
      setError(message(e, "Could not import this file."));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-background/70 p-4 backdrop-blur-sm">
      <section className="w-full max-w-[560px] rounded-[26px] border border-white/40 bg-card p-6 shadow-[0_18px_48px_rgba(76,26,134,0.16)]">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
              Bulk import
            </p>
            <h3 className="mt-1 text-xl font-black text-foreground">Import leave entitlements</h3>
          </div>
          <button
            type="button"
            aria-label="Close import"
            onClick={onClose}
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="mt-5 space-y-4">
          <div className="flex items-center gap-2">
            <select
              value={format}
              onChange={(e) => setFormat(e.target.value as "csv" | "xlsx")}
              className="h-10 rounded-xl border border-border/70 bg-card px-3 text-sm font-semibold text-foreground shadow-sm"
            >
              <option value="xlsx">XLSX</option>
              <option value="csv">CSV</option>
            </select>
            <button
              type="button"
              disabled={downloadingTemplate}
              onClick={handleTemplate}
              className="inline-flex items-center gap-1.5 rounded-xl border border-border/70 bg-card px-4 py-2 text-sm font-semibold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
            >
              {downloadingTemplate ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : <Download className="h-3.5 w-3.5" />}
              Download template
            </button>
          </div>

          <label className="block space-y-2">
            <span className="text-sm font-semibold text-foreground">File to import</span>
            <input
              type="file"
              accept=".csv,.xlsx"
              onChange={handleFileChange}
              className="block w-full text-sm text-muted-foreground file:mr-3 file:rounded-full file:border-0 file:bg-primary file:px-4 file:py-2 file:text-xs file:font-bold file:text-primary-foreground"
            />
          </label>
        </div>

        {error ? <p className="mt-3 text-sm font-semibold text-destructive">{error}</p> : null}

        {result ? (
          <div className="mt-4 rounded-2xl bg-surface-low p-4">
            <p className="text-sm font-semibold text-foreground">
              {result.imported} imported · {result.skipped} skipped · {result.failed} failed
            </p>
            {result.errors.length > 0 ? (
              <ul className="mt-2 max-h-40 space-y-1 overflow-y-auto text-xs text-destructive">
                {result.errors.map((e, idx) => (
                  <li key={idx}>
                    Row {e.row}: {e.message}
                  </li>
                ))}
              </ul>
            ) : null}
          </div>
        ) : null}

        <div className="mt-5 grid grid-cols-2 gap-3">
          <button
            type="button"
            onClick={onClose}
            className="h-12 rounded-[18px] border border-border/70 bg-card text-sm font-bold text-muted-foreground transition hover:text-foreground"
          >
            Close
          </button>
          <button
            type="button"
            disabled={busy || !file}
            onClick={handleSubmit}
            className="inline-flex h-12 items-center justify-center gap-2 rounded-[18px] bg-primary text-sm font-bold text-primary-foreground shadow-sm transition hover:opacity-90 disabled:opacity-50"
          >
            {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
            Import
          </button>
        </div>
      </section>
    </div>
  );
}
