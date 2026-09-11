import { useMemo, useState } from "react";
import { Download, LoaderCircle, Sliders, TriangleAlert } from "lucide-react";
import {
  downloadPayslipPdf,
  type AdjustmentCategory,
  type Payslip,
  type PayslipLineItem,
} from "../api";
import { saveFile } from "@/shared/lib/api-client";
import { rm, warningLabels } from "../lib/payroll-format";
import { BADGE, CARD, HINT, NOTE_PANEL } from "../lib/ui";
import { PayslipDrawer } from "./PayslipDrawer";

// Every payslip on the run, in the three bands a payroll is reconciled in:
// the hours behind the month, what the EMPLOYEE paid, and what the EMPLOYER
// paid on top.
//
// The employer band matters as much as the employee one — the cheque to KWSP
// is the two halves added together, so a table that only showed the
// employee's side could not be reconciled against what is actually remitted.
// The bands are tinted so the eye can trace each one down the table without
// counting columns.
//
// Under each name sits the arithmetic that produced the gross: base salary,
// overtime with the hours behind it, and every line item signed. That is the
// first thing anyone asks when a figure looks wrong.

// Employee-paid: comes out of what reaches the bank.
const EMP_TINT = "bg-cyan-500/5 dark:bg-cyan-400/5";
// Employer-paid: never touches the employee's pay, but it is real money out.
const ER_TINT = "bg-orange-500/5 dark:bg-orange-400/5";

const CELL = "whitespace-nowrap px-2 py-2 text-right align-top tabular-nums";

// A small outlined box rather than a bare glyph. Next to a dense wall of
// figures an unbordered icon reads as decoration; the border says it is a
// control, and keeps the two row actions looking like a matched pair.
const ROW_ACTION =
  "inline-flex size-7 shrink-0 items-center justify-center rounded-md border border-border/70 text-muted-foreground transition hover:border-primary/40 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background disabled:opacity-50";
const HEAD = "whitespace-nowrap px-2 py-2 text-right text-[11px] font-semibold uppercase tracking-wide text-muted-foreground";

