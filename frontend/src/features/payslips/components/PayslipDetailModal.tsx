import { createPortal } from "react-dom";
import { X } from "lucide-react";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";
import { SkeletonPanel } from "@/shared/components/Skeleton";
import { rmWithUnit } from "@/features/payroll/lib/payroll-format";
import type { PayslipOvertimeRow } from "@/features/payroll/api";
import { getMyPayslip, type PayslipSummary } from "../api";

// The full breakdown behind one payslip.
//
// The summary already carries net, gross and the four statutory figures, so
// they render from it immediately and the detail read only fills in what the
// list can't hold: the earnings that add up to gross, and the individual
// allowance / deduction lines.
//
// Portalled, like every other overlay here — a fixed overlay rendered inside
// the shell's animated tab wrapper is positioned against that wrapper rather
// than the viewport for as long as the animation runs.
export function PayslipDetailModal({
  summary,
  onClose,
}: {
  summary: PayslipSummary;
  onClose: () => void;
}) {
  useBodyScrollLock();

  const query = useCachedQuery(`/payslips/${summary.id}`, () => getMyPayslip(summary.id));
  const payslip = query.data;

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm">
      <section className="max-h-[calc(100vh-2rem)] w-full max-w-[560px] overflow-y-auto rounded-[32px] border border-white/40 bg-card/95 p-6 shadow-panel backdrop-blur-xl sm:p-8">
        <div className="flex items-start justify-between gap-4 border-b border-border/60 pb-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
              Payslip
            </p>
            <h2 className="text-xl font-black text-foreground">{summary.periodLabel}</h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="grid h-10 w-10 shrink-0 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground transition hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="mt-5 rounded-[22px] border border-border/70 bg-surface-low/50 p-5 text-center">
          <p className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
            Net pay
          </p>
          <p className="mt-1 text-3xl font-black tabular-nums text-foreground">
            {rmWithUnit(summary.netPay)}
          </p>
        </div>

        {query.loading ? (
          <div className="mt-5">
            <SkeletonPanel />
          </div>
        ) : query.error ? (
          <p className="mt-5 text-sm font-medium text-destructive">{query.error}</p>
        ) : payslip ? (
          <>
            <Group title="Earnings">
              <Row label="Basic pay" value={payslip.basicPay} />
              {payslip.proratedPay !== payslip.basicPay ? (
                <Row label="Prorated pay" value={payslip.proratedPay} />
              ) : null}
              {payslip.otPay > 0 ? <Row label="Overtime" value={payslip.otPay} /> : null}
              {/* Each allowance / claim by its own name; the plain totals only
                  when the lines don't add up (an imported payslip). */}
              {payslip.earningLines ? (
                payslip.earningLines.map((line, i) => (
                  <Row
                    key={`${line.label}-${i}`}
                    label={line.label}
                    value={Math.abs(line.amount)}
                    negative={line.amount < 0}
                  />
                ))
              ) : (
                <>
                  {payslip.totalAllowances > 0 ? (
                    <Row label="Allowances" value={payslip.totalAllowances} />
                  ) : null}
                  {payslip.totalReimbursements > 0 ? (
                    <Row label="Reimbursements" value={payslip.totalReimbursements} />
                  ) : null}
                </>
              )}
              <Row label="Gross pay" value={payslip.grossPay} strong />
            </Group>

            {payslip.overtimeRows.length > 0 ? <OvertimeTable rows={payslip.overtimeRows} /> : null}

            <Group title="Deductions">
              <Row label="EPF" value={payslip.epfEmployee} />
              <Row label="SOCSO" value={payslip.socsoEmployee} />
              <Row label="EIS" value={payslip.eisEmployee} />
              {payslip.skbbkEmployee > 0 ? (
                <Row label="SKBBK" value={payslip.skbbkEmployee} />
              ) : null}
              {/* Includes Additional PCB — withheld and filed as PCB. PCB the
                  company pays for them isn't taken from their pay. */}
              <Row
                label="PCB (tax)"
                value={payslip.pcb - payslip.pcbBorneByEmployer + payslip.voluntaryPcb}
              />
              {/* Loan repayment, zakat, CP38, one-offs — each by the name it
                  was given. */}
              {payslip.deductionLines ? (
                payslip.deductionLines.map((line, i) => (
                  <Row key={`${line.label}-${i}`} label={line.label} value={line.amount} />
                ))
              ) : (
                <>
                  {payslip.cp38 > 0 ? <Row label="CP38" value={payslip.cp38} /> : null}
                  {payslip.zakat > 0 ? <Row label="Zakat" value={payslip.zakat} /> : null}
                  {payslip.totalDeductions > 0 ? (
                    <Row label="Other deductions" value={payslip.totalDeductions} />
                  ) : null}
                </>
              )}
            </Group>

            {payslip.pcbBorneByEmployer > 0 ? (
              <Group title="Paid by your employer">
                <Row label="PCB (tax)" value={payslip.pcbBorneByEmployer} />
              </Group>
            ) : null}

            {/* Earnings and deductions above are already listed by name, so
                only what isn't in pay is left to show. */}
            {payslip.earningLines && payslip.deductionLines ? (
              payslip.notInPayLines.length > 0 ? (
                <Group title="Not paid in cash">
                  {payslip.notInPayLines.map((line, i) => (
                    <Row key={`${line.label}-${i}`} label={line.label} value={line.amount} />
                  ))}
                </Group>
              ) : null
            ) : /* The named lines behind those totals — "Allowances RM 500" is
                   not an answer to "what was the RM 500 for". */
            payslip.lineItems.length > 0 ? (
              <Group title="Line items">
                {payslip.lineItems.map((line) => (
                  <Row
                    key={line.id}
                    label={line.label}
                    value={line.amount}
                    negative={line.kind === "DEDUCTION"}
                  />
                ))}
              </Group>
            ) : null}
          </>
        ) : null}
      </section>
    </div>,
    document.body,
  );
}

