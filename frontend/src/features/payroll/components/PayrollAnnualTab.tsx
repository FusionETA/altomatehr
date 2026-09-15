import { useState } from "react";
import { Calculator, Download, LoaderCircle } from "lucide-react";
import {
  downloadAnnualReport,
  getAnnualReportKinds,
  getPayrollAnnual,
  type PayrollAnnualReportMeta,
} from "../api";
import { saveFile } from "@/shared/lib/api-client";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonRows } from "@/shared/components/Skeleton";
import { Cp8dConverterModal } from "./Cp8dConverterModal";
import { rm } from "../lib/payroll-format";
import {
  BUTTON_GHOST,
  CARD,
  ERROR_PANEL,
  HINT,
  INPUT,
  LABEL,
  NOTE_PANEL,
  TD,
  TD_NUM,
  TH,
  TH_NUM,
  WARN_PANEL,
} from "../lib/ui";

// The year-end filings.
//
// The table comes before the downloads deliberately: Form E and the CP8D are
// a declaration, and an admin should have looked at what they are declaring
// before producing the file that declares it.
export function PayrollAnnualTab() {
  const now = new Date();
  // Year-end work is done in the FOLLOWING year — Form E is due 31 March —
  // so the year being filed is almost always the last one.
  const [year, setYear] = useState(now.getFullYear() - 1);
  const [converterOpen, setConverterOpen] = useState(false);

  const [busy, setBusy] = useState<string | null>(null);
  const [downloadError, setDownloadError] = useState<string | null>(null);

  // Cached, keyed by year: flicking between filing years shows the one you
  // looked at a moment ago at once, and only a year never opened waits.
  const { data: payload = null, loading, error } = useCachedQuery(
    `/payroll/annual/${year}`,
    () => getPayrollAnnual(year),
  );
  const { data: kinds = [] } = useCachedQuery<PayrollAnnualReportMeta[]>(
    "/payroll/annual/reports",
    getAnnualReportKinds,
  );

  async function get(meta: PayrollAnnualReportMeta) {
    setBusy(meta.kind);
    setDownloadError(null);
    try {
      saveFile(await downloadAnnualReport(year, meta.kind, meta.extension));
    } catch (err) {
      // A missing E-number comes back as a 409 naming the field. That
      // sentence is the whole value of the failure.
      setDownloadError(err instanceof Error ? err.message : `Could not produce the ${meta.title}.`);
    } finally {
      setBusy(null);
    }
  }

  const rows = payload?.employees ?? [];
  const totals = rows.reduce(
    (acc, row) => ({
      income: acc.income + row.totalIncome,
      pcb: acc.pcb + row.totalPcb,
      epf: acc.epf + row.totalEpfEmployee,
      socso: acc.socso + row.totalSocsoEmployee,
      eis: acc.eis + row.totalEisEmployee,
    }),
    { income: 0, pcb: 0, epf: 0, socso: 0, eis: 0 },
  );

  return (
    <div className="space-y-6">
      <section className={CARD}>
        <div className="flex flex-wrap items-center justify-between gap-6">
          <div>
            <label className={LABEL} htmlFor="annualYear">
              Filing year
            </label>
            <div className="mt-1 w-36">
              <input
                id="annualYear"
                type="number"
                min={2000}
                max={2100}
                className={INPUT}
                value={year}
                onChange={(e) => setYear(Number(e.target.value))}
              />
            </div>
          </div>

          {/* The totals only mean something once there's an approved run to
              sum — a row of zeros for an empty year just read as broken. */}
          {rows.length > 0 ? (
            <dl className="grid grid-cols-2 gap-x-8 gap-y-1 text-right sm:grid-cols-4">
              <Total label="Employees" value={rows.length} plain />
              <Total label="Income" value={totals.income} />
              <Total label="PCB" value={totals.pcb} />
              <Total label="EPF (employee)" value={totals.epf} />
            </dl>
          ) : null}
        </div>

        <p className={`${HINT} mt-4`}>
          Summed across this year's <strong>approved</strong> runs only. A draft is not
          remuneration that was paid, so a return built on one would be false.
        </p>
      </section>

      {error ? <section className={ERROR_PANEL}>Error: {error}</section> : null}

      {/* Both LHDN TXT files are named after the E-number and refuse without
          it, so this is worth saying before the download is pressed. */}
      {payload && payload.employerNo.length === 0 ? (
        <div className={WARN_PANEL}>
          <p className="font-semibold">No LHDN employer number on file</p>
          <p className="mt-1">
            The two CP8D upload files are named after it and cannot be produced without it. Add
            it under Settings → Employer details.
          </p>
        </div>
      ) : null}

      {loading ? (
        <section className={`${CARD} overflow-x-auto p-0 sm:p-0`}>
          <table className="w-full min-w-[1000px] border-collapse">
            <tbody>
              <SkeletonRows
                rows={4}
                widths={["w-40", "w-24", "w-20", "w-20", "w-20", "w-20"]}
              />
            </tbody>
          </table>
        </section>
      ) : rows.length === 0 ? (
        <section className={NOTE_PANEL}>
          No approved payroll runs in {year}, so there is nothing to file. If the org ran
          payroll elsewhere that year, import it from the <strong>Payroll runs</strong> tab first.
        </section>
      ) : (
        <section className={`${CARD} overflow-x-auto p-0 sm:p-0`}>
          <table className="w-full min-w-[1000px] border-collapse">
            <thead>
              <tr className="border-b border-border/70">
                <th className={TH}>Employee</th>
                <th className={TH}>Tax no.</th>
                <th className={TH_NUM}>Salary</th>
                <th className={TH_NUM}>Bonus etc.</th>
                <th className={TH_NUM}>Benefits</th>
                <th className={TH_NUM}>Total income</th>
                <th className={TH_NUM}>EPF</th>
                <th className={TH_NUM}>SOCSO</th>
                <th className={TH_NUM}>EIS</th>
                <th className={TH_NUM}>PCB</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.employeeProfileId} className="border-b border-border/40 last:border-0">
                  <td className={TD}>
                    <div className="font-medium text-foreground">{row.employeeName}</div>
                    <div className="text-xs text-muted-foreground">
                      {row.employeeCode || "No employee number"}
                    </div>
                  </td>
                  <td className={TD}>
                    {row.incomeTaxNumber ?? (
                      <span className="text-xs font-medium text-amber-700 dark:text-amber-400">
                        Missing
                      </span>
                    )}
                  </td>
                  <td className={TD_NUM}>{rm(row.grossSalary)}</td>
                  <td className={TD_NUM}>{rm(row.bonusAndCommission)}</td>
                  {/* Never part of gross or net, but still reportable income. */}
                  <td className={TD_NUM}>{rm(row.totalBik)}</td>
                  <td className={`${TD_NUM} font-semibold`}>{rm(row.totalIncome)}</td>
                  <td className={TD_NUM}>{rm(row.totalEpfEmployee)}</td>
                  <td className={TD_NUM}>{rm(row.totalSocsoEmployee)}</td>
                  <td className={TD_NUM}>{rm(row.totalEisEmployee)}</td>
                  <td className={TD_NUM}>{rm(row.totalPcb)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr className="border-t border-border/70 bg-muted/40 font-semibold">
                <td className={TD} colSpan={5}>
                  Total
                </td>
                <td className={TD_NUM}>{rm(totals.income)}</td>
                <td className={TD_NUM}>{rm(totals.epf)}</td>
                <td className={TD_NUM}>{rm(totals.socso)}</td>
                <td className={TD_NUM}>{rm(totals.eis)}</td>
                <td className={TD_NUM}>{rm(totals.pcb)}</td>
              </tr>
            </tfoot>
          </table>
        </section>
      )}

      {kinds.length > 0 ? (
        <section className={`${CARD} space-y-6`}>
          <Group
            title="Forms"
            subtitle="What the employer keeps and hands out."
            items={kinds.filter((k) => k.group === "FORMS")}
            busy={busy}
            onPick={get}
          />
          <Group
            title="LHDN e-CP8D upload"
            subtitle="The pipe-delimited pair the portal expects. Upload both."
            items={kinds.filter((k) => k.group === "LHDN_TXT")}
            busy={busy}
            onPick={get}
          />

          {/* Sits under the generated pair rather than beside it: this builds
              the same two files from rows typed by hand, which is what you
              want only when there is no payroll here to build them from. */}
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border/60 bg-muted/30 px-4 py-3">
            <div className="min-w-0">
              <p className="text-sm font-semibold text-foreground">No payroll here for that year?</p>
              <p className="text-xs text-muted-foreground">
                Type the rows by hand instead — for a mid-year cutover, a one-off correction, or a
                dry run against the portal.
              </p>
            </div>
            <button
              type="button"
              onClick={() => setConverterOpen(true)}
              className="inline-flex shrink-0 items-center gap-1.5 rounded-xl border border-border/70 bg-card px-3.5 py-2 text-sm font-bold text-foreground hover:bg-muted"
            >
              <Calculator className="size-4" aria-hidden />
              CP8D converter
            </button>
          </div>

          {downloadError ? (
            <p className="text-sm font-medium text-destructive">{downloadError}</p>
          ) : null}
        </section>
      ) : null}

      {converterOpen ? (
        <Cp8dConverterModal
          defaultEmployerNo={payload?.employerNo}
          defaultEmployerName={payload?.organizationName}
          defaultYear={year}
          onClose={() => setConverterOpen(false)}
        />
      ) : null}
    </div>
  );
}