export function PayrollPayslipsTable({
  runId,
  payslips,
  categories = [],
  onAdjust,
}: {
  runId: string;
  payslips: Payslip[];
  // Only needed to mark a benefit-in-kind row as non-cash in the breakdown.
  // Absent simply means those rows read as ordinary allowances.
  categories?: AdjustmentCategory[];
  // Absent on a run that can no longer be edited.
  onAdjust?: (employeeProfileId: string) => void;
}) {
  const [open, setOpen] = useState<Payslip | null>(null);
  const [downloading, setDownloading] = useState<string | null>(null);

  const nonCash = useMemo(
    () => new Set(categories.filter((c) => c.nonCash).map((c) => c.code)),
    [categories],
  );

  const totals = useMemo(() => sum(payslips), [payslips]);

  if (payslips.length === 0) {
    return (
      <section className={NOTE_PANEL}>
        No payslips yet. Generate the run to produce them.
      </section>
    );
  }

  async function getPdf(payslip: Payslip) {
    setDownloading(payslip.id);
    try {
      saveFile(await downloadPayslipPdf(runId, payslip.employeeProfileId));
    } finally {
      setDownloading(null);
    }
  }

  return (
    <section className={`${CARD} p-0 sm:p-0`}>
      <header className="px-5 pt-5 sm:px-6">
        <h2 className="text-base font-semibold text-foreground">Payslips</h2>
        <p className={HINT}>
          {payslips.length} employee(s). Select a row for the full breakdown. Scroll
          sideways for the employer's side.
        </p>
      </header>

      <div className="mt-4 overflow-x-auto">
        <table className="w-full min-w-[1480px] border-collapse text-xs">
          <thead>
            {/* Band headings, so the two contribution groups are not read as
                one long row of similar-looking numbers. */}
            <tr>
              <th className="sticky left-0 z-20 bg-card" />
              <th
                colSpan={5}
                className="px-2 pb-1 text-center text-[10px] font-medium uppercase tracking-[0.18em] text-muted-foreground/70"
              >
                Hours and days
              </th>
              <th />
              <th
                colSpan={5}
                className={`${EMP_TINT} border-b-2 border-cyan-400/60 px-2 pb-1 text-center text-[10px] font-semibold uppercase tracking-[0.18em] text-cyan-700 dark:text-cyan-400`}
              >
                Employee pays
              </th>
              <th />
              <th
                colSpan={4}
                className={`${ER_TINT} border-b-2 border-orange-400/60 px-2 pb-1 text-center text-[10px] font-semibold uppercase tracking-[0.18em] text-orange-700 dark:text-orange-400`}
              >
                Employer pays
              </th>
              <th />
            </tr>

            <tr className="border-b border-border/70">
              <th className="sticky left-0 z-20 border-r border-border/60 bg-card px-3 py-2 text-left text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
                Employee
              </th>
              <th className={HEAD} title="Normal working hours recorded">Hrs</th>
              <th className={HEAD} title="Days paid of days in the period">Days</th>
              <th className={HEAD} title="Normal-day overtime hours">OT N</th>
              <th className={HEAD} title="Rest-day overtime hours">OT R</th>
              <th className={HEAD} title="Public-holiday overtime hours">OT PH</th>
              <th className={`${HEAD} text-foreground`}>Gross</th>
              <th className={`${HEAD} ${EMP_TINT}`}>PCB</th>
              <th className={`${HEAD} ${EMP_TINT}`}>EPF</th>
              <th className={`${HEAD} ${EMP_TINT}`}>SOCSO</th>
              <th className={`${HEAD} ${EMP_TINT}`}>EIS</th>
              <th className={`${HEAD} ${EMP_TINT}`} title="Self-employment social security (from Jun 2026)">
                SKBBK
              </th>
              <th className={`${HEAD} text-foreground`}>Net pay</th>
              <th className={`${HEAD} ${ER_TINT}`}>EPF</th>
              <th className={`${HEAD} ${ER_TINT}`}>SOCSO</th>
              <th className={`${HEAD} ${ER_TINT}`}>EIS</th>
              <th className={`${HEAD} ${ER_TINT}`} title="HRD Corp levy">HRDF</th>
              <th className={`${HEAD} text-foreground`}>Cost</th>
            </tr>
          </thead>

          <tbody>
            {payslips.map((payslip) => (
              <Row
                key={payslip.id}
                payslip={payslip}
                nonCash={nonCash}
                busy={downloading === payslip.id}
                onOpen={() => setOpen(payslip)}
                onPdf={() => void getPdf(payslip)}
                onAdjust={onAdjust}
              />
            ))}
          </tbody>

          <tfoot>
            <tr className="border-t border-border/70 bg-muted/40 font-semibold">
              <td className="sticky left-0 z-20 border-r border-border/60 bg-muted/40 px-3 py-2 text-left">
                Total
              </td>
              <td colSpan={5} />
              <td className={CELL}>{rm(totals.gross)}</td>
              <td className={`${CELL} ${EMP_TINT}`}>{rm(totals.pcb)}</td>
              <td className={`${CELL} ${EMP_TINT}`}>{rm(totals.epfEmp)}</td>
              <td className={`${CELL} ${EMP_TINT}`}>{rm(totals.socsoEmp)}</td>
              <td className={`${CELL} ${EMP_TINT}`}>{rm(totals.eisEmp)}</td>
              <td className={`${CELL} ${EMP_TINT}`}>{rm(totals.skbbk)}</td>
              <td className={CELL}>{rm(totals.net)}</td>
              <td className={`${CELL} ${ER_TINT}`}>{rm(totals.epfEr)}</td>
              <td className={`${CELL} ${ER_TINT}`}>{rm(totals.socsoEr)}</td>
              <td className={`${CELL} ${ER_TINT}`}>{rm(totals.eisEr)}</td>
              <td className={`${CELL} ${ER_TINT}`}>{rm(totals.hrdf)}</td>
              <td className={CELL}>{rm(totals.cost)}</td>
            </tr>
          </tfoot>
        </table>
      </div>

      {/* What actually gets paid to each agency: the two halves added
          together. Reading it off the bands above means adding two columns
          in your head, and that is the figure on the cheque.

          One tinted panel rather than a row of bordered pairs — the
          per-item rules read as a broken table, because half-width lines
          under label/value pairs look like cells that failed to align. */}
      {/* pt-5 matters: without it this panel started the pixel the table
          ended, so a tinted grid butted straight into the tinted Total row
          and the two read as one block that had gone wrong. */}
      <div className="px-5 pb-5 pt-5 sm:px-6">
        {/* Two totals blocks in a row need telling apart. The table's
            footer adds up its own columns; this is what leaves the bank
            account for each agency, which is both halves together. */}
        <p className="mb-2 text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          What gets remitted
        </p>

        <dl className="grid gap-x-8 gap-y-2.5 rounded-2xl border border-border/60 bg-muted/20 px-4 py-3.5 text-xs sm:grid-cols-2 lg:grid-cols-3">
          <Summary label="Employees paid" value={String(payslips.length)} />
          <Summary label="Total net pay" value={rm(totals.net)} />
          <Summary label="Total PCB payment" value={rm(totals.pcb + totals.cp38)} />
          <Summary label="Total EPF payment" value={rm(totals.epfEmp + totals.epfEr)} />
          <Summary label="Total SOCSO payment" value={rm(totals.socsoEmp + totals.socsoEr)} />
          <Summary label="Total EIS payment" value={rm(totals.eisEmp + totals.eisEr)} />
          {totals.skbbk > 0 ? (
            <Summary label="Total SKBBK payment" value={rm(totals.skbbk)} />
          ) : null}
          {totals.zakat > 0 ? (
            <Summary label="Total zakat" value={rm(totals.zakat)} />
          ) : null}
          {totals.hrdf > 0 ? (
            <Summary label="Total HRD Corp levy" value={rm(totals.hrdf)} />
          ) : null}
          {totals.bik > 0 ? (
            <Summary label="Benefits in kind (not cash)" value={rm(totals.bik)} />
          ) : null}
        </dl>
      </div>

      {open ? <PayslipDrawer payslip={open} onClose={() => setOpen(null)} /> : null}
    </section>
  );
}