// What the one "Overtime" figure in Earnings is made of: one row per
// multiplier, with the pay for an hour at it.
function OvertimeTable({ rows }: { rows: PayslipOvertimeRow[] }) {
  return (
    <section className="mt-5">
      <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
        Overtime
      </p>
      <div className="mt-2 overflow-x-auto rounded-[18px] border border-border/60">
        <table className="w-full text-sm tabular-nums">
          <thead>
            <tr className="border-b border-border/50 text-xs text-muted-foreground">
              <th className="px-4 py-2 text-left font-medium">Type</th>
              <th className="px-4 py-2 text-right font-medium">Rate</th>
              <th className="px-4 py-2 text-right font-medium">Hrs</th>
              <th className="px-4 py-2 text-right font-medium">Amount</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border/50">
            {rows.map((row) => (
              <tr key={row.multiplier}>
                <td className="px-4 py-2.5 text-muted-foreground">{row.label}</td>
                <td className="px-4 py-2.5 text-right font-semibold text-foreground">
                  {row.rate.toFixed(4)}
                </td>
                <td className="px-4 py-2.5 text-right font-semibold text-foreground">
                  {row.hours.toFixed(2)}
                </td>
                <td className="px-4 py-2.5 text-right font-semibold text-foreground">
                  {rmWithUnit(row.amount)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function Group({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="mt-5">
      <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
        {title}
      </p>
      <dl className="mt-2 divide-y divide-border/50 rounded-[18px] border border-border/60">
        {children}
      </dl>
    </section>
  );
}

function Row({
  label,
  value,
  strong = false,
  negative = false,
}: {
  label: string;
  value: number;
  strong?: boolean;
  negative?: boolean;
}) {
  return (
    <div className="flex items-baseline justify-between gap-4 px-4 py-2.5 text-sm">
      <dt className={strong ? "font-bold text-foreground" : "text-muted-foreground"}>{label}</dt>
      <dd
        className={`tabular-nums ${
          strong ? "font-black text-foreground" : "font-semibold text-foreground"
        }`}
      >
        {negative ? "−" : ""}
        {rmWithUnit(value)}
      </dd>
    </div>
  );
}
