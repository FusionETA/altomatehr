import { useState } from "react";
import { LoaderCircle } from "lucide-react";
import {
  pauseEmployeeLoan,
  replanEmployeeLoan,
  resumeEmployeeLoan,
  skipEmployeeLoanMonths,
  type EmployeeLoan,
  type ReplanLoan,
} from "../api";
import { MONTHS, periodLabel, rm } from "../lib/payroll-format";
import { PayrollSelect } from "./PayrollSelect";
import { BUTTON, BUTTON_GHOST, CARD, HINT, INPUT, LABEL, NOTE_PANEL } from "../lib/ui";

// Changing a loan that has started: re-plan what is still owed, pause it, or
// resume it. Months whose payroll is submitted or awaiting approval never
// change — the server enforces that; this form starts every picker at the
// first month that can.
export type LoanChangeKind = "replan" | "pause" | "resume";

type Period = { year: number; month: number };

const addMonths = ({ year, month }: Period, months: number): Period => {
  const raw = month - 1 + months;
  return { year: year + Math.floor(raw / 12), month: (raw % 12) + 1 };
};

const compare = (a: Period, b: Period) => a.year * 12 + a.month - (b.year * 12 + b.month);

export function LoanChangeForm({
  loan,
  kind,
  onSaved,
  onCancel,
}: {
  loan: EmployeeLoan;
  kind: LoanChangeKind;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const firstEditable: Period = { year: loan.firstEditableYear, month: loan.firstEditableMonth };
  const title =
    kind === "replan" ? "Re-plan loan" : kind === "pause" ? "Pause loan" : "Resume loan";

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      onSaved();
    } catch (err) {
      // Every refusal (a filed month, a plan that does not add up) comes back
      // as a written reason.
      setError(err instanceof Error ? err.message : "That change did not go through.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className={CARD}>
      <header className="mb-4">
        <h2 className="text-base font-semibold text-foreground">
          {title} · {loan.employeeName}
        </h2>
        <p className={HINT}>
          RM {rm(loan.principalAmount)} lent · RM {rm(loan.remainingAmount)} outstanding. Months
          already submitted or awaiting approval stay exactly as they are.
        </p>
      </header>

      {kind === "replan" ? (
        <ReplanFields loan={loan} firstEditable={firstEditable} busy={busy} onSubmit={run} />
      ) : kind === "pause" ? (
        <PauseFields loan={loan} firstEditable={firstEditable} busy={busy} onSubmit={run} />
      ) : (
        <ResumeFields loan={loan} firstEditable={firstEditable} busy={busy} onSubmit={run} />
      )}

      {error ? <p className="mt-4 text-sm font-medium text-destructive">{error}</p> : null}

      <div className="mt-4">
        <button type="button" className={BUTTON_GHOST} onClick={onCancel}>
          Back to loans
        </button>
      </div>
    </section>
  );
}

type FieldsProps = {
  loan: EmployeeLoan;
  firstEditable: Period;
  busy: boolean;
  onSubmit: (action: () => Promise<unknown>) => void;
};

// ─── Re-plan ──────────────────────────────────────────────────────────

type ReplanWay = "months" | "amount" | "each" | "settle";

function ReplanFields({ loan, firstEditable, busy, onSubmit }: FieldsProps) {
  const owed = loan.remainingToPlan;
  const firstIndex = loan.schedule.findIndex(
    (i) => i.year === firstEditable.year && i.month === firstEditable.month,
  );
  // The months still to come, as the loan stands — the starting point for
  // every option. Skipped (RM 0) months are left out; skipping is Pause's job.
  const current = (firstIndex < 0 ? [] : loan.schedule.slice(firstIndex))
    .map((i) => i.amount)
    .filter((amount) => amount > 0);

  const [way, setWay] = useState<ReplanWay>("months");
  const [count, setCount] = useState(String(Math.max(1, current.length)));
  const [amount, setAmount] = useState(String(current[0] ?? ""));
  const [each, setEach] = useState<string[]>(current.map((n) => n.toFixed(2)));

  const eachTotal = each.reduce((total, value) => total + (Number(value) || 0), 0);
  const eachDiff = Math.round((owed - eachTotal) * 100) / 100;

  // Only the count and the end month are shown — the exact installments are
  // worked out by the server (the last one absorbs the rounding) and appear on
  // the loan once saved.
  const months =
    way === "months"
      ? Number(count) || 0
      : way === "amount"
        ? Number(amount) > 0
          ? Math.ceil(owed / Number(amount))
          : 0
        : way === "each"
          ? each.length
          : 1;
  const ends = months > 0 ? addMonths(firstEditable, months - 1) : null;

  function submit(event: React.FormEvent) {
    event.preventDefault();
    const body: ReplanLoan =
      way === "months"
        ? { mode: "FIXED", installmentCount: Number(count) }
        : way === "amount"
          ? { mode: "CUSTOM", installmentAmount: Number(amount) }
          : way === "settle"
            ? { mode: "FIXED", installmentCount: 1 }
            : { mode: "CUSTOM", remainder: each.map((value) => Number(value)) };
    onSubmit(() => replanEmployeeLoan(loan.id, body));
  }

  if (owed <= 0) {
    return (
      <p className={NOTE_PANEL}>
        Every installment of this loan is already submitted or awaiting approval — there is
        nothing left to re-plan.
      </p>
    );
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <div className={NOTE_PANEL}>
        Still owed: <strong>RM {rm(owed)}</strong>, repaid from{" "}
        <strong>{periodLabel(firstEditable.year, firstEditable.month)}</strong>. The new plan replaces
        every month from then on, including any skipped ones, and the loan shows as a custom
        schedule.
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <label className={LABEL} htmlFor="replanWay">
            New plan
          </label>
          <PayrollSelect
            id="replanWay"
            value={way}
            onChange={(next) => next && setWay(next as ReplanWay)}
            options={[
              { value: "months", label: "Over a number of months" },
              { value: "amount", label: "A fixed amount each month" },
              { value: "each", label: "Set each month" },
              { value: "settle", label: "Settle it all next payroll" },
            ]}
          />
        </div>

        {way === "months" ? (
          <div>
            <label className={LABEL} htmlFor="replanCount">
              Number of months
            </label>
            <input
              id="replanCount"
              type="number"
              min="1"
              max="600"
              required
              className={INPUT}
              value={count}
              onChange={(e) => setCount(e.target.value)}
            />
          </div>
        ) : way === "amount" ? (
          <div>
            <label className={LABEL} htmlFor="replanAmount">
              Deducted each month (RM)
            </label>
            <input
              id="replanAmount"
              type="number"
              step="0.01"
              min="0.01"
              required
              className={INPUT}
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
            />
          </div>
        ) : null}
      </div>

      {way === "each" ? (
        <div>
          <p className={LABEL}>Amount per month (RM)</p>
          <ul className="grid gap-2 sm:grid-cols-3 lg:grid-cols-4">
            {each.map((value, index) => {
              const period = addMonths(firstEditable, index);
              return (
                <li key={index}>
                  <label className="text-xs text-muted-foreground" htmlFor={`replanEach${index}`}>
                    {periodLabel(period.year, period.month)}
                  </label>
                  <input
                    id={`replanEach${index}`}
                    type="number"
                    step="0.01"
                    min="0.01"
                    required
                    className={INPUT}
                    value={value}
                    onChange={(e) =>
                      setEach(each.map((old, i) => (i === index ? e.target.value : old)))
                    }
                  />
                </li>
              );
            })}
          </ul>
          <div className="mt-2 flex flex-wrap items-center gap-3">
            <button
              type="button"
              className={BUTTON_GHOST}
              onClick={() => setEach([...each, eachDiff > 0 ? eachDiff.toFixed(2) : ""])}
            >
              Add a month
            </button>
            {each.length > 1 ? (
              <button
                type="button"
                className={BUTTON_GHOST}
                onClick={() => setEach(each.slice(0, -1))}
              >
                Remove last month
              </button>
            ) : null}
            <span
              className={`text-sm ${eachDiff === 0 ? "text-muted-foreground" : "font-medium text-destructive"}`}
            >
              Total RM {rm(eachTotal)}
              {eachDiff === 0
                ? " — matches what is owed"
                : eachDiff > 0
                  ? ` — RM ${rm(eachDiff)} short`
                  : ` — RM ${rm(-eachDiff)} too much`}
            </span>
          </div>
        </div>
      ) : null}

      {ends ? (
        <p className={HINT}>
          {months} month{months === 1 ? "" : "s"}, last deduction in{" "}
          {periodLabel(ends.year, ends.month)}
          {loan.endYear && compare(ends, { year: loan.endYear, month: loan.endMonth }) !== 0
            ? ` (was ${periodLabel(loan.endYear, loan.endMonth)})`
            : ""}
          .
        </p>
      ) : null}

      <button
        type="submit"
        className={BUTTON}
        disabled={busy || (way === "each" && eachDiff !== 0)}
      >
        {busy ? <LoaderCircle className="size-4 animate-spin" aria-hidden /> : null}
        Save new plan
      </button>
    </form>
  );
}

// ─── Pause ────────────────────────────────────────────────────────────

type PauseWay = "skip" | "until";

function PauseFields({ loan, firstEditable, busy, onSubmit }: FieldsProps) {
  const [way, setWay] = useState<PauseWay>("skip");
  const [from, setFrom] = useState<Period>(firstEditable);
  const [months, setMonths] = useState("1");

  const skipCount = Number(months) || 0;

  function submit(event: React.FormEvent) {
    event.preventDefault();
    onSubmit(() =>
      way === "skip"
        ? skipEmployeeLoanMonths(loan.id, {
            fromYear: from.year,
            fromMonth: from.month,
            months: skipCount,
          })
        : pauseEmployeeLoan(loan.id, { fromYear: from.year, fromMonth: from.month }),
    );
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <label className={LABEL} htmlFor="pauseWay">
            Pause
          </label>
          <PayrollSelect
            id="pauseWay"
            value={way}
            onChange={(next) => next && setWay(next as PauseWay)}
            options={[
              { value: "skip", label: "Skip a number of months" },
              { value: "until", label: "Until I resume it" },
            ]}
          />
        </div>

        <MonthPicker id="pauseFrom" label="From" value={from} onChange={setFrom} />

        {way === "skip" ? (
          <div>
            <label className={LABEL} htmlFor="pauseMonths">
              Months to skip
            </label>
            <input
              id="pauseMonths"
              type="number"
              min="1"
              max="24"
              required
              className={INPUT}
              value={months}
              onChange={(e) => setMonths(e.target.value)}
            />
          </div>
        ) : null}
      </div>

      <p className={HINT}>
        {way === "skip"
          ? `Nothing is deducted for ${skipCount || "…"} month${skipCount === 1 ? "" : "s"} from ${periodLabel(from.year, from.month)}. Nothing is forgiven — what was due then moves into the next months that are not skipped, so the loan ends up to ${skipCount || "…"} month${skipCount === 1 ? "" : "s"} later.`
          : `Nothing is deducted from ${periodLabel(from.year, from.month)} until you resume it. When you do, the paused months are recorded as RM 0 and what was due in them moves to after the pause.`}{" "}
        The earliest month that can change is{" "}
        {periodLabel(firstEditable.year, firstEditable.month)}.
      </p>

      <button type="submit" className={BUTTON} disabled={busy}>
        {busy ? <LoaderCircle className="size-4 animate-spin" aria-hidden /> : null}
        {way === "skip" ? "Skip months" : "Pause loan"}
      </button>
    </form>
  );
}

// ─── Resume ───────────────────────────────────────────────────────────

function ResumeFields({ loan, firstEditable, busy, onSubmit }: FieldsProps) {
  const pausedFrom: Period = {
    year: loan.pausedFromYear ?? firstEditable.year,
    month: loan.pausedFromMonth ?? firstEditable.month,
  };
  // Deductions can restart no earlier than the pause itself, nor on a month
  // whose payroll is already submitted or awaiting approval.
  const earliest = compare(firstEditable, pausedFrom) > 0 ? firstEditable : pausedFrom;
  const [at, setAt] = useState<Period>(earliest);

  const pausedMonths = Math.max(0, compare(at, pausedFrom));

  function submit(event: React.FormEvent) {
    event.preventDefault();
    onSubmit(() => resumeEmployeeLoan(loan.id, { year: at.year, month: at.month }));
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <div className={NOTE_PANEL}>
        Paused since <strong>{periodLabel(pausedFrom.year, pausedFrom.month)}</strong>.
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <MonthPicker id="resumeAt" label="Deduct again from" value={at} onChange={setAt} />
      </div>

      <p className={HINT}>
        {pausedMonths > 0
          ? `${pausedMonths} month${pausedMonths === 1 ? "" : "s"} recorded as RM 0. What was due in them moves to after the pause, so the loan ends up to ${pausedMonths} month${pausedMonths === 1 ? "" : "s"} later.`
          : "No months are skipped — the schedule carries on as it was."}{" "}
        The earliest it can restart is {periodLabel(earliest.year, earliest.month)}.
      </p>

      <button type="submit" className={BUTTON} disabled={busy}>
        {busy ? <LoaderCircle className="size-4 animate-spin" aria-hidden /> : null}
        Resume loan
      </button>
    </form>
  );
}

function MonthPicker({
  id,
  label,
  value,
  onChange,
}: {
  id: string;
  label: string;
  value: Period;
  onChange: (next: Period) => void;
}) {
  return (
    <div>
      <label className={LABEL} htmlFor={id}>
        {label}
      </label>
      <div className="flex gap-2">
        <PayrollSelect
          id={id}
          value={String(value.month)}
          onChange={(next) => next && onChange({ ...value, month: Number(next) })}
          options={MONTHS.map((name, index) => ({ value: String(index + 1), label: name }))}
        />
        <div className="w-32 shrink-0">
          <input
            aria-label={`${label} year`}
            type="number"
            min={2000}
            max={2100}
            className={INPUT}
            value={value.year}
            onChange={(e) => onChange({ ...value, year: Number(e.target.value) })}
          />
        </div>
      </div>
    </div>
  );
}