function Row({
  payslip,
  nonCash,
  busy,
  onOpen,
  onPdf,
  onAdjust,
}: {
  payslip: Payslip;
  nonCash: Set<string>;
  busy: boolean;
  onOpen: () => void;
  onPdf: () => void;
  onAdjust?: (employeeProfileId: string) => void;
}) {
  const hourly = payslip.snapshotSalaryType === "HOURLY";

  return (
    <tr className="border-b border-border/40 transition last:border-0 hover:bg-muted/30">
      {/* Two columns inside the sticky cell: identity and the breakdown on
          the left, the actions pinned right. They live HERE rather than in a
          trailing column so they stay reachable without scrolling the wide
          contributions grid sideways to find them. */}
      <td className="sticky left-0 z-10 border-r border-border/60 bg-card px-3 py-2 align-top">
       <div className="flex items-start gap-2">
        <div className="min-w-0 flex-1">
        <button
          type="button"
          className="text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background"
          onClick={onOpen}
        >
          <span className="flex flex-wrap items-center gap-1.5">
            <span className="text-sm font-medium text-foreground">
              {payslip.snapshotName}
            </span>
            {payslip.statutoryWarnings.length > 0 ? (
              <span
                className={`${BADGE} border-amber-500/30 bg-amber-500/10 py-0.5 text-[10px] text-amber-700 dark:text-amber-400`}
                title={warningLabels(payslip.statutoryWarnings)}
              >
                <TriangleAlert className="size-3" aria-hidden />
                {payslip.statutoryWarnings.length} missing
              </span>
            ) : null}
          </span>

          <span className="block text-[10px] text-muted-foreground">
            {[payslip.snapshotEmployeeNumber, payslip.snapshotPosition]
              .filter(Boolean)
              .join(" · ") || "No employee number"}
          </span>
        </button>

        {/* The arithmetic behind the gross, in the order the payslip PDF
            prints it. This is what someone querying a figure is looking for. */}
        <ul className="mt-1 space-y-0.5 text-[10.5px] leading-tight">
          {breakdown(payslip, nonCash).map((item) => (
            <li key={item.label} className="flex justify-between gap-3">
              <span className="text-muted-foreground">{item.label}</span>
              <span
                className={
                  item.amount < 0
                    ? "tabular-nums text-destructive"
                    : item.signed
                      ? "tabular-nums text-emerald-600 dark:text-emerald-400"
                      : "tabular-nums text-muted-foreground"
                }
              >
                {item.signed && item.amount > 0 ? "+" : ""}
                {rm(item.amount)}
              </span>
            </li>
          ))}
        </ul>
        </div>

        <div className="flex shrink-0 items-center gap-1.5">
          {onAdjust ? (
            <button
              type="button"
              aria-label={`Adjust ${payslip.snapshotName}`}
              title="Edit OT / adjustments"
              className={ROW_ACTION}
              onClick={() => onAdjust(payslip.employeeProfileId)}
            >
              <Sliders className="size-3" aria-hidden />
            </button>
          ) : null}

          <button
            type="button"
            aria-label={`Download ${payslip.snapshotName}'s payslip`}
            title="Download this payslip"
            className={ROW_ACTION}
            disabled={busy}
            onClick={onPdf}
          >
            {busy ? (
              <LoaderCircle className="size-3 animate-spin" aria-hidden />
            ) : (
              <Download className="size-3" aria-hidden />
            )}
          </button>
        </div>
       </div>
      </td>

      {/* Days and the OT columns are a MONTHLY concept — an hourly employee
          is paid for hours, with no day count to prorate. */}
      <td className={CELL}>{hours(payslip.workedHours)}</td>
      <td className={CELL}>
        {hourly ? "" : `${payslip.proratedDays}/${payslip.prorationDaysInPeriod}`}
      </td>
      <td className={CELL}>{hourly ? "" : hours(payslip.otNormalHours)}</td>
      <td className={CELL}>{hourly ? "" : hours(payslip.otRestHours)}</td>
      <td className={CELL}>{hourly ? "" : hours(payslip.otPublicHours)}</td>

      <td className={`${CELL} font-semibold text-foreground`}>{rm(payslip.grossPay)}</td>

      <td className={`${CELL} ${EMP_TINT}`}>{rm(payslip.pcb)}</td>
      <td className={`${CELL} ${EMP_TINT}`}>{rm(payslip.epfEmployee)}</td>
      <td className={`${CELL} ${EMP_TINT}`}>{rm(payslip.socsoEmployee)}</td>
      <td className={`${CELL} ${EMP_TINT}`}>{rm(payslip.eisEmployee)}</td>
      <td className={`${CELL} ${EMP_TINT}`}>{rm(payslip.skbbkEmployee)}</td>

      <td className={`${CELL} font-semibold text-foreground`}>{rm(payslip.netPay)}</td>

      <td className={`${CELL} ${ER_TINT}`}>{rm(payslip.epfEmployer)}</td>
      <td className={`${CELL} ${ER_TINT}`}>{rm(payslip.socsoEmployer)}</td>
      <td className={`${CELL} ${ER_TINT}`}>{rm(payslip.eisEmployer)}</td>
      <td className={`${CELL} ${ER_TINT}`}>{rm(payslip.hrdf)}</td>

      <td className={`${CELL} font-semibold text-foreground`}>
        {rm(payslip.totalCostToEmployer)}
      </td>

    </tr>
  );
}

