import { useState } from "react";
import { LoaderCircle } from "lucide-react";
import {
  createEmployeeLoan,
  updateEmployeeLoan,
  type EmployeeLoan,
  type LoanRepaymentMode,
  type PayrollEmployee,
  type SaveEmployeeLoan,
} from "../api";
import { MONTHS } from "../lib/payroll-format";
import { PayrollSelect } from "./PayrollSelect";
import { BUTTON, BUTTON_GHOST, CARD, HINT, INPUT, LABEL, WARN_PANEL } from "../lib/ui";

// Recording a loan or salary advance.
//
// The exact installments are NOT previewed here on purpose: the last one
// absorbs the rounding so the schedule sums to the principal exactly, and a
// client-side guess at that arithmetic would sometimes disagree with the
// server by a sen. The saved row shows the real schedule.
export function LoanForm({
  employees,
  existing,
  onSaved,
  onCancel,
}: {
  employees: PayrollEmployee[];
  existing: EmployeeLoan | null;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const now = new Date();

  const [employeeProfileId, setEmployeeProfileId] = useState(
    existing?.employeeProfileId ?? employees[0]?.employeeProfileId ?? "",
  );
  const [principal, setPrincipal] = useState(String(existing?.principalAmount ?? ""));
  const [mode, setMode] = useState<LoanRepaymentMode>(existing?.mode ?? "FIXED");
  const [count, setCount] = useState(String(existing?.installmentCount ?? 12));
  const [amount, setAmount] = useState(String(existing?.installmentAmount ?? ""));
  const [year, setYear] = useState(existing?.startYear ?? now.getFullYear());
  const [month, setMonth] = useState(existing?.startMonth ?? now.getMonth() + 1);
  const [notes, setNotes] = useState(existing?.notes ?? "");

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Editing a loan already deducting would restate months already filed, so
  // the server refuses it and the form says so rather than letting the admin
  // type into a field that cannot be saved.
  const locked = existing?.hasStarted ?? false;

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);

    const body: SaveEmployeeLoan = {
      employeeProfileId,
      principalAmount: Number(principal),
      mode,
      // The server reads whichever one the mode names and works out the
      // other, so the unused field is sent as null rather than as a stale
      // number left over from switching modes.
      installmentCount: mode === "FIXED" ? Number(count) : null,
      installmentAmount: mode === "CUSTOM" ? Number(amount) : null,
      startYear: year,
      startMonth: month,
      notes: notes.trim() || null,
    };

    try {
      if (existing) {
        await updateEmployeeLoan(existing.id, body);
      } else {
        await createEmployeeLoan(body);
      }
      onSaved();
    } catch (err) {
      // Terms that cannot be repaid and edits to a started loan both come
      // back as a written reason.
      setError(err instanceof Error ? err.message : "Could not save this loan.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className={CARD}>
      <header className="mb-4">
        <h2 className="text-base font-semibold text-foreground">
          {existing ? "Edit loan" : "Record a loan or advance"}
        </h2>
        <p className={HINT}>
          Deducted from each month's pay automatically, from the starting period onwards. An
          installment counts as paid once its month has been approved.
        </p>
      </header>

      {locked ? (
        <div className={`${WARN_PANEL} mb-4`}>
          Repayments have already been taken on this loan, so its terms are fixed — changing
          them would restate months that are already filed. Cancel it instead to stop the
          remaining deductions.
        </div>
      ) : null}

      <form onSubmit={submit} className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <div>
            <label className={LABEL} htmlFor="loanEmployee">
              Employee
            </label>
            <PayrollSelect
              id="loanEmployee"
              value={employeeProfileId}
              disabled={existing !== null}
              onChange={(next) => next && setEmployeeProfileId(next)}
              options={employees.map((employee) => ({
                value: employee.employeeProfileId,
                label: employee.employeeNumber
                  ? `${employee.name} (${employee.employeeNumber})`
                  : employee.name,
              }))}
            />
            {existing ? (
              <p className={HINT}>A loan cannot be moved to a different person.</p>
            ) : null}
          </div>

          <div>
            <label className={LABEL} htmlFor="loanPrincipal">
              Amount lent (RM)
            </label>
            <input
              id="loanPrincipal"
              type="number"
              step="0.01"
              min="0.01"
              required
              className={INPUT}
              value={principal}
              disabled={locked}
              onChange={(e) => setPrincipal(e.target.value)}
            />
          </div>

          <div>
            <label className={LABEL} htmlFor="loanMode">
              Repayment
            </label>
            <PayrollSelect
              id="loanMode"
              value={mode}
              disabled={locked}
              onChange={(next) => next && setMode(next as LoanRepaymentMode)}
              options={[
                { value: "FIXED", label: "Over a number of months" },
                { value: "CUSTOM", label: "A fixed amount each month" },
              ]}
            />
            <p className={HINT}>
              Either way the last installment takes up the rounding, so the total repaid is
              exactly the amount lent.
            </p>
          </div>

          {mode === "FIXED" ? (
            <div>
              <label className={LABEL} htmlFor="loanCount">
                Number of months
              </label>
              <input
                id="loanCount"
                type="number"
                min="1"
                max="600"
                required
                className={INPUT}
                value={count}
                disabled={locked}
                onChange={(e) => setCount(e.target.value)}
              />
            </div>
          ) : (
            <div>
              <label className={LABEL} htmlFor="loanAmount">
                Deducted each month (RM)
              </label>
              <input
                id="loanAmount"
                type="number"
                step="0.01"
                min="0.01"
                required
                className={INPUT}
                value={amount}
                disabled={locked}
                onChange={(e) => setAmount(e.target.value)}
              />
            </div>
          )}

          <div>
            <label className={LABEL} htmlFor="loanMonth">
              First deduction
            </label>
            <div className="flex gap-2">
              <PayrollSelect
                id="loanMonth"
                value={String(month)}
                disabled={locked}
                onChange={(next) => next && setMonth(Number(next))}
                options={MONTHS.map((name, index) => ({
                  value: String(index + 1),
                  label: name,
                }))}
              />
              <div className="w-32 shrink-0">
                <input
                  aria-label="First deduction year"
                  type="number"
                  min={2000}
                  max={2100}
                  className={INPUT}
                  value={year}
                  disabled={locked}
                  onChange={(e) => setYear(Number(e.target.value))}
                />
              </div>
            </div>
          </div>

          <div>
            <label className={LABEL} htmlFor="loanNotes">
              Notes
            </label>
            <input
              id="loanNotes"
              type="text"
              maxLength={500}
              className={INPUT}
              placeholder="What it was for"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
            />
          </div>
        </div>

        {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

        <div className="flex flex-wrap gap-3">
          <button type="submit" className={BUTTON} disabled={busy || employees.length === 0}>
            {busy ? <LoaderCircle className="size-4 animate-spin" aria-hidden /> : null}
            {existing ? "Save changes" : "Record loan"}
          </button>
          <button type="button" className={BUTTON_GHOST} onClick={onCancel}>
            Cancel
          </button>
        </div>
      </form>
    </section>
  );
}
