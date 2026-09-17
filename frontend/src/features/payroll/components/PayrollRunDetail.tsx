import { useCallback, useEffect, useState } from "react";
import { ArrowLeft, TriangleAlert } from "lucide-react";
import {
  getAdjustmentCategories,
  getPayrollReadiness,
  getPayrollRun,
  getSalaryChangeHints,
  type AdjustmentCategory,
  type PayrollReadiness,
  type PayrollRunDetail as RunDetail,
  type SalaryChangeHint,
  type SkippedEmployee,
} from "../api";
import { Skeleton, SkeletonRows } from "@/shared/components/Skeleton";
import { statusLabels, statusTone, dateTime } from "../lib/payroll-format";
import {
  BADGE,
  BUTTON_GHOST_SM,
  CARD,
  ERROR_PANEL,
  HINT,
  WARN_PANEL,
} from "../lib/ui";
import { PayrollRunActions } from "./PayrollRunActions";
import { PayrollRunAdjustments } from "./PayrollRunAdjustments";
import { PayrollRunClaims } from "./PayrollRunClaims";
import { PayrollRunTabs } from "./PayrollRunTabs";
import { PayrollRunAttention } from "./PayrollRunAttention";
import { PayrollRunDownloads } from "./PayrollRunDownloads";
import { PayrollPayslipsTable } from "./PayrollPayslipsTable";
import { SalaryChangeHints } from "./SalaryChangeHints";

