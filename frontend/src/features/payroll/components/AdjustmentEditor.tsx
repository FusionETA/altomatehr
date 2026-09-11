import { useEffect, useMemo, useState } from "react";
import { LoaderCircle, Plus, RotateCcw, Trash2, X } from "lucide-react";
import {
  clearAdjustment,
  getAdjustmentContext,
  saveAdjustment,
  type AdjustmentCategory,
  type PayslipLineKind,
  type FixedAllowanceOverride,
  type PayrollAdjustmentContext,
  type SavePayrollRunAdjustment,
} from "../api";
import { rm } from "../lib/payroll-format";
import {
  BUTTON,
  BUTTON_DANGER,
  BUTTON_GHOST,
  ERROR_PANEL,
  HINT,
  INPUT,
  INPUT_SM,
  LABEL,
  TEXTAREA,
  NOTE_PANEL,
  WARN_PANEL,
} from "../lib/ui";
import { CategoryPicker, StatutoryStrip } from "./AdjustmentCategoryPicker";
import { CheckBox } from "./PayrollCheckbox";
import { DrawerPortal } from "./DrawerPortal";

// What one employee gets on top of their salary this month, and what comes
// off it.
//
// Four blocks, in the order an admin thinks about them: the hours that were
// actually worked, overtime, the recurring rows this month departs from, and
// finally the one-offs. Saving replaces the whole row — see the DTO for why a
// partial patch could not express "I cleared the overtime".
type Line = {
  // Local only, so React keys survive a reorder or a delete.
  key: string;
  category: string;
  label: string;
  amount: string;
  treatAsRecurring: boolean;
};

