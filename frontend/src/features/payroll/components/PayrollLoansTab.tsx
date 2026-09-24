import { useCallback, useEffect, useState } from "react";
import { ChevronDown, ChevronRight, LoaderCircle, Plus } from "lucide-react";
import {
  cancelEmployeeLoan,
  deleteEmployeeLoan,
  getEmployeeLoans,
  getPayrollEmployees,
  reactivateEmployeeLoan,
  type EmployeeLoan,
} from "../api";
import {
  loanStatusLabels,
  loanStatusTone,
  rm,
} from "../lib/payroll-format";
import {
  BADGE,
  BUTTON,
  BUTTON_DANGER_SM,
  BUTTON_GHOST_SM,
  CARD,
  ERROR_PANEL,
  FOCUS_RING,
  HINT,
  NOTE_PANEL,
  TD,
  TD_NUM,
  TH,
  TH_NUM,
} from "../lib/ui";
import { LoanForm } from "./LoanForm";
import { TableSkeleton } from "./TableSkeleton";
import { Skeleton } from "@/shared/components/Skeleton";
import { useCachedQuery } from "@/shared/lib/use-cached-query";

// Staff loans and salary advances, and how far through each one is.
//
// Progress is not stored: an installment is paid when its period has an
// APPROVED run. That is the only durable evidence the money was actually
// deducted, and it means reverting a month un-pays its installment with no
// separate bookkeeping to get wrong.
export function PayrollLoansTab() {
  const [expanded, setExpanded] = useState<string | null>(null);
  const [editing, setEditing] = useState<EmployeeLoan | null>(null);
  const [adding, setAdding] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Cached. These were read fresh on every mount with `loading` starting true,
  // so the tab emptied itself to a skeleton on every visit and filled back in a
  // round trip later — the same rows, redrawn, which reads as a blink.
  const loansQuery = useCachedQuery("/payroll/loans", () => getEmployeeLoans());
  const employeesQuery = useCachedQuery("/payroll/employees", () => getPayrollEmployees());

  const loans = loansQuery.data ?? [];
  const employees = employeesQuery.data ?? [];
  const loading = loansQuery.loading || employeesQuery.loading;

  // Writes invalidate /payroll*, so both reads come back on their own; this is
  // what the action handlers await.
  const load = useCallback(
    () => Promise.all([loansQuery.refresh(), employeesQuery.refresh()]),
    [loansQuery.refresh, employeesQuery.refresh],
  );

  useEffect(() => {
    const first = loansQuery.error ?? employeesQuery.error;
    if (first) setError(first);
  }, [loansQuery.error, employeesQuery.error]);

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
            {loading ? (
              <Skeleton className="mt-1.5 h-3 w-56" />
            ) : (
              <p className={HINT}>
                {loans.length} on record · RM {rm(outstanding)} still outstanding
              </p>
            )}
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
          {/* Six columns rather than eight — lent + per month, and period +
              progress, each read as one fact — so the table fits a normal
              screen without scrolling sideways to reach its buttons. On a
              narrow one the actions column stays pinned right, so Edit and
              Cancel never sit off-screen behind a scrollbar. */}
          <table className="w-full min-w-[760px] border-collapse">
            <thead>
              <tr className="border-b border-border/70">
                <th className={TH}>Employee</th>
                <th className={TH_NUM}>Amount</th>
                <th className={TH}>Repayment</th>
                <th className={TH_NUM}>Outstanding</th>
                <th className={TH}>Status</th>
                <th className={`${TH} ${PINNED}`} aria-label="Actions" />
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

// Pinned to the right edge so the row's buttons are always in reach, even
// when a narrow screen scrolls the table sideways. Opaque, or the figures
// scrolling underneath would show through.
const PINNED = "sticky right-0 bg-card";

// "Sep 2026" — the full month names made the period the widest thing in the
// row.
function shortPeriod(year: number, month: number): string {
  return new Date(year, month - 1, 1).toLocaleDateString("en-MY", {
    month: "short",
    year: "numeric",
  });
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

  return (
    <>
      <tr className="border-b border-border/40 last:border-0">
        <td className={TD}>
          <button
            type="button"
            className={`flex items-center gap-1.5 rounded-md font-medium text-foreground transition ${FOCUS_RING}`}
            onClick={onToggle}
            aria-expanded={expanded}
          >
            {expanded ? (
              <ChevronDown className="size-4 text-muted-foreground" aria-hidden />
            ) : (
              <ChevronRight className="size-4 text-muted-foreground" aria-hidden />
            )}
            {loan.employeeName || (
              // Blank only when the person is no longer in this org's
              // directory. A blank cell made the note underneath read as
              // the name.
              <span className="italic text-muted-foreground">Employee no longer on file</span>
            )}
          </button>
          {loan.notes ? (
            <div className="ml-5 text-xs text-muted-foreground">{loan.notes}</div>
          ) : null}
        </td>

        <td className={TD_NUM}>
          <div>{rm(loan.principalAmount)}</div>
          <div className="text-xs text-muted-foreground">
            {rm(loan.installmentAmount)} / month
          </div>
        </td>

        <td className={TD}>
          <div className="flex items-center gap-2">
            <div className="h-1.5 w-20 shrink-0 overflow-hidden rounded-full bg-muted">
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
              {loan.paidInstallments} of {loan.installmentCount}
            </span>
          </div>
          <div className="mt-1 whitespace-nowrap text-xs text-muted-foreground">
            {shortPeriod(loan.startYear, loan.startMonth)} –{" "}
            {shortPeriod(loan.endYear, loan.endMonth)}
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
              <span className="block">{rm(loan.remainingAmount)}</span>
              <span className="block text-xs">not collected</span>
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

        <td className={`${TD} ${PINNED} text-right`}>
          <div className="flex justify-end gap-2">
            {loan.status === "ACTIVE" ? (
              <>
                <button type="button" className={BUTTON_GHOST_SM} onClick={onEdit}>
                  Edit
                </button>
                <button
                  type="button"
                  className={BUTTON_DANGER_SM}
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
                  className={BUTTON_GHOST_SM}
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
                    className={BUTTON_DANGER_SM}
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
          <td colSpan={6} className="px-3 py-4">
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
