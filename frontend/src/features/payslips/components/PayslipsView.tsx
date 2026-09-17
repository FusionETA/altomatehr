import { useState } from "react";
import { Download, FileText, LoaderCircle } from "lucide-react";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonCards } from "@/shared/components/Skeleton";
import { rmWithUnit, shortDate } from "@/features/payroll/lib/payroll-format";
import { downloadMyPayslipPdf, getMyPayslips, type PayslipSummary } from "../api";
import { PayslipDetailModal } from "./PayslipDetailModal";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";

// The employee's own payslip history.
//
// Net pay leads, because that is the number anyone opens this screen for.
// Gross and the statutory deductions sit under it — an employee checking a
// payslip is usually reconciling what left their pay, not what it started as.
export function PayslipsView() {
  const [selected, setSelected] = useState<PayslipSummary | null>(null);
  const [downloading, setDownloading] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const payslipsQuery = useCachedQuery("/payslips", getMyPayslips);
  const payslips = payslipsQuery.data ?? [];

  async function download(payslip: PayslipSummary) {
    setDownloading(payslip.id);
    setError(null);
    try {
      await downloadMyPayslipPdf(payslip.id, payslip.periodLabel.replace(/\s+/g, "-").toLowerCase());
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not download that payslip.");
    } finally {
      setDownloading(null);
    }
  }

  if (payslipsQuery.loading) return <SkeletonCards />;

  if (payslipsQuery.error) {
    return (
      <section className={`${CARD} p-6 text-sm font-medium text-destructive`}>
        {payslipsQuery.error}
      </section>
    );
  }

  if (payslips.length === 0) {
    return (
      <section className={`${CARD} p-8 text-center`}>
        <FileText className="mx-auto h-6 w-6 text-muted-foreground" />
        <p className="mt-3 text-sm font-bold text-foreground">No payslips yet</p>
        {/* Says WHY there is nothing rather than just that there is nothing:
            a payslip appears when the month is finalised, not when it is run,
            so "nothing here" is often simply "not yet". */}
        <p className="mt-1 text-xs text-muted-foreground">
          A payslip appears here once payroll finalises the month it belongs to.
        </p>
      </section>
    );
  }

  return (
    <>
      {error ? <p className="mb-4 text-sm font-medium text-destructive">{error}</p> : null}

      <ul className="space-y-3">
        {payslips.map((payslip) => (
          <li key={payslip.id}>
            <div className={`${CARD} p-5`}>
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div className="min-w-0">
                  <p className="text-sm font-bold text-foreground">{payslip.periodLabel}</p>
                  <p className="mt-0.5 text-xs text-muted-foreground">
                    Paid {shortDate(payslip.submittedAt)}
                  </p>
                </div>
                <div className="shrink-0 text-right">
                  <p className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
                    Net pay
                  </p>
                  <p className="text-xl font-black tabular-nums text-foreground">
                    {rmWithUnit(payslip.netPay)}
                  </p>
                </div>
              </div>

              <dl className="mt-4 grid grid-cols-2 gap-x-4 gap-y-2 border-t border-border/50 pt-3 text-xs sm:grid-cols-5">
                <Figure label="Gross" value={payslip.grossPay} />
                <Figure label="EPF" value={payslip.epfEmployee} />
                <Figure label="SOCSO" value={payslip.socsoEmployee} />
                <Figure label="EIS" value={payslip.eisEmployee} />
                <Figure label="PCB" value={payslip.pcb} />
              </dl>

              <div className="mt-4 flex flex-wrap gap-2">
                <button
                  type="button"
                  onClick={() => setSelected(payslip)}
                  className="rounded-full border border-border/60 bg-card px-4 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground"
                >
                  View breakdown
                </button>
                <button
                  type="button"
                  onClick={() => void download(payslip)}
                  disabled={downloading === payslip.id}
                  className="inline-flex items-center gap-1.5 rounded-full border border-border/60 bg-card px-4 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground disabled:opacity-50"
                >
                  {downloading === payslip.id ? (
                    <LoaderCircle className="h-3.5 w-3.5 animate-spin" />
                  ) : (
                    <Download className="h-3.5 w-3.5" />
                  )}
                  PDF
                </button>
              </div>
            </div>
          </li>
        ))}
      </ul>

      {selected ? (
        <PayslipDetailModal summary={selected} onClose={() => setSelected(null)} />
      ) : null}
    </>
  );
}

function Figure({ label, value }: { label: string; value: number }) {
  return (
    <div>
      <dt className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
        {label}
      </dt>
      <dd className="mt-0.5 font-bold tabular-nums text-foreground">{rmWithUnit(value)}</dd>
    </div>
  );
}
