import { useCallback, useEffect, useState } from "react";
import { SlidersHorizontal, Upload } from "lucide-react";
import { SearchInput } from "@/shared/components/SearchInput";
import { TablePager } from "@/shared/components/TablePager";
import { usePaged } from "@/shared/lib/use-paged";
import {
  getPayrollEmployees,
  getRunAdjustments,
  type AdjustmentCategory,
  type FixedAllowanceRow,
  type PayrollEmployee,
  type PayrollRunAdjustment,
} from "../api";
import { rm } from "../lib/payroll-format";
import { BADGE, BUTTON_GHOST_SM, CARD, ERROR_PANEL, HINT, TD, TH } from "../lib/ui";
import { AdjustmentEditor } from "./AdjustmentEditor";
import { ImportAdjustmentsDialog } from "./ImportAdjustmentsDialog";
import { TableSkeleton } from "./TableSkeleton";

// Who has something set for this month, and a way in for everyone else.
//
// Listed from the ROSTER rather than from the payslips: adjustments are
// inputs, so they have to be editable before a run has ever been generated —
// which is exactly when an admin wants to enter the month's overtime.
export function PayrollRunAdjustments({
  runId,
  memberIds,
  hasRoster,
  outsideAccess,
  editable,
  categories,
  showRoster,
  openFor,
  onOpenHandled,
  onChanged,
  periodLabel,
}: {
  runId: string;
  // The run's chosen roster. Empty means a pre-picker run: everyone.
  memberIds: string[];
  // The run has a roster even when `memberIds` is empty: a limited admin
  // gets only their own people, so a run of other staff arrives with none —
  // and that must show nobody, not everyone they can see.
  hasRoster: boolean;
  // The run holds people outside this admin's access.
  outsideAccess: boolean;
  editable: boolean;
  periodLabel: string;
  categories: AdjustmentCategory[];
  // False once the run has payslips — each row already carries its own
  // adjust button, so this component's own roster list would repeat them.
  // The component stays mounted either way: it also owns the dialog a
  // payslip row's shortcut opens via `openFor`.
  showRoster: boolean;
  // A payslip row's shortcut opens the editor here rather than mounting a
  // second copy of it beside the table.
  openFor: string | null;
  onOpenHandled: () => void;
  onChanged: () => void;
}) {
  const [employees, setEmployees] = useState<PayrollEmployee[]>([]);
  const [adjustments, setAdjustments] = useState<PayrollRunAdjustment[]>([]);
  const [open, setOpen] = useState<string | null>(null);
  const [importing, setImporting] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState("");

  // Stable key so a fresh-but-equal array from the parent doesn't refetch.
  const memberKey = memberIds.join(",");

  const load = useCallback(() => {
    setLoading(true);
    setError(null);

    const members = new Set(memberKey ? memberKey.split(",") : []);

    return Promise.all([getPayrollEmployees(), getRunAdjustments(runId)])
      .then(([nextEmployees, nextAdjustments]) => {
        // Only the people this run will pay — the ones ticked in the picker,
        // not the whole roster.
        setEmployees(
          members.size === 0 && !hasRoster
            ? nextEmployees
            : nextEmployees.filter((employee) => members.has(employee.employeeProfileId)),
        );
        setAdjustments(nextAdjustments);
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, [runId, memberKey, hasRoster]);

  useEffect(() => {
    void load();
  }, [load]);

  // A shortcut pressed on a payslip row.
  useEffect(() => {
    if (openFor === null) return;
    setOpen(openFor);
    onOpenHandled();
  }, [openFor, onOpenHandled]);

  const byEmployee = new Map(adjustments.map((row) => [row.employeeProfileId, row]));
  const withSomething = employees.filter((employee) =>
    byEmployee.has(employee.employeeProfileId),
  );

  // A company runs payroll for hundreds: find someone by name or staff number,
  // a page at a time, rather than scrolling one long list.
  const needle = query.trim().toLowerCase();
  const shown = needle
    ? employees.filter(
        (employee) =>
          employee.name.toLowerCase().includes(needle) ||
          (employee.employeeNumber ?? "").toLowerCase().includes(needle),
      )
    : employees;
  const paged = usePaged(shown, 25);

  return (
    <>
      {showRoster ? (
        <section className={CARD}>
          <header className="mb-4 flex flex-wrap items-start justify-between gap-3">
            <div className="min-w-0">
              <h2 className="text-base font-semibold text-foreground">One-off adjustments</h2>
              <p className={HINT}>
                Overtime hours, a bonus, a deduction, or a change to someone's recurring
                allowances — for this month only. These carry over when payroll is re-run;
                the payslip lines are rebuilt from them.
              </p>
            </div>

            {/* Only on a draft: the import writes through the same service the
                editor does, and that refuses anything else. */}
            {editable ? (
              <button
                type="button"
                onClick={() => setImporting(true)}
                className={`${BUTTON_GHOST_SM} shrink-0`}
              >
                <Upload className="size-3.5" aria-hidden />
                Import adjustments
              </button>
            ) : null}
          </header>

          {error ? <p className={ERROR_PANEL}>Error: {error}</p> : null}

          {loading ? (
            <TableSkeleton columns={3} label="Loading adjustments" />
          ) : employees.length === 0 ? (
            <p className={HINT}>
              {!hasRoster
                ? "No employees on the payroll yet."
                : outsideAccess
                  ? "No one on this run is within your admin access."
                  : "No one on this run is on the payroll any more."}
            </p>
          ) : (
            <>
              {withSomething.length === 0 ? (
                <p className={`${HINT} mb-3`}>
                  No one-off adjustments this month. Everyone is paid from their profile:
                  their salary plus the recurring items under “Every month”.
                </p>
              ) : null}

              <SearchInput
                value={query}
                onChange={setQuery}
                placeholder="Search by name or employee number"
                className="mb-3 sm:max-w-sm"
              />

              {shown.length === 0 ? (
                <p className={HINT}>No one on this run matches “{query.trim()}”.</p>
              ) : (
              <div className="overflow-x-auto">
                <table className="w-full min-w-[760px] border-collapse">
                  <thead>
                    <tr className="border-b border-border/70">
                      <th className={TH}>Employee</th>
                      <th className={TH}>Set for this month</th>
                      <th className={TH}>Every month (from profile)</th>
                      <th className={TH} aria-label="Edit" />
                    </tr>
                  </thead>
                  <tbody>
                    {paged.pageItems.map((employee) => {
                      const adjustment = byEmployee.get(employee.employeeProfileId);

                      return (
                        <tr
                          key={employee.employeeProfileId}
                          className="border-b border-border/40 last:border-0"
                        >
                          <td className={TD}>
                            <span className="font-medium text-foreground">{employee.name}</span>
                            {employee.employeeNumber ? (
                              <span className="ml-2 text-xs text-muted-foreground">
                                {employee.employeeNumber}
                              </span>
                            ) : null}
                          </td>

                          <td className={TD}>
                            {adjustment ? (
                              <div className="flex flex-wrap gap-1.5">
                                {summarise(adjustment).map((chip) => (
                                  <span
                                    key={chip}
                                    className={`${BADGE} border-sky-500/30 bg-sky-500/10 text-sky-700 dark:text-sky-400`}
                                  >
                                    {chip}
                                  </span>
                                ))}
                              </div>
                            ) : (
                              <span className="text-sm text-muted-foreground">—</span>
                            )}
                          </td>

                          <td className={TD}>
                            <RecurringItems
                              rows={employee.fixedAllowances ?? []}
                              overrides={adjustment?.fixedAllowanceOverrides ?? {}}
                              categories={categories}
                            />
                          </td>

                          <td className={`${TD} text-right`}>
                            <button
                              type="button"
                              className={BUTTON_GHOST_SM}
                              onClick={() => setOpen(employee.employeeProfileId)}
                            >
                              <SlidersHorizontal className="size-3.5" aria-hidden />
                              {/* A submitted run still opens, read-only — an
                                  admin asking "why was this month different?"
                                  needs to see the inputs, not be locked out. */}
                              {editable ? (adjustment ? "Edit" : "Adjust") : "View"}
                            </button>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
              )}
              <TablePager paged={paged} noun="employee" />
            </>
          )}
        </section>
      ) : null}

      {open ? (
        <AdjustmentEditor
          runId={runId}
          employeeProfileId={open}
          categories={categories}
          onClose={() => setOpen(null)}
          onSaved={() => {
            setOpen(null);
            void load();
            // The run's payslips are now behind their inputs, so the page
            // above has to re-read and show the staleness banner.
            onChanged();
          }}
        />
      ) : null}
    
      {importing ? (
        <ImportAdjustmentsDialog
          runId={runId}
          periodLabel={periodLabel}
          onClose={() => setImporting(false)}
          onImported={() => {
            void load();
            onChanged();
          }}
        />
      ) : null}
    </>
  );
}

// The profile's recurring items, as this run will pay them: an override for
// this month shows the new amount beside the usual one, a skip says so.
function RecurringItems({
  rows,
  overrides,
  categories,
}: {
  rows: FixedAllowanceRow[];
  overrides: PayrollRunAdjustment["fixedAllowanceOverrides"];
  categories: AdjustmentCategory[];
}) {
  if (rows.length === 0) {
    return <span className="text-sm text-muted-foreground">—</span>;
  }
  const byCode = new Map(categories.map((category) => [category.code, category]));

  return (
    <div className="flex flex-wrap gap-1.5">
      {rows.map((row) => {
        const category = byCode.get(row.category);
        const label = row.name?.trim() || category?.label || row.category;
        const sign = category?.kind === "DEDUCTION" ? "−" : "";
        const override = overrides[String(row.index)];
        const skipped = override?.skip === true;
        const changed = !skipped && override?.amount != null && override.amount !== row.amount;

        return (
          <span
            key={row.index}
            className={`${BADGE} border-border/70 bg-muted/40 text-muted-foreground`}
            title={skipped ? "Not paid this month" : changed ? "Changed for this month" : "Paid every month"}
          >
            {skipped ? (
              <>
                <span className="line-through">
                  {label} {sign}RM {rm(row.amount)}
                </span>
                <span className="ml-1">· not this month</span>
              </>
            ) : changed ? (
              <>
                {label} {sign}RM {rm(override.amount ?? row.amount)}
                <span className="ml-1">(usually RM {rm(row.amount)})</span>
              </>
            ) : (
              <>
                {label} {sign}RM {rm(row.amount)}
              </>
            )}
          </span>
        );
      })}
    </div>
  );
}

// What is set, in the fewest words that still distinguish two rows.
function summarise(adjustment: PayrollRunAdjustment): string[] {
  const chips: string[] = [];

  const ot =
    adjustment.otNormalHours +
    adjustment.otRestHours +
    adjustment.otPublicHours +
    Object.values(adjustment.otCustomHours ?? {}).reduce((sum, h) => sum + h, 0);
  if (ot > 0) chips.push(`${ot}h overtime`);

  if (adjustment.basicPayOverride != null) chips.push(`basic RM ${rm(adjustment.basicPayOverride)}`);

  if (adjustment.manualLineItems.length > 0) {
    const total = adjustment.manualLineItems.reduce((sum, item) => sum + item.amount, 0);
    chips.push(
      `${adjustment.manualLineItems.length} one-off line(s) · RM ${rm(total)}`,
    );
  }

  const overrides = Object.keys(adjustment.fixedAllowanceOverrides).length;
  if (overrides > 0) chips.push(`${overrides} allowance override(s)`);

  if (adjustment.workedHours !== null) chips.push("hours overridden");
  // 0 counts: it cancels the Leave module's unpaid days for this month.
  if (adjustment.unpaidLeaveDays !== null) {
    chips.push(`${adjustment.unpaidLeaveDays} unpaid day${adjustment.unpaidLeaveDays === 1 ? "" : "s"}`);
  }
  // 0 counts too: an entered levy replaces the computed one outright.
  if (adjustment.hrdfLevy !== null) chips.push(`HRDF levy RM ${rm(adjustment.hrdfLevy)}`);
  if (adjustment.notes) chips.push("note");

  // A saved row with nothing in it is possible — the admin typed something,
  // then took it back out without clearing. Say so rather than showing an
  // empty cell that reads as "nothing set".
  return chips.length > 0 ? chips : ["saved, but empty"];
}
