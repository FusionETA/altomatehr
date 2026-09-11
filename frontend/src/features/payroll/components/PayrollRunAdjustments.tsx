import { useCallback, useEffect, useState } from "react";
import { SlidersHorizontal } from "lucide-react";
import {
  getPayrollEmployees,
  getRunAdjustments,
  type AdjustmentCategory,
  type PayrollEmployee,
  type PayrollRunAdjustment,
} from "../api";
import { rm } from "../lib/payroll-format";
import { BADGE, BUTTON_GHOST, CARD, ERROR_PANEL, HINT, TD, TH } from "../lib/ui";
import { AdjustmentEditor } from "./AdjustmentEditor";
import { TableSkeleton } from "./TableSkeleton";

// Who has something set for this month, and a way in for everyone else.
//
// Listed from the ROSTER rather than from the payslips: adjustments are
// inputs, so they have to be editable before a run has ever been generated —
// which is exactly when an admin wants to enter the month's overtime.
export function PayrollRunAdjustments({
  runId,
  editable,
  categories,
  openFor,
  onOpenHandled,
  onChanged,
}: {
  runId: string;
  editable: boolean;
  categories: AdjustmentCategory[];
  // A payslip row's shortcut opens the editor here rather than mounting a
  // second copy of it beside the table.
  openFor: string | null;
  onOpenHandled: () => void;
  onChanged: () => void;
}) {
  const [employees, setEmployees] = useState<PayrollEmployee[]>([]);
  const [adjustments, setAdjustments] = useState<PayrollRunAdjustment[]>([]);
  const [open, setOpen] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);

    return Promise.all([getPayrollEmployees(), getRunAdjustments(runId)])
      .then(([nextEmployees, nextAdjustments]) => {
        setEmployees(nextEmployees);
        setAdjustments(nextAdjustments);
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, [runId]);

  useEffect(() => {
    void load();
  }, [load]);

  // A shortcut pressed on a payslip row.
  useEffect(() => {
    if (openFor === null) return;
    setOpen(openFor);
    onOpenHandled();
  }, [openFor, onOpenHandled]);

  const byEmployee = new Map(adjustments.map((row) => [row.employeeProfileId, row]));
  const withSomething = employees.filter((employee) =>
    byEmployee.has(employee.employeeProfileId),
  );

  return (
    <section className={CARD}>
      <header className="mb-4">
        <h2 className="text-base font-semibold text-foreground">One-off adjustments</h2>
        <p className={HINT}>
          Overtime hours, a bonus, a deduction, or a change to someone's recurring
          allowances — for this month only. These survive a regeneration; the payslip
          lines they produce do not.
        </p>
      </header>

      {error ? <p className={ERROR_PANEL}>Error: {error}</p> : null}

      {loading ? (
        <TableSkeleton columns={3} label="Loading adjustments" />
      ) : employees.length === 0 ? (
        <p className={HINT}>No employees on the payroll yet.</p>
      ) : (
        <>
          {withSomething.length === 0 ? (
            <p className={`${HINT} mb-3`}>
              Nothing set for anyone this month. Everyone is paid straight off their
              profile.
            </p>
          ) : null}

          <div className="overflow-x-auto">
            <table className="w-full min-w-[620px] border-collapse">
              <thead>
                <tr className="border-b border-border/70">
                  <th className={TH}>Employee</th>
                  <th className={TH}>Set for this month</th>
                  <th className={TH} aria-label="Edit" />
                </tr>
              </thead>
              <tbody>
                {employees.map((employee) => {
                  const adjustment = byEmployee.get(employee.employeeProfileId);

                  return (
                    <tr
                      key={employee.employeeProfileId}
                      className="border-b border-border/40 last:border-0"
                    >
                      <td className={TD}>
                        <span className="font-medium text-foreground">{employee.name}</span>
                        {employee.employeeNumber ? (
                          <span className="ml-2 text-xs text-muted-foreground">
                            {employee.employeeNumber}
                          </span>
                        ) : null}
                      </td>

                      <td className={TD}>
                        {adjustment ? (
                          <div className="flex flex-wrap gap-1.5">
                            {summarise(adjustment).map((chip) => (
                              <span
                                key={chip}
                                className={`${BADGE} border-sky-500/30 bg-sky-500/10 text-sky-700 dark:text-sky-400`}
                              >
                                {chip}
                              </span>
                            ))}
                          </div>
                        ) : (
                          <span className="text-sm text-muted-foreground">—</span>
                        )}
                      </td>

                      <td className={`${TD} text-right`}>
                        <button
                          type="button"
                          className={`${BUTTON_GHOST} h-8 rounded-xl px-3 text-xs`}
                          onClick={() => setOpen(employee.employeeProfileId)}
                        >
                          <SlidersHorizontal className="size-3.5" aria-hidden />
                          {/* A submitted run still opens, read-only — an
                              admin asking "why was this month different?"
                              needs to see the inputs, not be locked out. */}
                          {editable ? (adjustment ? "Edit" : "Adjust") : "View"}
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </>
      )}

      {open ? (
        <AdjustmentEditor
          runId={runId}
          employeeProfileId={open}
          categories={categories}
          onClose={() => setOpen(null)}
          onSaved={() => {
            setOpen(null);
            void load();
            // The run's payslips are now behind their inputs, so the page
            // above has to re-read and show the staleness banner.
            onChanged();
          }}
        />
      ) : null}
    </section>
  );
}

// What is set, in the fewest words that still distinguish two rows.
function summarise(adjustment: PayrollRunAdjustment): string[] {
  const chips: string[] = [];

  const ot =
    adjustment.otNormalHours + adjustment.otRestHours + adjustment.otPublicHours;
  if (ot > 0) chips.push(`${ot}h overtime`);

  if (adjustment.manualLineItems.length > 0) {
    const total = adjustment.manualLineItems.reduce((sum, item) => sum + item.amount, 0);
    chips.push(
      `${adjustment.manualLineItems.length} one-off line(s) · RM ${rm(total)}`,
    );
  }

  const overrides = Object.keys(adjustment.fixedAllowanceOverrides).length;
  if (overrides > 0) chips.push(`${overrides} allowance override(s)`);

  if (adjustment.workedHours !== null) chips.push("hours overridden");
  if (adjustment.notes) chips.push("note");

  // A saved row with nothing in it is possible — the admin typed something,
  // then took it back out without clearing. Say so rather than showing an
  // empty cell that reads as "nothing set".
  return chips.length > 0 ? chips : ["saved, but empty"];
}
