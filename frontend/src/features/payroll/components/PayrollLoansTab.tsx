import { useCallback, useEffect, useState } from "react";
import { ChevronDown, ChevronRight, LoaderCircle, Plus } from "lucide-react";
import {
  cancelEmployeeLoan,
  deleteEmployeeLoan,
  getEmployeeLoans,
  getPayrollEmployees,
  reactivateEmployeeLoan,
  type EmployeeLoan,
  type PayrollEmployee,
} from "../api";
import {
  loanStatusLabels,
  loanStatusTone,
  periodLabel,
  rm,
} from "../lib/payroll-format";
import {
  BADGE,
  BUTTON,
  BUTTON_DANGER,
  BUTTON_GHOST,
  CARD,
  ERROR_PANEL,
  HINT,
  NOTE_PANEL,
  TD,
  TD_NUM,
  TH,
  TH_NUM,
} from "../lib/ui";
import { LoanForm } from "./LoanForm";
import { TableSkeleton } from "./TableSkeleton";

// Staff loans and salary advances, and how far through each one is.
//
// Progress is not stored: an installment is paid when its period has an
// APPROVED run. That is the only durable evidence the money was actually
// deducted, and it means reverting a month un-pays its installment with no
// separate bookkeeping to get wrong.
export function PayrollLoansTab() {
  const [loans, setLoans] = useState<EmployeeLoan[]>([]);
  const [employees, setEmployees] = useState<PayrollEmployee[]>([]);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [editing, setEditing] = useState<EmployeeLoan | null>(null);
  const [adding, setAdding] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);

    return Promise.all([getEmployeeLoans(), getPayrollEmployees()])
      .then(([nextLoans, nextEmployees]) => {
        setLoans(nextLoans);
        setEmployees(nextEmployees);
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  async function act(key: string, action: () => Promise<unknown>) {
    setBusy(key);
    setError(null);
    try {
      await action();
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "That action did not go through.");
    } finally {
      setBusy(null);
    }
  }

  if (adding || editing) {
    return (
      <LoanForm
        employees={employees}
        existing={editing}
        onSaved={() => {
          setAdding(false);
          setEditing(null);
          void load();
        }}
        onCancel={() => {
          setAdding(false);
          setEditing(null);
        }}
      />
    );
  }

  const outstanding = loans
    .filter((loan) => loan.status === "ACTIVE")
    .reduce((total, loan) => total + loan.remainingAmount, 0);

  return (
    <div className="space-y-6">
      {error ? <section className={ERROR_PANEL}>Error: {error}</section> : null}

      <section className={CARD}>
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div>
            <h2 className="text-base font-semibold text-foreground">Loans and advances</h2>
            <p className={HINT}>
              {loading
                ? "Loading…"
                : `${loans.length} on record · RM ${rm(outstanding)} still outstanding`}
            </p>
          </div>

          <button type="button" className={BUTTON} onClick={() => setAdding(true)}>
            <Plus className="size-4" aria-hidden />
            Record a loan
          </button>
        </div>
      </section>

      {loading ? (
        <section className={`${CARD}`}>
          <TableSkeleton columns={7} label="Loading loans" />
        </section>
      ) : loans.length === 0 ? (
        <section className={NOTE_PANEL}>
          No loans on record. Recording one deducts it from the employee's pay automatically,
          from the month you choose onwards.
        </section>
      ) : loans.length > 0 ? (
        <section className={`${CARD} overflow-x-auto p-0 sm:p-0`}>
          <table className="w-full min-w-[940px] border-collapse">
            <thead>
              <tr className="border-b border-border/70">
                <th className={TH}>Employee</th>
                <th className={TH_NUM}>Lent</th>
                <th className={TH_NUM}>Per month</th>
                <th className={TH}>Period</th>
                <th className={TH}>Progress</th>
                <th className={TH_NUM}>Outstanding</th>
                <th className={TH}>Status</th>
                <th className={TH} aria-label="Actions" />
              </tr>
            </thead>
            <tbody>
              {loans.map((loan) => (
                <Row
                  key={loan.id}
                  loan={loan}
                  expanded={expanded === loan.id}
                  busy={busy}
                  onToggle={() => setExpanded(expanded === loan.id ? null : loan.id)}
                  onEdit={() => setEditing(loan)}
                  onCancel={() => void act(`cancel-${loan.id}`, () => cancelEmployeeLoan(loan.id))}
                  onReactivate={() =>
                    void act(`reactivate-${loan.id}`, () => reactivateEmployeeLoan(loan.id))
                  }
                  onDelete={() => void act(`delete-${loan.id}`, () => deleteEmployeeLoan(loan.id))}
                />
              ))}
            </tbody>
          </table>
        </section>
      ) : null}
    </div>
  );
}

