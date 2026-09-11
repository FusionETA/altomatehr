import { CalendarClock } from "lucide-react";
import type { SalaryChangeHint } from "../api";
import { rm, shortDate } from "../lib/payroll-format";
import { HINT, WARN_PANEL } from "../lib/ui";

// Mid-cycle salary changes the run's payslips do not account for.
//
// Deliberately advisory. The engine pays ONE salary for the whole month, and
// whether a raise was meant to be backdated is the admin's call — every
// Malaysian payroll product leaves this decision with them. What is removed
// here is the arithmetic and the chance of forgetting, not the judgement.
export function SalaryChangeHints({ hints }: { hints: SalaryChangeHint[] }) {
  // MATCHED never reaches the client — a change effective on the 1st needs
  // no correction, so there is nothing to say.
  const outstanding = hints.filter((hint) => !hint.alreadyApplied);
  if (hints.length === 0) return null;

  return (
    <div className={WARN_PANEL}>
      <p className="flex items-center gap-2 font-semibold">
        <CalendarClock className="size-4" aria-hidden />
        {outstanding.length > 0
          ? `${outstanding.length} salary change(s) took effect part-way through this month`
          : "Mid-cycle salary changes — all corrections applied"}
      </p>

      <p className="mt-1">
        A payslip pays one salary for the whole month, so a change part-way through leaves it
        out by the prorated difference. Add the suggested line to that employee's adjustment to
        correct it.
      </p>

      <ul className="mt-3 space-y-3">
        {hints.map((hint) => (
          <li
            key={`${hint.payslipId}-${hint.salaryChangeId}`}
            className="rounded-xl border border-amber-500/20 bg-card/60 p-3"
          >
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <span className="text-sm font-semibold text-foreground">
                {hint.employeeName}
              </span>
              <span className="text-xs text-muted-foreground">
                {hint.reasonLabel} · effective {shortDate(hint.effectiveDate)}
              </span>
            </div>

            <p className="mt-1 text-sm text-foreground">
              RM {rm(hint.previousMonthlySalary)} → RM {rm(hint.newMonthlySalary)}, with{" "}
              {hint.daysAtOldRate} day(s) at the old rate and {hint.daysAtNewRate} at the new,
              over {hint.totalDaysInPeriod} days.
            </p>

            {hint.alreadyApplied ? (
              <p className={HINT}>
                Applied — a correction of RM {rm(hint.delta)} is already on this run.
              </p>
            ) : hint.outcome === "OVERPAID" ? (
              <p className="mt-1 text-sm">
                This payslip used the <strong>new</strong> salary for the whole month, so{" "}
                <strong>RM {rm(hint.delta)}</strong> was overpaid. Add it as a salary-adjustment
                deduction.
              </p>
            ) : hint.outcome === "UNDERPAID" ? (
              <p className="mt-1 text-sm">
                This payslip used the <strong>old</strong> salary for the whole month, so{" "}
                <strong>RM {rm(hint.delta)}</strong> is owed. Add it as arrears.
              </p>
            ) : (
              // Neither side matches: a second change, or a hand-edit between
              // generating and now. Suggesting a figure would be a guess.
              <p className="mt-1 text-sm">
                This payslip was generated against RM{" "}
                {rm(hint.payslipSnapshotMonthlySalary)}, which matches neither side of the
                change. Work out the correction by hand — there may have been more than one
                edit.
              </p>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}
