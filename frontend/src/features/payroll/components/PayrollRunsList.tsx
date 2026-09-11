import { useState } from "react";
import { ChevronRight, LoaderCircle, Plus, TriangleAlert } from "lucide-react";
import { createPayrollRun, type PayrollRun } from "../api";
import { PayrollSelect } from "./PayrollSelect";
import {
  MONTHS,
  rm,
  shortDate,
  sourceLabels,
  statusLabels,
  statusTone,
} from "../lib/payroll-format";
import {
  BADGE,
  BUTTON,
  CARD,
  ERROR_PANEL,
  INPUT,
  LABEL,
  NOTE_PANEL,
  TD,
  TD_NUM,
  TH,
  TH_NUM,
} from "../lib/ui";

// Every payroll run, newest period first, and the form that starts a new one.
//
// The table leads with the period and the status because that is what an
// admin scans for — "is March done?" — and carries the three figures that
// get reconciled: gross, net, and what the month actually costs the employer.
export function PayrollRunsList({
  runs,
  loading,
  error,
  onOpen,
  onCreated,
}: {
  runs: PayrollRun[];
  loading: boolean;
  error: string | null;
  onOpen: (runId: string) => void;
  onCreated: () => void;
}) {
  const now = new Date();
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  async function handleCreate(event: React.FormEvent) {
    event.preventDefault();
    setCreating(true);
    setCreateError(null);

    try {
      await createPayrollRun(year, month);
      onCreated();
    } catch (err) {
      // One run per period is a backend invariant, so "already exists" lands
      // here as a plain message rather than a duplicate row.
      setCreateError(err instanceof Error ? err.message : "Could not create the run.");
    } finally {
      setCreating(false);
    }
  }

  return (
    <div className="space-y-6">
      <section className={CARD}>
        <header className="mb-4">
          <h2 className="text-base font-semibold text-foreground">Start a payroll run</h2>
          <p className="mt-1 text-xs text-muted-foreground">
            One run per month. Creating it opens an empty draft — nothing is calculated until
            you generate it.
          </p>
        </header>

        <form onSubmit={handleCreate} className="flex flex-wrap items-end gap-4">
          <div className="w-40">
            <label className={LABEL} htmlFor="runMonth">
              Month
            </label>
            <PayrollSelect
              id="runMonth"
              value={String(month)}
              onChange={(next) => next && setMonth(Number(next))}
              options={MONTHS.map((name, index) => ({
                value: String(index + 1),
                label: name,
              }))}
            />
          </div>

          <div className="w-32">
            <label className={LABEL} htmlFor="runYear">
              Year
            </label>
            <input
              id="runYear"
              type="number"
              min={2000}
              max={2100}
              className={INPUT}
              value={year}
              onChange={(e) => setYear(Number(e.target.value))}
            />
          </div>

          <button type="submit" className={BUTTON} disabled={creating}>
            {creating ? (
              <LoaderCircle className="size-4 animate-spin" aria-hidden />
            ) : (
              <Plus className="size-4" aria-hidden />
            )}
            {creating ? "Creating…" : "Create draft"}
          </button>

          {createError ? (
            <p className="w-full text-sm font-medium text-destructive">{createError}</p>
          ) : null}
        </form>
      </section>

      {error ? (
        <section className={ERROR_PANEL}>Error: {error}</section>
      ) : loading ? (
        <section className={NOTE_PANEL}>Loading payroll runs…</section>
      ) : runs.length === 0 ? (
        <section className={NOTE_PANEL}>
          No payroll runs yet. Create one above to get started.
        </section>
      ) : (
        <section className={`${CARD} overflow-x-auto p-0 sm:p-0`}>
          <table className="w-full min-w-[860px] border-collapse">
            <thead>
              <tr className="border-b border-border/70">
                <th className={TH}>Period</th>
                <th className={TH}>Status</th>
                <th className={TH_NUM}>Staff</th>
                <th className={TH_NUM}>Gross</th>
                <th className={TH_NUM}>Net pay</th>
                <th className={TH_NUM}>Cost to employer</th>
                <th className={TH}>Generated</th>
                <th className={TH} aria-label="Open" />
              </tr>
            </thead>
            <tbody>
              {runs.map((run) => (
                <tr
                  key={run.id}
                  className="cursor-pointer border-b border-border/40 transition last:border-0 hover:bg-muted/40"
                  onClick={() => onOpen(run.id)}
                >
                  <td className={`${TD} font-medium`}>
                    <div className="flex items-center gap-2">
                      {run.periodLabel}
                      {run.source === "IMPORTED" ? (
                        <span className={`${BADGE} border-border bg-muted/60 text-muted-foreground`}>
                          {sourceLabels.IMPORTED}
                        </span>
                      ) : null}
                    </div>
                  </td>

                  <td className={TD}>
                    <div className="flex items-center gap-2">
                      <span className={`${BADGE} ${statusTone[run.status]}`}>
                        {statusLabels[run.status]}
                      </span>
                      {/* Stale means the figures on screen are behind the
                          inputs that produced them — worth seeing from the
                          list, not just after opening the run. */}
                      {run.isStale ? (
                        <span
                          className={`${BADGE} border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-400`}
                          title="Inputs changed since these payslips were generated"
                        >
                          <TriangleAlert className="size-3" aria-hidden />
                          Stale
                        </span>
                      ) : null}
                    </div>
                  </td>

                  <td className={TD_NUM}>{run.employeeCount || "—"}</td>
                  <td className={TD_NUM}>{rm(run.totalGross)}</td>
                  <td className={`${TD_NUM} font-semibold`}>{rm(run.totalNet)}</td>
                  <td className={TD_NUM}>{rm(run.totalCostToEmployer)}</td>
                  <td className={`${TD} text-muted-foreground`}>
                    {shortDate(run.generatedAt)}
                  </td>
                  <td className={`${TD} text-right`}>
                    <ChevronRight
                      className="ml-auto size-4 text-muted-foreground"
                      aria-hidden
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}
    </div>
  );
}
