import { useEffect, useMemo, useState } from "react";
import { Download, FileText, LoaderCircle, Sparkles, UserPlus, X } from "lucide-react";
import {
  exportEmployeeLeaveSummary,
  exportEmployeeLeaveSummaryPdf,
  getEmployeeLeaveBalances,
  getLeaveSummaryReport,
  resetLeaveEntitlement,
  seedLeaveEntitlements,
  setLeaveEntitlement,
  type EmployeeLeaveBalances,
  type LeaveAccrualMethod,
  type LeaveApplication,
  type LeaveBalance,
  type LeaveSummaryReport,
  type LeaveType,
} from "@/features/leave/api";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { ApplyOnBehalfModal } from "./ApplyOnBehalfModal";

const CARD = "rounded-[22px] border border-border/70 bg-card/70 p-5";
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

export function EmployeeLeaveModal({
  employee,
  types,
  year,
  onClose,
  onBalancesUpdated,
}: {
  employee: EmployeeLeaveBalances;
  types: LeaveType[];
  year: number;
  onClose: () => void;
  onBalancesUpdated: (employeeId: string, balances: LeaveBalance[]) => void;
}) {
  const [balances, setBalances] = useState<LeaveBalance[]>(employee.balances);
  const [report, setReport] = useState<LeaveSummaryReport | null>(null);
  const [loadingReport, setLoadingReport] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busyTypeId, setBusyTypeId] = useState<string | null>(null);
  const [seeding, setSeeding] = useState(false);
  const [applyOnBehalfOpen, setApplyOnBehalfOpen] = useState(false);
  const [exporting, setExporting] = useState(false);
  const employeeLabel = buildName(employee.email);

  useEffect(() => {
    getLeaveSummaryReport(employee.userId, year)
      .then(setReport)
      .catch((e: unknown) => setError(message(e, "Could not load the leave report.")))
      .finally(() => setLoadingReport(false));
  }, [employee.userId, year]);

  function balanceFor(typeId: string) {
    return balances.find((b) => b.leaveTypeId === typeId);
  }

  async function refreshBalances() {
    const result = await getEmployeeLeaveBalances(employee.userId, year);
    setBalances(result.data);
    onBalancesUpdated(employee.userId, result.data);
  }

  async function handleSave(typeId: string, entitledDays: number, accrualMethod: LeaveAccrualMethod | null) {
    setBusyTypeId(typeId);
    setError(null);
    try {
      await setLeaveEntitlement(employee.userId, typeId, { entitledDays, accrualMethod }, year);
      await refreshBalances();
    } catch (e) {
      setError(message(e, "Could not save this entitlement."));
    } finally {
      setBusyTypeId(null);
    }
  }

  async function handleReset(typeId: string) {
    setBusyTypeId(typeId);
    setError(null);
    try {
      await resetLeaveEntitlement(employee.userId, typeId, year);
      await refreshBalances();
    } catch (e) {
      setError(message(e, "Could not reset this entitlement."));
    } finally {
      setBusyTypeId(null);
    }
  }

  async function handleSeed() {
    setSeeding(true);
    setError(null);
    try {
      await seedLeaveEntitlements(employee.userId, year);
      await refreshBalances();
    } catch (e) {
      setError(message(e, "Could not seed entitlements."));
    } finally {
      setSeeding(false);
    }
  }

  async function handleExport(kind: "pdf" | "csv" | "xlsx") {
    setExporting(true);
    try {
      if (kind === "pdf") await exportEmployeeLeaveSummaryPdf(employee.userId, year);
      else await exportEmployeeLeaveSummary(employee.userId, kind, year);
    } catch (e) {
      setError(message(e, "Could not export this report."));
    } finally {
      setExporting(false);
    }
  }

  const activeTypes = useMemo(() => types.filter((t) => !t.isArchived), [types]);
  const anyUnopened = balances.some((b) => !b.isOpened) || balances.length < activeTypes.length;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm">
      <div className="nice-scrollbar max-h-[90vh] w-full max-w-[760px] overflow-y-auto rounded-[28px] border border-white/40 bg-card/95 p-6 shadow-[0_18px_48px_rgba(76,26,134,0.14)] backdrop-blur-xl sm:p-8">
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
              {employee.role} · {year}
            </p>
            <h2 className="mt-1 truncate text-2xl font-black text-foreground">{employeeLabel}</h2>
            <p className="text-sm text-muted-foreground">{employee.email}</p>
          </div>
          <button
            type="button"
            aria-label="Close"
            onClick={onClose}
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        {error ? (
          <p className="mt-4 rounded-2xl border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive">
            {error}
          </p>
        ) : null}

        <div className="mt-5 flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => setApplyOnBehalfOpen(true)}
            className="inline-flex items-center gap-2 rounded-full bg-primary px-4 py-2 text-xs font-bold text-primary-foreground shadow-sm transition hover:opacity-90"
          >
            <UserPlus className="h-3.5 w-3.5" />
            Apply on behalf
          </button>
          <button
            type="button"
            disabled={exporting}
            onClick={() => handleExport("pdf")}
            className="inline-flex items-center gap-2 rounded-full border border-border/60 bg-card px-4 py-2 text-xs font-bold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
          >
            <FileText className="h-3.5 w-3.5" />
            PDF
          </button>
          <button
            type="button"
            disabled={exporting}
            onClick={() => handleExport("csv")}
            className="inline-flex items-center gap-2 rounded-full border border-border/60 bg-card px-4 py-2 text-xs font-bold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
          >
            <Download className="h-3.5 w-3.5" />
            CSV
          </button>
          <button
            type="button"
            disabled={exporting}
            onClick={() => handleExport("xlsx")}
            className="inline-flex items-center gap-2 rounded-full border border-border/60 bg-card px-4 py-2 text-xs font-bold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
          >
            <Download className="h-3.5 w-3.5" />
            XLSX
          </button>
        </div>

        <section className={`mt-4 ${CARD}`}>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
              Entitlements
            </p>
            {anyUnopened ? (
              <button
                type="button"
                disabled={seeding}
                onClick={handleSeed}
                className="inline-flex items-center gap-1.5 rounded-full bg-secondary px-3 py-1.5 text-xs font-bold text-secondary-foreground transition hover:opacity-90 disabled:opacity-50"
              >
                {seeding ? <LoaderCircle className="h-3 w-3 animate-spin" /> : <Sparkles className="h-3 w-3" />}
                Seed defaults for {year}
              </button>
            ) : null}
          </div>

          <div className="mt-3 space-y-3">
            {activeTypes.map((type) => (
              <EntitlementRow
                key={type.id}
                type={type}
                balance={balanceFor(type.id)}
                busy={busyTypeId === type.id}
                onSave={(days, method) => handleSave(type.id, days, method)}
                onReset={() => handleReset(type.id)}
              />
            ))}
          </div>
        </section>

        <section className={`mt-4 ${CARD}`}>
          <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
            Report — {year}
          </p>
          {loadingReport ? (
            <p className="mt-3 text-sm text-muted-foreground">Loading report…</p>
          ) : !report ? (
            <p className="mt-3 text-sm text-muted-foreground">No report available.</p>
          ) : (
            <>
              <div className="mt-3 overflow-x-auto">
                <table className="w-full min-w-[760px] text-xs">
                  <thead>
                    <tr className="border-b border-border/60 text-left text-muted-foreground">
                      <th className="py-2 pr-3 font-bold uppercase tracking-[0.1em]">Type</th>
                      {MONTHS.map((m) => (
                        <th key={m} className="px-2 py-2 text-right font-bold">
                          {m}
                        </th>
                      ))}
                      <th className="px-2 py-2 text-right font-bold">Total</th>
                      <th className="py-2 pl-2 text-right font-bold">Balance</th>
                    </tr>
                  </thead>
                  <tbody>
                    {report.monthlyRows.map((row) => (
                      <tr key={row.leaveTypeName} className="border-b border-border/40">
                        <td className="py-2 pr-3 font-semibold text-foreground">{row.leaveTypeName}</td>
                        {row.monthly.map((value, idx) => (
                          <td key={idx} className="px-2 py-2 text-right tabular-nums text-muted-foreground">
                            {value ?? "—"}
                          </td>
                        ))}
                        <td className="px-2 py-2 text-right font-semibold tabular-nums text-foreground">
                          {row.total}
                        </td>
                        <td className="py-2 pl-2 text-right font-semibold tabular-nums text-foreground">
                          {row.balance}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="mt-4">
                <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
                  Detail
                </p>
                {report.detailRows.length === 0 ? (
                  <p className="mt-2 text-sm text-muted-foreground">No leave taken this year.</p>
                ) : (
                  <ul className="mt-2 space-y-1.5">
                    {report.detailRows.map((row, idx) => (
                      <li
                        key={idx}
                        className="flex flex-wrap items-center justify-between gap-2 rounded-xl bg-surface-low/70 px-3 py-2 text-xs"
                      >
                        <span className="font-semibold text-foreground">{row.leaveTypeName}</span>
                        <span className="text-muted-foreground">
                          {new Date(row.from).toLocaleDateString("en-GB", { day: "2-digit", month: "short" })}
                          {" – "}
                          {new Date(row.to).toLocaleDateString("en-GB", { day: "2-digit", month: "short" })}
                          {" · "}
                          {row.days}d{row.reason ? ` · ${row.reason}` : ""}
                        </span>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </>
          )}
        </section>
      </div>

      {applyOnBehalfOpen ? (
        <ApplyOnBehalfModal
          employeeId={employee.userId}
          employeeLabel={employeeLabel}
          types={activeTypes}
          onClose={() => setApplyOnBehalfOpen(false)}
          onCreated={(_app: LeaveApplication) => {
            refreshBalances();
          }}
        />
      ) : null}
    </div>
  );
}

function EntitlementRow({
  type,
  balance,
  busy,
  onSave,
  onReset,
}: {
  type: LeaveType;
  balance: LeaveBalance | undefined;
  busy: boolean;
  onSave: (entitledDays: number, accrualMethod: LeaveAccrualMethod | null) => void;
  onReset: () => void;
}) {
  const isAnnual = type.code.toUpperCase() === "ANNUAL";
  const [days, setDays] = useState(String(balance?.entitlementDays ?? type.defaultDays));
  const [method, setMethod] = useState<LeaveAccrualMethod>(
    (balance?.accrualMethod as LeaveAccrualMethod) ?? "LUMP_SUM",
  );

  useEffect(() => {
    setDays(String(balance?.entitlementDays ?? type.defaultDays));
    if (balance?.accrualMethod) setMethod(balance.accrualMethod as LeaveAccrualMethod);
  }, [balance?.entitlementDays, balance?.accrualMethod, type.defaultDays]);

  return (
    <div className="flex flex-col gap-3 rounded-2xl bg-surface-low p-4 sm:flex-row sm:items-center sm:justify-between">
      <div className="min-w-0">
        <p className="text-sm font-bold text-foreground">{type.name}</p>
        <p className="text-xs text-muted-foreground">
          {balance ? (
            <>
              {balance.remainingDays} remaining · {balance.takenDays} taken
              {balance.pendingDays > 0 ? ` · ${balance.pendingDays} pending` : ""}
              {!balance.isOpened ? " · projected, not opened yet" : ""}
            </>
          ) : (
            "Not opened for this year"
          )}
        </p>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <input
          type="number"
          min="0"
          step="0.5"
          value={days}
          onChange={(e) => setDays(e.target.value)}
          className="h-10 w-24 rounded-xl border border-border bg-card px-3 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        />
        {isAnnual ? (
          <Select value={method} onValueChange={(v) => setMethod(v as LeaveAccrualMethod)}>
            <SelectTrigger className="h-10 w-[140px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="LUMP_SUM">Lump sum</SelectItem>
              <SelectItem value="PRO_RATED">Pro-rated</SelectItem>
            </SelectContent>
          </Select>
        ) : null}
        <button
          type="button"
          disabled={busy}
          onClick={() => onSave(Number(days) || 0, isAnnual ? method : null)}
          className="inline-flex items-center gap-1.5 rounded-full bg-primary px-3 py-1.5 text-xs font-bold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
        >
          {busy ? <LoaderCircle className="h-3 w-3 animate-spin" /> : null}
          Save
        </button>
        <button
          type="button"
          disabled={busy}
          onClick={onReset}
          className="rounded-full border border-border/60 bg-card px-3 py-1.5 text-xs font-semibold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
        >
          Reset
        </button>
      </div>
    </div>
  );
}
