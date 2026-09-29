import { createPortal } from "react-dom";
import { History, LoaderCircle, X } from "lucide-react";
import type { SalaryChange } from "@/features/payroll/api";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";
import { salaryText } from "./SalaryChangeDialog";

const date = (iso: string) =>
  new Date(iso).toLocaleDateString("en-MY", { day: "numeric", month: "short", year: "numeric" });

// Every real salary change for one employee, newest first: what it was, what
// it became, from when, why, and who recorded it. Typo corrections are never
// in here — that is the point of asking when the salary is saved.
export function SalaryHistoryDialog({
  employeeName,
  history,
  loading,
  error,
  onClose,
}: {
  employeeName: string;
  history: SalaryChange[];
  loading: boolean;
  error: string | null;
  onClose: () => void;
}) {
  useBodyScrollLock();

  return createPortal(
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 px-4 py-5 backdrop-blur-md"
      onClick={onClose}
    >
      <section
        className="max-h-[calc(100vh-2.5rem)] w-full max-w-lg overflow-y-auto rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)] sm:p-6"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-3">
          <div>
            <h2 className="flex items-center gap-2 text-xl font-black text-foreground">
              <History className="h-5 w-5 text-primary" />
              Salary history
            </h2>
            <p className="mt-1 text-sm text-muted-foreground">{employeeName}</p>
          </div>
          <button
            type="button"
            aria-label="Close"
            onClick={onClose}
            className="rounded-full p-2 text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        {loading ? (
          <p className="mt-5 flex items-center gap-2 text-sm text-muted-foreground">
            <LoaderCircle className="h-4 w-4 animate-spin" /> Loading…
          </p>
        ) : error ? (
          <p className="mt-5 text-sm font-medium text-destructive">{error}</p>
        ) : history.length === 0 ? (
          <p className="mt-5 rounded-2xl border border-border/60 bg-surface-low p-4 text-sm text-muted-foreground">
            No salary adjustments recorded yet. A change saved as a salary adjustment appears here;
            typo corrections don't.
          </p>
        ) : (
          <ol className="mt-5 space-y-3">
            {history.map((c) => (
              <li key={c.id} className="rounded-2xl border border-border/60 bg-card p-4">
                <div className="flex flex-wrap items-baseline justify-between gap-2">
                  <p className="text-sm font-bold text-foreground">
                    {c.reasonLabel}
                    {c.raisePercent != null ? (
                      <span
                        className={`ml-2 text-xs font-bold ${
                          c.raisePercent >= 0 ? "text-emerald-700 dark:text-emerald-400" : "text-destructive"
                        }`}
                      >
                        {c.raisePercent >= 0 ? "+" : ""}
                        {c.raisePercent}%
                      </span>
                    ) : null}
                  </p>
                  <p className="text-xs font-semibold text-muted-foreground">
                    Effective {date(c.effectiveDate)}
                  </p>
                </div>
                <p className="mt-1 text-sm tabular-nums text-muted-foreground">
                  {salaryText(c.previousSalaryType, c.previousMonthlySalary, c.previousHourlyRate)}
                  {" → "}
                  <span className="font-semibold text-foreground">
                    {salaryText(c.newSalaryType, c.newMonthlySalary, c.newHourlyRate)}
                  </span>
                </p>
                {c.notes ? <p className="mt-1 text-sm text-foreground">{c.notes}</p> : null}
                <p className="mt-2 text-xs text-muted-foreground">
                  Recorded {date(c.createdAt)}
                  {c.changedByName ? ` by ${c.changedByName}` : ""}
                </p>
              </li>
            ))}
          </ol>
        )}
      </section>
    </div>,
    document.body,
  );
}