function Total({
  label,
  value,
  plain,
}: {
  label: string;
  value: number;
  plain?: boolean;
}) {
  return (
    <div>
      <dt className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </dt>
      <dd className="tabular-nums text-foreground">{plain ? value : rm(value)}</dd>
    </div>
  );
}

function Group({
  title,
  subtitle,
  items,
  busy,
  onPick,
}: {
  title: string;
  subtitle: string;
  items: PayrollAnnualReportMeta[];
  busy: string | null;
  onPick: (meta: PayrollAnnualReportMeta) => void;
}) {
  if (items.length === 0) return null;

  return (
    <div>
      <header className="mb-3">
        <h2 className="text-base font-semibold text-foreground">{title}</h2>
        <p className={HINT}>{subtitle}</p>
      </header>

      <ul className="grid gap-2 sm:grid-cols-2">
        {items.map((meta) => (
          <li key={meta.kind}>
            <button
              type="button"
              className={`${BUTTON_GHOST} h-auto w-full items-start justify-start gap-3 rounded-2xl p-3 text-left`}
              disabled={busy !== null}
              onClick={() => onPick(meta)}
            >
              <span className="mt-0.5 shrink-0 text-muted-foreground">
                {busy === meta.kind ? (
                  <LoaderCircle className="size-4 animate-spin" aria-hidden />
                ) : (
                  <Download className="size-4" aria-hidden />
                )}
              </span>
              <span>
                <span className="block text-sm font-semibold text-foreground">{meta.title}</span>
                <span className="mt-0.5 block text-xs font-normal text-muted-foreground">
                  {meta.description}
                </span>
              </span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
