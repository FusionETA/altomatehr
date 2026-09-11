import { CircleSlash, TriangleAlert, UserX } from "lucide-react";
import type { PayrollReadiness, Payslip, SkippedEmployee } from "../api";
import { rm } from "../lib/payroll-format";
import { CARD, HINT, TD, TH } from "../lib/ui";

// Everything standing between this run and a filing, in one place.
//
// Three different problems, kept apart because the fix differs: a zero net
// pay is a salary or a deduction to correct, a missing field is data entry,
// and an excluded employee may be correct. Pooling them into one list would
// make the admin work out which is which.
export function PayrollRunAttention({
  readiness,
  payslips,
  skipped,
}: {
  readiness: PayrollReadiness | null;
  payslips: Payslip[];
  skipped: SkippedEmployee[];
}) {
  // Generation produces a payslip either way, so a zero net is only visible
  // here — and it is the one that blocks submission outright.
  const zeroNet = payslips.filter((payslip) => payslip.netPay <= 0);

  return (
    <div className="space-y-4">
      {zeroNet.length > 0 ? (
        <section className="rounded-[28px] border border-destructive/25 bg-destructive/5 p-5 sm:p-6">
          <h3 className="flex items-center gap-2 text-[15px] font-semibold text-destructive">
            <CircleSlash className="size-4" aria-hidden />
            {zeroNet.length} employee(s) would take home nothing
          </h3>
          <p className="mt-1 text-xs text-destructive/80">
            The run cannot be submitted while anyone is on zero or less. Either the
            salary is missing from their profile, or their deductions exceed their pay.
          </p>

          <ul className="mt-3 space-y-1">
            {zeroNet.map((payslip) => (
              <li
                key={payslip.id}
                className="flex items-baseline justify-between gap-3 text-sm"
              >
                <span className="font-medium text-foreground">
                  {payslip.snapshotName}
                </span>
                <span className="tabular-nums text-muted-foreground">
                  gross {rm(payslip.grossPay)} · net {rm(payslip.netPay)}
                </span>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      {readiness && !readiness.ok ? (
        <section className={CARD}>
          <h3 className="flex items-center gap-2 text-[15px] font-semibold text-foreground">
            <TriangleAlert className="size-4 text-amber-600 dark:text-amber-400" aria-hidden />
            {readiness.totalMissingCount} field(s) the statutory files need
          </h3>
          <p className={HINT}>
            Each one is required by a file this run has to produce. Submission is
            refused until they are filled.
          </p>

          {readiness.orgIssues.length > 0 ? (
            <p className="mt-3 text-sm">
              <span className="font-medium text-foreground">Company Info</span>{" "}
              <span className="text-muted-foreground">
                is missing {readiness.orgIssues.join(", ")} — Settings → Form E (LHDN).
              </span>
            </p>
          ) : null}

          {readiness.employeeIssues.length > 0 ? (
            <div className="mt-3 overflow-x-auto">
              <table className="w-full min-w-[420px] border-collapse">
                <thead>
                  <tr className="border-b border-border/70">
                    <th className={TH}>Employee</th>
                    <th className={TH}>Missing</th>
                  </tr>
                </thead>
                <tbody>
                  {readiness.employeeIssues.map((issue) => (
                    <tr
                      key={`${issue.employeeCode}-${issue.name}`}
                      className="border-b border-border/40 last:border-0"
                    >
                      <td className={TD}>
                        <span className="font-medium text-foreground">{issue.name}</span>
                        {issue.employeeCode ? (
                          <span className="ml-2 text-xs text-muted-foreground">
                            {issue.employeeCode}
                          </span>
                        ) : null}
                      </td>
                      <td className={`${TD} text-muted-foreground`}>
                        {issue.missing.join(", ")}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : null}
        </section>
      ) : null}

      {/* Not necessarily a problem — an archived leaver SHOULD be left out.
          Listed so the exclusion is a decision rather than a silent drop. */}
      {skipped.length > 0 ? (
        <section className={CARD}>
          <h3 className="flex items-center gap-2 text-[15px] font-semibold text-foreground">
            <UserX className="size-4 text-muted-foreground" aria-hidden />
            {skipped.length} employee(s) left out of this run
          </h3>
          <p className={HINT}>
            Generation skipped them for the reason given. Often correct — a leaver, or
            someone who joined after the period.
          </p>

          <ul className="mt-3 space-y-1 text-sm">
            {skipped.map((employee) => (
              <li
                key={employee.employeeProfileId}
                className="flex items-baseline justify-between gap-3"
              >
                <span className="font-medium text-foreground">{employee.name}</span>
                <span className="text-muted-foreground">{employee.reason}</span>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
    </div>
  );
}