// One month: its totals, what stands in the way of filing it, its payslips,
// and everything it produces.
//
// The order is the order an admin works in — what's wrong first, then the
// numbers, then the documents. Putting the downloads at the top would invite
// filing a run with an unresolved warning above it.
export function PayrollRunDetailView({
  runId,
  onBack,
}: {
  runId: string;
  onBack: () => void;
}) {
  const [detail, setDetail] = useState<RunDetail | null>(null);
  const [readiness, setReadiness] = useState<PayrollReadiness | null>(null);
  const [hints, setHints] = useState<SalaryChangeHint[]>([]);
  // Fetched once here and shared: both the payslips table (to mark a benefit
  // in kind as non-cash) and the adjustment editor need the catalogue, and
  // it is static reference data.
  const [categories, setCategories] = useState<AdjustmentCategory[]>([]);
  // Set from a payslip row's shortcut, handed to the adjustments section.
  const [adjusting, setAdjusting] = useState<string | null>(null);
  const [skipped, setSkipped] = useState<SkippedEmployee[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);

    // The run is the page. Readiness and the salary hints are advice on top —
    // if either read fails the month is still workable, so they degrade to
    // absent rather than taking the screen down.
    return Promise.all([
      getPayrollRun(runId),
      getPayrollReadiness(runId).catch(() => null),
      getSalaryChangeHints(runId).catch(() => []),
      getAdjustmentCategories().catch(() => []),
    ])
      .then(([nextDetail, nextReadiness, nextHints, nextCategories]) => {
        setDetail(nextDetail);
        setReadiness(nextReadiness);
        setHints(nextHints);
        setCategories(nextCategories);
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, [runId]);

  useEffect(() => {
    void load();
  }, [load]);

  // First open of a run only — a re-open keeps the detail we already had.
  if (loading && !detail) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-3">
          <Skeleton className="h-9 w-24 rounded-full" />
          <div className="space-y-2">
            <Skeleton className="h-6 w-40" />
            <Skeleton className="h-3 w-56" />
          </div>
        </div>
        <section className={`${CARD} overflow-x-auto p-0 sm:p-0`}>
          <table className="w-full min-w-[720px] border-collapse">
            <tbody>
              <SkeletonRows rows={4} widths={["w-40", "w-16", "w-16", "w-16", "w-16", "w-20"]} />
            </tbody>
          </table>
        </section>
      </div>
    );
  }
  if (error) return <section className={ERROR_PANEL}>Error: {error}</section>;
  if (!detail) return null;

  const { run, payslips } = detail;

  // One number for the tab: everything an admin has to look at before this
  // month can be filed.
  const attentionCount =
    payslips.filter((payslip) => payslip.netPay <= 0).length +
    (readiness && !readiness.ok ? readiness.totalMissingCount : 0) +
    skipped.length;
  const generated = run.generatedAt !== null;

  return (
    <div className="space-y-6">
      {/* ── Header ───────────────────────────────────────────────────── */}
      {/* No totals card. Gross, net and cost were shown here AND in the
          table's footer AND in its summary panel — three copies of the same
          three numbers, which is three places for them to disagree. The
          table is the single source; the reference dropped this card for
          exactly that reason. */}
      {/* Back sits inline with the period title, not stacked above it — a
          standalone row here just pushed the whole review down the page. */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <button
            type="button"
            className={`${BUTTON_GHOST_SM} shrink-0`}
            onClick={onBack}
          >
            <ArrowLeft className="size-4" aria-hidden />
            All runs
          </button>
          <div>
            <h1 className="text-xl font-semibold text-foreground">{run.periodLabel}</h1>
            <p className={HINT}>
              {run.employeeCount} payroll result{run.employeeCount === 1 ? "" : "s"} on file
              {generated ? ` · generated ${dateTime(run.generatedAt)}` : ""}
              {run.submittedAt ? ` · approved ${dateTime(run.submittedAt)}` : ""}
            </p>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          {run.source === "IMPORTED" ? (
            <span className={`${BADGE} border-border bg-muted/60 text-muted-foreground`}>
              Imported history
            </span>
          ) : null}
          <span className={`${BADGE} ${statusTone[run.status]}`}>
            {statusLabels[run.status]}
          </span>
        </div>
      </div>

      {/* ── What is wrong with the run itself ───────────────────────── */}
      {/* Stale and "sent back" are facts about the RUN. The people-shaped
          problems — zero net, missing fields, exclusions — moved into the
          Needs attention tab, where they can be read as a list rather than
          as three stacked banners above the figures. */}
      {run.isStale ? (
        <div className={WARN_PANEL}>
          <p className="flex items-center gap-2 font-semibold">
            <TriangleAlert className="size-4" aria-hidden />
            These payslips are behind their inputs
          </p>
          <p className="mt-1">
            An adjustment or an attached claim changed after this run was generated, so what is
            shown below is not what it would produce now. Regenerate before filing.
          </p>
        </div>
      ) : null}

      {run.approvalRejectionReason ? (
        <div className={WARN_PANEL}>
          <p className="font-semibold">Sent back by the approver</p>
          <p className="mt-1">{run.approvalRejectionReason}</p>
        </div>
      ) : null}

      <SalaryChangeHints hints={hints} />

      {/* ── The payslips, and what is in their way ───────────────────── */}
      <PayrollRunTabs
        payslipCount={payslips.length}
        attentionCount={attentionCount}
        payslips={
          <PayrollPayslipsTable
            runId={run.id}
            payslips={payslips}
            categories={categories}
            onAdjust={setAdjusting}
          />
        }
        attention={
          <PayrollRunAttention
            readiness={readiness}
            payslips={payslips}
            skipped={skipped}
          />
        }
      />

      {/* Adjustments have no standalone section once the run is generated:
          every payslip row carries its own adjust button, so a list of the
          same people underneath is the same thing twice. Before generation
          there are no rows to hang a button on, so the list IS the way in —
          and only then. */}
      {payslips.length === 0 ? (
        <PayrollRunAdjustments
          runId={run.id}
          editable={run.status === "DRAFT"}
          periodLabel={run.periodLabel}
          categories={categories}
          openFor={adjusting}
          onOpenHandled={() => setAdjusting(null)}
          onChanged={() => void load()}
        />
      ) : null}

      {/* Claims only when there is something to show or something to
          attach. An org whose claims settle through Xero will never have
          either, and a permanent card explaining its own emptiness is the
          kind of thing you learn to scroll past. */}
      <PayrollRunClaims
        runId={run.id}
        editable={run.status === "DRAFT"}
        onChanged={() => void load()}
      />

      <PayrollRunDownloads run={run} generated={generated} />

      {/* ── What happens next ────────────────────────────────────────── */}
      {/* At the foot, not the head. This page is a review — the warnings,
          the figures, then the inputs behind them — and approval freezes
          figures that feed year-to-date and cascade on a revert. A commit
          button above the thing it commits invites approving first and
          reading second. */}
      <section className={CARD}>
        <PayrollRunActions
          run={run}
          payslipCount={payslips.length}
          blockingCount={readiness && !readiness.ok ? readiness.totalMissingCount : 0}
          onChanged={() => void load()}
          onSkipped={setSkipped}
          onDeleted={onBack}
        />
      </section>
    </div>
  );
}

