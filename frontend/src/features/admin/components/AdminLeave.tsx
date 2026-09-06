import { useEffect, useMemo, useState } from "react";
import type { ChangeEvent } from "react";
import {
  CalendarCheck,
  ClipboardList,
  Clock3,
  Download,
  LoaderCircle,
  RefreshCw,
  Sparkles,
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
import { SearchInput } from "@/shared/components/SearchInput";
import { OverflowTabList } from "@/shared/components/OverflowTabList";
import { EmployeeLeaveModal } from "./EmployeeLeaveModal";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const TILE = "rounded-2xl border border-border/60 bg-surface-low p-4";
const EYEBROW = "text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground";

const CURRENT_YEAR = new Date().getFullYear();
const YEAR_OPTIONS = [CURRENT_YEAR - 1, CURRENT_YEAR, CURRENT_YEAR + 1];

type AdminLeaveTab = "overview" | "history" | "balances";

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

  const activeTypes = useMemo(() => types.filter((t) => !t.isArchived), [types]);
  const typeName = (id: string) => types.find((t) => t.id === id)?.name ?? "Leave";

  const tabs = useMemo(
    () => [
      { id: "overview" as AdminLeaveTab, label: "Overview", badge: overview?.totals.pending },
      { id: "history" as AdminLeaveTab, label: "History" },
      { id: "balances" as AdminLeaveTab, label: "Balances" },
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

      <OverflowTabList
        items={tabs}
        value={tab}
        onChange={setTab}
        variant="underline"
        className="border-b border-border/50"
        ariaLabel="Leave sections"
      />

      <div className="flex flex-wrap items-center gap-2 sm:justify-end">
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
        <ExportButtons busy={exportBusy} onExport={handleExport} />
        <button
          type="button"
          onClick={() => setImportOpen(true)}
          className="inline-flex items-center gap-2 rounded-xl border border-border/70 bg-card px-4 py-2 text-sm font-semibold text-muted-foreground shadow-sm transition hover:text-foreground"
        >
          <Upload className="h-4 w-4" />
          Import
        </button>
      </div>

      {loading ? <section className={`${CARD} text-sm text-muted-foreground`}>Loading…</section> : null}

      {!loading && tab === "overview" && overview ? (
        <>
          <div className="grid gap-3 sm:grid-cols-3">
            <StatCard
              icon={Clock3}
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

      {!loading && tab === "history" && overview ? (
        <HistoryTab
          applications={overview.recentApplications}
          typeName={typeName}
          onSelect={setSelectedApplication}
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
}: {
  icon: LucideIcon;
  label: string;
  value: number;
  sublabel?: string;
  tone?: string;
  toneBg?: string;
}) {
  return (
    <div className={CARD}>
      <div className="flex items-start justify-between gap-3">
        <p className="text-3xl font-black tabular-nums text-foreground">{value}</p>
        <div className={`rounded-2xl ${toneBg} p-2.5 ${tone}`}>
          <Icon className="h-[18px] w-[18px]" />
        </div>
      </div>
      <p className="mt-2 text-sm font-bold text-foreground">{label}</p>
      {sublabel ? <p className="mt-0.5 text-xs text-muted-foreground">{sublabel}</p> : null}
    </div>
  );
}

function CardHead({ icon: Icon, title, meta }: { icon: LucideIcon; title: string; meta?: string }) {
  return (
    <div className="flex flex-row items-center justify-between gap-3 pb-3">
      <div className="flex items-center gap-3">
        <div className="rounded-2xl bg-primary/10 p-2.5 text-primary">
          <Icon className="h-[18px] w-[18px]" />
        </div>
        <h3 className="text-base font-black text-foreground">{title}</h3>
      </div>
      {meta ? <span className={EYEBROW}>{meta}</span> : null}
    </div>
  );
}

function EmptyState({ text }: { text: string }) {
  return (
    <p className="rounded-2xl bg-surface-low px-4 py-6 text-center text-sm text-muted-foreground">
      {text}
    </p>
  );
}

// ─── History tab ─────────────────────────────────────────────────────────────

function HistoryTab({
  applications,
  typeName,
  onSelect,
}: {
  applications: LeaveApplication[];
  typeName: (id: string) => string;
  onSelect: (application: LeaveApplication) => void;
}) {
  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2 text-sm text-muted-foreground">
        <ClipboardList className="h-4 w-4" />
        Showing the <span className="font-semibold text-foreground">10</span> most recent
        applications org-wide.
      </div>

      {applications.length === 0 ? (
        <section className={`${CARD} text-center`}>
          <p className="text-sm text-muted-foreground">No leave applications yet.</p>
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
                {applications.map((a) => (
                  <tr
                    key={a.id}
                    tabIndex={0}
                    onClick={() => onSelect(a)}
                    className="cursor-pointer border-b border-border/60 transition-colors hover:bg-muted/70 focus-visible:bg-muted/70 focus-visible:outline-none"
                  >
                    <td className="p-4 pl-6 align-middle font-bold text-foreground">
                      {a.employeeEmail ? buildName(a.employeeEmail) : a.employeeId}
                    </td>
                    <td className="p-4 align-middle">{typeName(a.leaveTypeId)}</td>
                    <td className="p-4 align-middle">{formatDateRange(a.startDate, a.endDate)}</td>
                    <td className="p-4 align-middle">{a.totalDays}</td>
                    <td className="p-4 align-middle">{relativeDaysAgo(a.createdAt)}</td>
                    <td className="p-4 align-middle">
                      <LeaveStatusBadge status={a.status} />
                    </td>
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

function OnLeaveTodayPanel({ entries }: { entries: LeaveOverview["onLeaveToday"] }) {
  return (
    <section className={CARD}>
      <CardHead icon={Users} title="On leave today" />
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
      <CardHead icon={CalendarCheck} title="Days used by type" meta="This year" />
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
      <CardHead icon={RefreshCw} title="Maintenance" meta="Background jobs" />
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

function ExportButtons({
  busy,
  onExport,
}: {
  busy: "csv" | "xlsx" | "zip" | null;
  onExport: (kind: "csv" | "xlsx" | "zip") => void;
}) {
  const BTN =
    "inline-flex items-center gap-1.5 rounded-xl border border-border/70 bg-card px-3 py-2 text-xs font-bold text-muted-foreground shadow-sm transition hover:text-foreground disabled:opacity-50";
  return (
    <div className="flex items-center gap-1.5">
      {(["csv", "xlsx", "zip"] as const).map((kind) => (
        <button key={kind} type="button" disabled={busy !== null} onClick={() => onExport(kind)} className={BTN}>
          {busy === kind ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : <Download className="h-3.5 w-3.5" />}
          {kind === "zip" ? "PDF ZIP" : kind.toUpperCase()}
        </button>
      ))}
    </div>
  );
}

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