function Row({
  loan,
  expanded,
  busy,
  onToggle,
  onEdit,
  onCancel,
  onReactivate,
  onDelete,
}: {
  loan: EmployeeLoan;
  expanded: boolean;
  busy: string | null;
  onToggle: () => void;
  onEdit: () => void;
  onCancel: () => void;
  onReactivate: () => void;
  onDelete: () => void;
}) {
  const spinner = (key: string) =>
    busy === key ? <LoaderCircle className="size-3.5 animate-spin" aria-hidden /> : null;

  const small = "rounded-xl px-3 text-xs h-8";

  return (
    <>
      <tr className="border-b border-border/40 last:border-0">
        <td className={TD}>
          <button
            type="button"
            className="flex items-center gap-1.5 font-medium text-foreground"
            onClick={onToggle}
            aria-expanded={expanded}
          >
            {expanded ? (
              <ChevronDown className="size-4 text-muted-foreground" aria-hidden />
            ) : (
              <ChevronRight className="size-4 text-muted-foreground" aria-hidden />
            )}
            {loan.employeeName}
          </button>
          {loan.notes ? (
            <div className="ml-5 text-xs text-muted-foreground">{loan.notes}</div>
          ) : null}
        </td>

        <td className={TD_NUM}>{rm(loan.principalAmount)}</td>
        <td className={TD_NUM}>{rm(loan.installmentAmount)}</td>
        <td className={`${TD} text-muted-foreground`}>
          {periodLabel(loan.startYear, loan.startMonth)} –{" "}
          {periodLabel(loan.endYear, loan.endMonth)}
        </td>

        <td className={TD}>
          <div className="flex items-center gap-2">
            <div className="h-1.5 w-24 overflow-hidden rounded-full bg-muted">
              <div
                className="h-full rounded-full bg-primary"
                style={{
                  width: `${Math.round(
                    (loan.paidInstallments / Math.max(1, loan.installmentCount)) * 100,
                  )}%`,
                }}
              />
            </div>
            <span className="text-xs tabular-nums text-muted-foreground">
              {loan.paidInstallments}/{loan.installmentCount}
            </span>
          </div>
        </td>

        {/* A cancelled loan still has an arithmetic remainder, but nothing
            more will ever be deducted for it — shown muted so the column
            does not read as active debt next to a header that (correctly)
            counts it as nothing outstanding. */}
        <td className={TD_NUM}>
          {loan.status === "CANCELLED" ? (
            <span
              className="text-muted-foreground"
              title="Cancelled — no further deductions will be taken"
            >
              {rm(loan.remainingAmount)} not collected
            </span>
          ) : (
            <span className="font-semibold">{rm(loan.remainingAmount)}</span>
          )}
        </td>

        <td className={TD}>
          <span className={`${BADGE} ${loanStatusTone[loan.status]}`}>
            {loanStatusLabels[loan.status]}
          </span>
        </td>

        <td className={`${TD} text-right`}>
          <div className="flex justify-end gap-2">
            {loan.status === "ACTIVE" ? (
              <>
                <button type="button" className={`${BUTTON_GHOST} ${small}`} onClick={onEdit}>
                  Edit
                </button>
                <button
                  type="button"
                  className={`${BUTTON_DANGER} ${small}`}
                  disabled={busy !== null}
                  onClick={onCancel}
                >
                  {spinner(`cancel-${loan.id}`)}
                  Cancel
                </button>
              </>
            ) : null}

            {loan.status === "CANCELLED" ? (
              <>
                <button
                  type="button"
                  className={`${BUTTON_GHOST} ${small}`}
                  disabled={busy !== null}
                  onClick={onReactivate}
                >
                  {spinner(`reactivate-${loan.id}`)}
                  Reactivate
                </button>
                {/* Only a loan that never deducted anything can be deleted —
                    otherwise the payslips that took money would have nothing
                    explaining them. The server enforces it; this just avoids
                    offering a button that would be refused. */}
                {!loan.hasStarted ? (
                  <button
                    type="button"
                    className={`${BUTTON_DANGER} ${small}`}
                    disabled={busy !== null}
                    onClick={onDelete}
                  >
                    {spinner(`delete-${loan.id}`)}
                    Delete
                  </button>
                ) : null}
              </>
            ) : null}
          </div>
        </td>
      </tr>

      {expanded ? (
        <tr className="border-b border-border/40 bg-muted/30 last:border-0">
          <td colSpan={8} className="px-3 py-4">
            <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              Repayment schedule
            </p>
            <ul className="grid gap-1.5 sm:grid-cols-3 lg:grid-cols-4">
              {loan.schedule.map((installment) => (
                <li
                  key={installment.index}
                  className="flex items-center justify-between gap-3 rounded-xl border border-border/60 bg-card px-3 py-1.5 text-sm"
                >
                  <span className={installment.paid ? "text-muted-foreground" : "text-foreground"}>
                    {installment.periodLabel}
                  </span>
                  <span className="flex items-center gap-2">
                    <span className="tabular-nums">{rm(installment.amount)}</span>
                    {installment.paid ? (
                      <span className="text-xs font-semibold text-emerald-600 dark:text-emerald-400">
                        paid
                      </span>
                    ) : null}
                  </span>
                </li>
              ))}
            </ul>
          </td>
        </tr>
      ) : null}
    </>
  );
}
