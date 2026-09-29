import { useState } from "react";
import { createPortal } from "react-dom";
import { Wallet } from "lucide-react";
import {
  SALARY_CHANGE_REASONS,
  SALARY_CHANGE_REASON_LABELS,
  type SalaryChangeReason,
} from "@/features/payroll/api";
import { rmWithUnit } from "@/features/payroll/lib/payroll-format";
import { useBodyScrollLock } from "@/shared/lib/use-body-scroll-lock";

export type SalaryClassification =
  | { kind: "CORRECTION" }
  | { kind: "CHANGE"; reason: SalaryChangeReason; effectiveDate: string; notes: string };

const today = () => new Date().toISOString().slice(0, 10);

const choice = (active: boolean) =>
  `block cursor-pointer rounded-2xl border p-4 text-sm transition ${
    active ? "border-primary bg-primary/5" : "border-border/70 bg-card hover:border-primary/40"
  }`;

const input =
  "mt-1 h-11 w-full rounded-xl border border-border/70 bg-background px-3 text-sm text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary";

// Asked when a salary that was already set is changed, as the previous system
// did: was the old figure a typo, or is this person's pay really changing?
// Only a real change goes into the salary history — with its reason and the
// date it takes effect — so the history an audit or IR dispute reads holds
// raises and promotions, not data-entry fixes.
export function SalaryChangeDialog({
  from,
  to,
  onCancel,
  onConfirm,
}: {
  from: string;
  to: string;
  onCancel: () => void;
  onConfirm: (classification: SalaryClassification) => void;
}) {
  useBodyScrollLock();

  const [kind, setKind] = useState<"CORRECTION" | "CHANGE" | null>(null);
  const [reason, setReason] = useState<SalaryChangeReason>("RAISE");
  const [effectiveDate, setEffectiveDate] = useState(today());
  const [notes, setNotes] = useState("");

  const canSave = kind === "CORRECTION" || (kind === "CHANGE" && effectiveDate !== "");

  function confirm() {
    if (kind === "CORRECTION") onConfirm({ kind: "CORRECTION" });
    else if (kind === "CHANGE") onConfirm({ kind: "CHANGE", reason, effectiveDate, notes: notes.trim() });
  }

  return createPortal(
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 px-4 py-5 backdrop-blur-md">
      <section className="max-h-[calc(100vh-2.5rem)] w-full max-w-md overflow-y-auto rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)] sm:p-6">
        <h2 className="flex items-center gap-2 text-xl font-black text-foreground">
          <Wallet className="h-5 w-5 text-primary" />
          Why is the salary changing?
        </h2>
        <p className="mt-1 text-sm text-muted-foreground">
          <span className="font-semibold text-foreground tabular-nums">{from}</span>
          {" → "}
          <span className="font-semibold text-foreground tabular-nums">{to}</span>
        </p>

        <div className="mt-4 space-y-3">
          <label className={choice(kind === "CORRECTION")}>
            <span className="flex items-start gap-3">
              <input
                type="radio"
                name="salary-change-kind"
                checked={kind === "CORRECTION"}
                onChange={() => setKind("CORRECTION")}
                className="mt-1"
              />
              <span>
                <span className="block font-bold text-foreground">Typo correction</span>
                <span className="block text-xs text-muted-foreground">
                  The old figure was a mistake. The salary is fixed, and no history entry is made.
                </span>
              </span>
            </span>
          </label>

          <label className={choice(kind === "CHANGE")}>
            <span className="flex items-start gap-3">
              <input
                type="radio"
                name="salary-change-kind"
                checked={kind === "CHANGE"}
                onChange={() => setKind("CHANGE")}
                className="mt-1"
              />
              <span>
                <span className="block font-bold text-foreground">Salary adjustment</span>
                <span className="block text-xs text-muted-foreground">
                  A raise, promotion or restructure. Recorded in the salary history with the date
                  it takes effect.
                </span>
              </span>
            </span>
          </label>

          {kind === "CHANGE" ? (
            <div className="space-y-3 rounded-2xl border border-border/60 bg-surface-low p-4">
              <div className="grid gap-3 sm:grid-cols-2">
                <label className="block">
                  <span className="text-xs font-semibold text-muted-foreground">Effective from</span>
                  <input
                    type="date"
                    value={effectiveDate}
                    onChange={(e) => setEffectiveDate(e.target.value)}
                    className={input}
                  />
                </label>
                <label className="block">
                  <span className="text-xs font-semibold text-muted-foreground">Reason</span>
                  <select
                    value={reason}
                    onChange={(e) => setReason(e.target.value as SalaryChangeReason)}
                    className={input}
                  >
                    {SALARY_CHANGE_REASONS.map((r) => (
                      <option key={r} value={r}>
                        {SALARY_CHANGE_REASON_LABELS[r]}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
              <label className="block">
                <span className="text-xs font-semibold text-muted-foreground">Notes (optional)</span>
                <input
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                  maxLength={500}
                  placeholder="e.g. Annual review 2026"
                  className={input}
                />
              </label>
              <p className="text-xs text-muted-foreground">
                A date inside a month that is already on a payroll run is flagged on that run, with
                the correction for the days at each rate.
              </p>
            </div>
          ) : null}
        </div>

        <div className="mt-5 grid gap-2 sm:grid-cols-2">
          <button
            type="button"
            disabled={!canSave}
            onClick={confirm}
            className="flex h-11 items-center justify-center gap-2 rounded-full bg-primary text-sm font-bold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
          >
            Save salary
          </button>
          <button
            type="button"
            onClick={onCancel}
            className="h-11 rounded-full border border-border bg-card text-sm font-bold text-foreground transition hover:bg-secondary/50"
          >
            Cancel
          </button>
        </div>
      </section>
    </div>,
    document.body,
  );
}

// "RM 5,000.00 / month" — how the dialog and the history show a salary.
export function salaryText(type: "MONTHLY" | "HOURLY", monthly: number | null, hourly: number | null) {
  return type === "HOURLY" ? `${rmWithUnit(hourly ?? 0)} / hour` : `${rmWithUnit(monthly ?? 0)} / month`;
}
