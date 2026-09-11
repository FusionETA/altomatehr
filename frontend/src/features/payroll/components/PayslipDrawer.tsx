import { X } from "lucide-react";
import type { Payslip } from "../api";
import { rm, warningLabel } from "../lib/payroll-format";
import { HINT, TD, TD_NUM, TH, TH_NUM } from "../lib/ui";
import { DrawerPortal } from "./DrawerPortal";

// One payslip in full: what made up the gross, what came out, and what the
// month cost the employer.
//
// The arrangement mirrors the printed payslip on purpose — an employee
// querying a figure is holding the PDF, and the person answering should be
// looking at the same shape.
export function PayslipDrawer({
  payslip,
  onClose,
}: {
  payslip: Payslip;
  onClose: () => void;
}) {
  const earnings = payslip.lineItems.filter(
    (item) => item.kind === "ALLOWANCE" || item.kind === "REIMBURSEMENT",
  );
  const deductions = payslip.lineItems.filter((item) => item.kind === "DEDUCTION");

  // Mirrors the engine: gross less what the employee paid. Shown as a list
  // rather than a single figure so the arithmetic is checkable by eye.
  const statutory = [
    { label: "EPF (employee)", value: payslip.epfEmployee },
    { label: "SOCSO (employee)", value: payslip.socsoEmployee },
    { label: "EIS (employee)", value: payslip.eisEmployee },
    { label: "SKBBK", value: payslip.skbbkEmployee },
    { label: "PCB / MTD", value: payslip.pcb },
    { label: "CP38", value: payslip.cp38 },
    { label: "Zakat", value: payslip.zakat },
  ].filter((row) => row.value > 0);

  return (
    <DrawerPortal label={`Payslip for ${payslip.snapshotName}`} onClose={onClose}>
      <div className="p-6">
        <header className="mb-6 flex items-start justify-between gap-4">
          <div>
            <h2 className="text-lg font-semibold text-foreground">
              {payslip.snapshotName}
            </h2>
            <p className={HINT}>
              {[
                payslip.snapshotPosition,
                payslip.snapshotEmployeeNumber,
                payslip.snapshotSalaryType === "HOURLY" ? "Hourly" : "Monthly",
              ]
                .filter(Boolean)
                .join(" · ")}
            </p>
          </div>

          <button
            type="button"
            aria-label="Close"
            className="rounded-lg p-1.5 text-muted-foreground transition hover:bg-muted hover:text-foreground"
            onClick={onClose}
          >
            <X className="size-5" aria-hidden />
          </button>
        </header>

        {payslip.statutoryWarnings.length > 0 ? (
          <div className="mb-6 rounded-2xl border border-amber-500/30 bg-amber-500/10 p-3 text-sm text-amber-800 dark:text-amber-300">
            <p className="font-semibold">Missing for the statutory files</p>
            <ul className="mt-1 list-inside list-disc">
              {payslip.statutoryWarnings.map((warning) => (
                <li key={warning}>{warningLabel(warning)}</li>
              ))}
            </ul>
          </div>
        ) : null}

        <Section title="Earnings">
          <Row label="Basic pay" value={payslip.basicPay} />
          {payslip.proratedPay !== payslip.basicPay ? (
            <Row
              label={`Prorated pay (${payslip.proratedDays}/${payslip.prorationDaysInPeriod} days)`}
              value={payslip.proratedPay}
            />
          ) : null}
          {payslip.otPay > 0 ? <Row label="Overtime" value={payslip.otPay} /> : null}
          {earnings.map((item) => (
            <Row key={item.id} label={item.label} value={item.amount} />
          ))}
          <Row label="Gross pay" value={payslip.grossPay} total />
        </Section>

        <Section title="Deductions from the employee">
          {statutory.map((row) => (
            <Row key={row.label} label={row.label} value={row.value} />
          ))}
          {deductions.map((item) => (
            <Row key={item.id} label={item.label} value={item.amount} />
          ))}
          <Row label="Net pay" value={payslip.netPay} total />
        </Section>

        {/* Non-cash: it never reached gross or net, but it IS part of what
            the employee declares. Kept apart so nobody adds it to the net. */}
        {payslip.totalBenefitsInKind > 0 ? (
          <Section title="Benefits in kind">
            <Row label="Reportable, not paid in cash" value={payslip.totalBenefitsInKind} />
          </Section>
        ) : null}

        <Section title="Paid by the employer">
          <Row label="EPF (employer)" value={payslip.epfEmployer} />
          <Row label="SOCSO (employer)" value={payslip.socsoEmployer} />
          <Row label="EIS (employer)" value={payslip.eisEmployer} />
          {payslip.hrdf > 0 ? <Row label="HRD Corp levy" value={payslip.hrdf} /> : null}
          <Row label="Total cost to employer" value={payslip.totalCostToEmployer} total />
        </Section>

        {payslip.lineItems.length > 0 ? (
          <section className="mt-6">
            <h3 className="mb-2 text-sm font-semibold text-foreground">
              How each line was treated
            </h3>
            <p className={HINT}>
              Which statutory base a line feeds is decided by its category, not by its
              amount — this is why two allowances of the same size can move the tax
              differently.
            </p>

            <div className="mt-3 overflow-x-auto">
              <table className="w-full min-w-[420px] border-collapse">
                <thead>
                  <tr className="border-b border-border/70">
                    <th className={TH}>Line</th>
                    <th className={TH_NUM}>Amount</th>
                    <th className={TH}>Feeds</th>
                  </tr>
                </thead>
                <tbody>
                  {payslip.lineItems.map((item) => {
                    const feeds = [
                      item.subjectToEpf && "EPF",
                      item.subjectToSocso && "SOCSO",
                      item.subjectToEis && "EIS",
                      item.subjectToPcb && "PCB",
                    ].filter(Boolean);

                    return (
                      <tr key={item.id} className="border-b border-border/40 last:border-0">
                        <td className={TD}>{item.label}</td>
                        <td className={TD_NUM}>{rm(item.amount)}</td>
                        <td className={`${TD} text-xs text-muted-foreground`}>
                          {feeds.length > 0 ? feeds.join(" · ") : "Nothing"}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </section>
        ) : null}
      </div>
    </DrawerPortal>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="mb-6">
      <h3 className="mb-2 text-sm font-semibold text-foreground">{title}</h3>
      <dl className="space-y-1">{children}</dl>
    </section>
  );
}

function Row({
  label,
  value,
  total,
}: {
  label: string;
  value: number;
  total?: boolean;
}) {
  return (
    <div
      className={`flex items-baseline justify-between gap-4 ${
        total ? "mt-2 border-t border-border/60 pt-2 font-semibold" : ""
      }`}
    >
      <dt className={total ? "text-sm text-foreground" : "text-sm text-muted-foreground"}>
        {label}
      </dt>
      <dd className="text-sm tabular-nums text-foreground">{rm(value)}</dd>
    </div>
  );
}