// Base pay, then overtime with the hours that earned it, then every line
// item signed by its kind. Mirrors the payslip PDF's order.
function breakdown(
  payslip: Payslip,
  nonCash: Set<string>,
): { label: string; amount: number; signed: boolean }[] {
  const items: { label: string; amount: number; signed: boolean }[] = [];

  if (payslip.proratedPay > 0) {
    items.push({ label: "Base pay", amount: payslip.proratedPay, signed: false });
  }

  if (payslip.otPay > 0) {
    const parts = [
      payslip.otNormalHours > 0 && `${payslip.otNormalHours} normal`,
      payslip.otRestHours > 0 && `${payslip.otRestHours} rest`,
      payslip.otPublicHours > 0 && `${payslip.otPublicHours} PH`,
    ].filter(Boolean);

    items.push({
      label: parts.length > 0 ? `Overtime (${parts.join(" + ")})` : "Overtime",
      amount: payslip.otPay,
      signed: true,
    });
  }

  for (const line of payslip.lineItems) {
    // A benefit in kind never reaches cash, so it must not read as though it
    // added to the gross beside it.
    if (isNonCash(line, nonCash)) {
      items.push({
        label: `${line.label} (benefit, not cash)`,
        amount: line.amount,
        signed: false,
      });
      continue;
    }

    items.push({
      label: line.label,
      amount: line.kind === "DEDUCTION" ? -line.amount : line.amount,
      signed: true,
    });
  }

  return items;
}

const isNonCash = (line: PayslipLineItem, nonCash: Set<string>) =>
  line.kind === "ALLOWANCE" && line.category !== null && nonCash.has(line.category);

const hours = (value: number | null): string =>
  value === null || value === 0 ? "" : String(Math.round(value * 10) / 10);

function sum(payslips: Payslip[]) {
  return payslips.reduce(
    (acc, p) => ({
      gross: acc.gross + p.grossPay,
      net: acc.net + p.netPay,
      cost: acc.cost + p.totalCostToEmployer,
      pcb: acc.pcb + p.pcb,
      cp38: acc.cp38 + p.cp38,
      zakat: acc.zakat + p.zakat,
      epfEmp: acc.epfEmp + p.epfEmployee,
      epfEr: acc.epfEr + p.epfEmployer,
      socsoEmp: acc.socsoEmp + p.socsoEmployee,
      socsoEr: acc.socsoEr + p.socsoEmployer,
      eisEmp: acc.eisEmp + p.eisEmployee,
      eisEr: acc.eisEr + p.eisEmployer,
      skbbk: acc.skbbk + p.skbbkEmployee,
      hrdf: acc.hrdf + p.hrdf,
      bik: acc.bik + p.totalBenefitsInKind,
    }),
    {
      gross: 0, net: 0, cost: 0, pcb: 0, cp38: 0, zakat: 0,
      epfEmp: 0, epfEr: 0, socsoEmp: 0, socsoEr: 0,
      eisEmp: 0, eisEr: 0, skbbk: 0, hrdf: 0, bik: 0,
    },
  );
}

function Summary({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="tabular-nums font-semibold text-foreground">{value}</dd>
    </div>
  );
}