export function AdjustmentEditor({
  runId,
  employeeProfileId,
  categories,
  onClose,
  onSaved,
}: {
  runId: string;
  employeeProfileId: string;
  categories: AdjustmentCategory[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [context, setContext] = useState<PayrollAdjustmentContext | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [otNormal, setOtNormal] = useState("");
  const [otRest, setOtRest] = useState("");
  const [otPublic, setOtPublic] = useState("");
  const [workedHours, setWorkedHours] = useState("");
  const [expectedHours, setExpectedHours] = useState("");
  const [notes, setNotes] = useState("");
  const [lines, setLines] = useState<Line[]>([]);
  const [overrides, setOverrides] = useState<Record<string, FixedAllowanceOverride>>({});

  const byCode = useMemo(
    () => new Map(categories.map((category) => [category.code, category])),
    [categories],
  );

  useEffect(() => {
    let live = true;

    getAdjustmentContext(runId, employeeProfileId)
      .then((next) => {
        if (!live) return;
        setContext(next);

        const saved = next.adjustment;
        setOtNormal(saved?.otNormalHours ? String(saved.otNormalHours) : "");
        setOtRest(saved?.otRestHours ? String(saved.otRestHours) : "");
        setOtPublic(saved?.otPublicHours ? String(saved.otPublicHours) : "");
        setNotes(saved?.notes ?? "");
        setOverrides(saved?.fixedAllowanceOverrides ?? {});

        setWorkedHours(saved?.workedHours == null ? "" : String(saved.workedHours));
        setExpectedHours(saved?.expectedHours == null ? "" : String(saved.expectedHours));

        setLines(
          (saved?.manualLineItems ?? []).map((item, index) => ({
            key: `saved-${index}`,
            category: item.category,
            label: item.label ?? "",
            amount: String(item.amount),
            treatAsRecurring: item.treatAsRecurring,
          })),
        );
      })
      .catch((e: unknown) => live && setError(e instanceof Error ? e.message : String(e)))
      .finally(() => live && setLoading(false));

    return () => {
      live = false;
    };
  }, [runId, employeeProfileId]);

  const monthly = context?.salaryType === "MONTHLY";
  const editable = context?.editable ?? false;

  // The KIND decides which half of the catalogue the row can pick from;
  // the category within it decides the statutory treatment.
  function addLine(kind: PayslipLineKind, category?: string) {
    const preset =
      category ?? (kind === "DEDUCTION" ? "deduct_salary_adjustment" : "allowance_standard");

    setLines((current) => [
      ...current,
      {
        key: `new-${Date.now()}-${current.length}`,
        category: preset,
        // Left empty on purpose. The category already names the row, and
        // copying that name into the label printed it twice on screen for
        // no gain — the server falls back to the category's own label when
        // this is blank, so an empty field IS the default.
        label: "",
        amount: "",
        treatAsRecurring: false,
      },
    ]);
  }

  function patchLine(key: string, patch: Partial<Line>) {
    setLines((current) =>
      current.map((line) => (line.key === key ? { ...line, ...patch } : line)),
    );
  }

  function patchOverride(index: number, patch: Partial<FixedAllowanceOverride>) {
    setOverrides((current) => {
      // A row with no override yet starts from "keep the profile amount".
      const base: FixedAllowanceOverride = current[String(index)] ?? {
        amount: null,
        skip: false,
      };

      return { ...current, [String(index)]: { ...base, ...patch } };
    });
  }

  async function save() {
    if (!context) return;

    setBusy("save");
    setError(null);

    const worked = workedHours.trim() === "" ? null : Number(workedHours);
    const expected = expectedHours.trim() === "" ? null : Number(expectedHours);

    const body: SavePayrollRunAdjustment = {
      otNormalHours: Number(otNormal) || 0,
      otRestHours: Number(otRest) || 0,
      otPublicHours: Number(otPublic) || 0,
      manualLineItems: lines
        // A row the admin added and never filled in is not an instruction to
        // pay zero — it is an abandoned row, so it is dropped rather than
        // saved as a line that renders on a payslip at RM 0.00.
        .filter((line) => line.amount.trim() !== "" && Number(line.amount) > 0)
        .map((line) => ({
          category: line.category,
          label: line.label.trim() || null,
          amount: Number(line.amount),
          treatAsRecurring: line.treatAsRecurring,
        })),
      fixedAllowanceOverrides: overrides,
      workedHours: worked,
      // Hourly staff are paid for what they worked, with nothing to compare
      // it against — the engine ignores an expected figure for them.
      expectedHours: monthly ? expected : null,
      notes: notes.trim() || null,
    };

    try {
      await saveAdjustment(runId, employeeProfileId, body);
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save these adjustments.");
    } finally {
      setBusy(null);
    }
  }

  async function clear() {
    setBusy("clear");
    setError(null);
    try {
      await clearAdjustment(runId, employeeProfileId);
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not clear these adjustments.");
    } finally {
      setBusy(null);
    }
  }

  return (
    <DrawerPortal label="Adjustments" onClose={onClose}>
      <div className="p-6">
        <header className="mb-6 flex items-start justify-between gap-4">
          <div>
            <h2 className="text-lg font-semibold text-foreground">
              {context?.employeeName ?? "Adjustments"}
            </h2>
            <p className={HINT}>
              One-off changes for this month only. They survive a regeneration;
              the payslip lines they produce do not.
            </p>
          </div>
          <button
            type="button"
            aria-label="Close"
            className="rounded-full p-2 text-muted-foreground transition hover:bg-muted hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background"
            onClick={onClose}
          >
            <X className="size-4" aria-hidden />
          </button>
        </header>

        {loading ? (
          <p className={NOTE_PANEL}>Loading…</p>
        ) : !context ? (
          <p className={ERROR_PANEL}>{error ?? "Could not load this employee."}</p>
        ) : (
          <div className="space-y-6">
            {!editable ? (
              <div className={WARN_PANEL}>
                This run is no longer a draft, so its inputs are fixed. Revert it to
                make changes.
              </div>
            ) : null}

            {/* ── Working hours ──────────────────────────────────────── */}
            <Block
              title="Working hours"
              hint={
                monthly
                  ? "Recorded on the payslip so the hours behind the month are visible. For monthly staff these do NOT change the pay — a monthly salary is day-based, and it is approved unpaid leave that docks it."
                  : "The hours paid this month. For hourly staff this IS the paid quantity, so a wrong figure is a wrong payslip."
              }
            >
              {/* Attendance figures mean nothing when the policy keeps this
                  employee off attendance — and for HOURLY staff a confident
                  zero would be a zero payslip, so the fallback is absent
                  rather than 0. */}
              {!context.attendanceApplies ? (
                <p className={`${HINT} mb-3`}>
                  This employee is not on attendance, so a blank field has nothing to
                  fall back to
                  {monthly
                    ? " and the payslip simply shows no hours."
                    : " — which for hourly pay means no hours are paid. Enter them here."}
                </p>
              ) : null}

              <div className="flex flex-wrap gap-3">
                <div className="w-44">
                  <label className={LABEL} htmlFor="adjWorked">
                    Worked hours
                  </label>
                  <input
                    id="adjWorked"
                    type="number"
                    step="0.1"
                    min="0"
                    className={INPUT}
                    disabled={!editable}
                    value={workedHours}
                    placeholder={placeholderFor(context.autoWorkedHours)}
                    onChange={(e) => setWorkedHours(e.target.value)}
                  />
                </div>

                {monthly ? (
                  <div className="w-44">
                    <label className={LABEL} htmlFor="adjExpected">
                      Expected hours
                    </label>
                    <input
                      id="adjExpected"
                      type="number"
                      step="0.1"
                      min="0"
                      className={INPUT}
                      disabled={!editable}
                      value={expectedHours}
                      placeholder={placeholderFor(context.autoExpectedHours)}
                      onChange={(e) => setExpectedHours(e.target.value)}
                    />
                  </div>
                ) : null}
              </div>

              <p className={`${HINT} mt-2`}>
                Leave blank to use what attendance computed (shown greyed in each
                field).
              </p>
            </Block>

            {/* ── Overtime ───────────────────────────────────────────── */}
            <Block
              title="Overtime"
              hint="Hours are yours to enter; the multipliers come from the employee's policy (or the Employment Act floor when they have none)."
            >
              {/* Typed hours are silently ignored when the policy banks OT as
                  time off — paying cash for hours already credited as leave
                  would pay them twice. Saying so beats accepting a number
                  that does nothing. */}
              {!context.cashOvertime ? (
                <div className={`${WARN_PANEL} mb-3`}>{context.overtimeDisabledReason}</div>
              ) : null}

              <div className="grid grid-cols-3 gap-3">
                <Hours
                  id="otNormal"
                  label="Normal day"
                  value={otNormal}
                  onChange={setOtNormal}
                  disabled={!editable || !context.cashOvertime}
                />
                <Hours
                  id="otRest"
                  label="Rest day"
                  value={otRest}
                  onChange={setOtRest}
                  disabled={!editable || !context.cashOvertime}
                />
                <Hours
                  id="otPublic"
                  label="Public holiday"
                  value={otPublic}
                  onChange={setOtPublic}
                  disabled={!editable || !context.cashOvertime}
                />
              </div>
            </Block>

            {/* ── Recurring rows, this month only ────────────────────── */}
            {context.fixedAllowances.length > 0 ? (
              <Block
                title="Recurring allowances"
                hint="From the employee's profile. Changing one here applies to this month only — the profile is untouched."
              >
                <ul className="space-y-2">
                  {context.fixedAllowances.map((row) => {
                    const override = overrides[String(row.index)];
                    const skipped = override?.skip === true;
                    const changed = skipped || (override?.amount ?? null) !== null;

                    return (
                      <li
                        key={row.index}
                        className="flex flex-wrap items-center gap-3 rounded-2xl border border-border/60 bg-card p-3"
                      >
                        <div className="min-w-0 flex-1">
                          <p className="text-sm font-medium text-foreground">
                            {row.name || byCode.get(row.category)?.label || row.category}
                          </p>
                          <p className="text-xs text-muted-foreground">
                            Normally RM {rm(row.amount)} ·{" "}
                            {byCode.get(row.category)?.label ?? row.category}
                          </p>
                        </div>

                        <div className="w-32 shrink-0">
                          <input
                            type="number"
                            step="0.01"
                            min="0"
                            aria-label={`Amount for ${row.name || row.category}`}
                            className={INPUT_SM}
                            disabled={!editable || skipped}
                            value={override?.amount ?? ""}
                            placeholder={rm(row.amount)}
                            onChange={(e) =>
                              patchOverride(row.index, {
                                amount: e.target.value === "" ? null : Number(e.target.value),
                              })
                            }
                          />
                        </div>

                        <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
                          <CheckBox
                            disabled={!editable}
                            checked={skipped}
                            onChange={(skip) => patchOverride(row.index, { skip })}
                            ariaLabel={`Skip ${row.name || row.category} this month`}
                          />
                          Skip
                        </label>

                        {changed && editable ? (
                          <button
                            type="button"
                            aria-label={`Reset ${row.name || row.category}`}
                            title="Back to the profile amount"
                            className="rounded-full p-1.5 text-muted-foreground transition hover:bg-muted hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background"
                            onClick={() =>
                              setOverrides((current) => {
                                const next = { ...current };
                                delete next[String(row.index)];
                                return next;
                              })
                            }
                          >
                            <RotateCcw className="size-3.5" aria-hidden />
                          </button>
                        ) : null}
                      </li>
                    );
                  })}
                </ul>
              </Block>
            ) : null}

            {/* ── One-offs ───────────────────────────────────────────── */}
            {/* Grouped and labelled, because with more than one row an
                unlabelled stack of three boxes is unreadable — you cannot
                tell the display name from the amount, or an allowance from
                a deduction. */}
            <section className="rounded-2xl border border-border/70 bg-card/60 p-4">
              <header className="mb-3 flex flex-wrap items-start justify-between gap-3">
                <div>
                  <h3 className="text-sm font-semibold text-foreground">
                    One-off line items
                  </h3>
                  <p className={HINT}>
                    Allowances and deductions that apply to this run only. Recurring
                    ones live on the employee's payroll profile.
                  </p>
                </div>

                {editable ? (
                  <div className="flex flex-wrap items-center gap-1">
                    <AddLine label="Allowance" onClick={() => addLine("ALLOWANCE")} />
                    <AddLine label="Deduction" onClick={() => addLine("DEDUCTION")} />
                    {/* Same statutory treatment as a live claim: it feeds
                        gross but no contribution base. */}
                    <AddLine
                      label="Expense claim"
                      onClick={() => addLine("ALLOWANCE", "wages_expense_claim")}
                    />
                  </div>
                ) : null}
              </header>

              {lines.length === 0 ? (
                <p className={HINT}>
                  No one-off line items.
                  {editable ? " Add an allowance or a deduction above." : ""}
                </p>
              ) : (
                <div className="space-y-4">
                  {LINE_GROUPS.map(({ id, heading, match }) => {
                    const rows = lines.filter((line) => match(line, byCode));
                    if (rows.length === 0) return null;

                    return (
                      <div key={id}>
                        <p className="mb-2 text-xs font-medium text-muted-foreground">
                          {heading} ({rows.length})
                        </p>

                        <ul className="space-y-2">
                          {rows.map((line) => (
                            <LineRow
                              key={line.key}
                              line={line}
                              meta={byCode.get(line.category)}
                              categories={categories}
                              editable={editable}
                              onPatch={(patch) => patchLine(line.key, patch)}
                              onRemove={() =>
                                setLines((current) =>
                                  current.filter((entry) => entry.key !== line.key),
                                )
                              }
                            />
                          ))}
                        </ul>
                      </div>
                    );
                  })}
                </div>
              )}
            </section>


            {/* ── Loans, for information ─────────────────────────────── */}
            {context.loanInstallments.length > 0 ? (
              <Block
                title="Loan repayments"
                hint="Deducted automatically from this month. Edited on the Loans tab, not here."
              >
                <ul className="space-y-1 text-sm">
                  {context.loanInstallments.map((installment) => (
                    <li
                      key={`${installment.loanId}-${installment.label}`}
                      className="flex justify-between rounded-xl border border-border/60 bg-muted/30 px-3 py-2"
                    >
                      <span className="text-muted-foreground">{installment.label}</span>
                      <span className="tabular-nums text-foreground">
                        {rm(installment.amount)}
                      </span>
                    </li>
                  ))}
                </ul>
              </Block>
            ) : null}

            {/* ── Notes ──────────────────────────────────────────────── */}
            <Block title="Notes" hint="Why this month departs from the usual. Not printed on the payslip.">
              <textarea
                aria-label="Notes"
                rows={3}
                maxLength={2000}
                className={TEXTAREA}
                disabled={!editable}
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
              />
            </Block>

            {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

            {editable ? (
              <div className="sticky bottom-0 -mx-6 flex flex-wrap gap-3 border-t border-border bg-background px-6 py-4">
                <button type="button" className={BUTTON} disabled={busy !== null} onClick={() => void save()}>
                  {busy === "save" ? (
                    <LoaderCircle className="size-4 animate-spin" aria-hidden />
                  ) : null}
                  Save adjustments
                </button>

                {context.adjustment ? (
                  <button
                    type="button"
                    className={BUTTON_DANGER}
                    disabled={busy !== null}
                    onClick={() => void clear()}
                  >
                    {busy === "clear" ? (
                      <LoaderCircle className="size-4 animate-spin" aria-hidden />
                    ) : null}
                    Clear everything
                  </button>
                ) : null}

                <button type="button" className={BUTTON_GHOST} onClick={onClose}>
                  Cancel
                </button>
              </div>
            ) : null}
          </div>
        )}
      </div>
    </DrawerPortal>
  );
}

// What an empty field falls back to, shown as its placeholder so the admin
// can see the figure they are choosing not to override.
function placeholderFor(auto: number | null): string {
  if (auto === null) return "—";

  // Attendance minutes divide into long decimals; nobody needs six of them.
  return String(Math.round(auto * 10) / 10);
}

function Block({
  title,
  hint,
  children,
}: {
  title: string;
  hint: string;
  children: React.ReactNode;
}) {
  return (
    <section className="rounded-2xl border border-border/70 bg-card/60 p-4">
      <header className="mb-3">
        <h3 className="text-sm font-semibold text-foreground">{title}</h3>
        <p className={HINT}>{hint}</p>
      </header>
      {children}
    </section>
  );
}

function Hours({
  id,
  label,
  value,
  onChange,
  disabled,
}: {
  id: string;
  label: string;
  value: string;
  onChange: (next: string) => void;
  disabled: boolean;
}) {
  return (
    <div>
      <label className={`${LABEL} text-xs`} htmlFor={id}>
        {label}
      </label>
      <input
        id={id}
        type="number"
        step="0.5"
        min="0"
        placeholder="0"
        className={INPUT_SM}
        disabled={disabled}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
    </div>
  );
}

// An expense claim is an ALLOWANCE-kind row, so it needs its own bucket or
// it would hide among the allowances — and it is the one an admin is most
// likely to be looking for.
const EXPENSE_CLAIM = "wages_expense_claim";

const LINE_GROUPS: {
  id: string;
  heading: string;
  match: (line: Line, byCode: Map<string, AdjustmentCategory>) => boolean;
}[] = [
  {
    id: "allowances",
    heading: "Allowances",
    match: (line, byCode) =>
      byCode.get(line.category)?.kind !== "DEDUCTION" && line.category !== EXPENSE_CLAIM,
  },
  {
    id: "deductions",
    heading: "Deductions",
    match: (line, byCode) => byCode.get(line.category)?.kind === "DEDUCTION",
  },
  {
    id: "claims",
    heading: "Expense claims",
    match: (line) => line.category === EXPENSE_CLAIM,
  },
];

function AddLine({ label, onClick }: { label: string; onClick: () => void }) {
  return (
    <button
      type="button"
      className="inline-flex h-8 items-center gap-1 rounded-lg px-2.5 text-xs font-semibold text-muted-foreground transition hover:bg-muted hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background"
      onClick={onClick}
    >
      <Plus className="size-3.5" aria-hidden />
      {label}
    </button>
  );
}

// Three labelled fields, then what the category does. The labels are the
// point: with several rows stacked, an unlabelled box could be the display
// name or the amount and you would have to click to find out.
function LineRow({
  line,
  meta,
  categories,
  editable,
  onPatch,
  onRemove,
}: {
  line: Line;
  meta: AdjustmentCategory | undefined;
  categories: AdjustmentCategory[];
  editable: boolean;
  onPatch: (patch: Partial<Line>) => void;
  onRemove: () => void;
}) {
  return (
    <li className="rounded-2xl border border-border/60 bg-card p-3">
      <div className="flex flex-wrap items-end gap-3">
        <div className="min-w-[200px] flex-1">
          <label className={`${LABEL} text-xs`}>Category</label>
          <CategoryPicker
            categories={categories}
            value={line.category}
            // An allowance row offers the allowance half of the catalogue,
            // a deduction row the other. Changing KIND is what the add
            // buttons are for.
            kind={meta?.kind}
            disabled={!editable}
            onChange={(code) => onPatch({ category: code })}
          />
        </div>

        <div className="min-w-[160px] flex-1">
          <label className={`${LABEL} text-xs`}>Display name</label>
          <input
            type="text"
            maxLength={200}
            aria-label="Display name"
            // Shows the name the row takes if left alone, so the default is
            // visible without being duplicated as a value to clear.
            placeholder={meta?.label ?? ""}
            className={INPUT_SM}
            disabled={!editable}
            value={line.label}
            onChange={(e) => onPatch({ label: e.target.value })}
          />
        </div>

        <div className="w-32 shrink-0">
          <label className={`${LABEL} text-xs`}>Amount (MYR)</label>
          <input
            type="number"
            step="0.01"
            min="0"
            aria-label="Amount"
            placeholder="0.00"
            className={INPUT_SM}
            disabled={!editable}
            value={line.amount}
            onChange={(e) => onPatch({ amount: e.target.value })}
          />
        </div>

        {editable ? (
          <button
            type="button"
            aria-label="Remove this line"
            className="mb-1 rounded-full p-2 text-muted-foreground transition hover:bg-destructive/10 hover:text-destructive focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 ring-offset-background"
            onClick={onRemove}
          >
            <Trash2 className="size-4" aria-hidden />
          </button>
        ) : null}
      </div>

      <StatutoryStrip category={meta} />

      {/* An annual bonus paid in twelve equal parts really is recurring, and
          taxing it as a spike each month over-withholds. */}
      {meta?.isAdditionalRemuneration ? (
        <label className="mt-2 flex items-center gap-2 text-xs text-muted-foreground">
          <CheckBox
            disabled={!editable}
            checked={line.treatAsRecurring}
            onChange={(treatAsRecurring) => onPatch({ treatAsRecurring })}
          />
          Paid every month at about this amount — tax it smoothly rather than as a
          one-off spike
        </label>
      ) : null}
    </li>
  );
}
