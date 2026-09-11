import { useCallback, useEffect, useState } from "react";
import { TriangleAlert } from "lucide-react";
import { getPayrollEmployees, type PayrollEmployee } from "../api";
import { rm, shortDate } from "../lib/payroll-format";
import {
  BADGE,
  CARD,
  ERROR_PANEL,
  HINT,
  TD,
  TD_NUM,
  TH,
  TH_NUM,
  WARN_PANEL,
} from "../lib/ui";
import { CheckBox } from "./PayrollCheckbox";
import { PayrollBulkFillPanel } from "./PayrollBulkFillPanel";
import { TableSkeleton } from "./TableSkeleton";

// Who gets paid, and whether their details are complete enough to file.
//
// This is the same check a run's readiness guard runs, brought forward: an
// admin should clear a missing IC in the quiet week, not on the afternoon the
// submission is refused. Nothing here is editable — a salary is changed on
// the employee's own profile, or in bulk through the panel below, so there is
// only ever one place a figure comes from.
export function PayrollEmployeesTab() {
  const [rows, setRows] = useState<PayrollEmployee[]>([]);
  const [includeArchived, setIncludeArchived] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);

    return getPayrollEmployees(includeArchived)
      .then(setRows)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, [includeArchived]);

  useEffect(() => {
    void load();
  }, [load]);

  const incomplete = rows.filter((row) => row.missing.length > 0);
  const noSalary = rows.filter(
    (row) =>
      row.notPayableReason === null &&
      (row.salaryType === "MONTHLY" ? row.monthlySalary : row.hourlyRate) === null,
  );

  return (
    <div className="space-y-6">
      {error ? <section className={ERROR_PANEL}>Error: {error}</section> : null}

      {/* What would stop a filing, before a run exists to be stopped. */}
      {incomplete.length > 0 ? (
        <div className={WARN_PANEL}>
          <p className="flex items-center gap-2 font-semibold">
            <TriangleAlert className="size-4" aria-hidden />
            {incomplete.length} of {rows.length} employee(s) are missing something a statutory
            file needs
          </p>
          <p className="mt-1">
            A run will still generate their payslips, but it cannot be submitted until these are
            filled. Fix them on the employee's profile, or in bulk below.
          </p>
        </div>
      ) : null}

      {/* Different problem, different sentence: they are filed correctly and
          would simply be paid nothing. */}
      {noSalary.length > 0 ? (
        <div className={WARN_PANEL}>
          <p className="font-semibold">
            {noSalary.length} employee(s) have no salary on file
          </p>
          <p className="mt-1">
            They generate a zero payslip, which blocks the run from being submitted:{" "}
            <strong>{noSalary.map((row) => row.name).join(", ")}</strong>.
          </p>
        </div>
      ) : null}

      <section className={`${CARD} p-0 sm:p-0`}>
        <header className="flex flex-wrap items-center justify-between gap-3 px-5 pt-5 sm:px-6">
          <div>
            <h2 className="text-base font-semibold text-foreground">Payroll details</h2>
            <p className={HINT}>
              {loading
                ? "Loading…"
                : `${rows.length} employee(s). Read-only — edited on each profile or in bulk below.`}
            </p>
          </div>

          <label className="flex items-center gap-2 text-sm text-muted-foreground">
            <CheckBox
              checked={includeArchived}
              onChange={setIncludeArchived}
              ariaLabel="Show archived employees"
            />
            Show archived
          </label>
        </header>

        {loading ? (
          <TableSkeleton columns={9} label="Loading the payroll roster" />
        ) : rows.length === 0 ? (
          <p className="px-5 py-6 text-sm text-muted-foreground sm:px-6">
            No employees yet.
          </p>
        ) : (
          <div className="mt-4 overflow-x-auto">
            <table className="w-full min-w-[1020px] border-collapse">
              <thead>
                <tr className="border-b border-border/70">
                  <th className={TH}>Employee</th>
                  <th className={TH}>Department</th>
                  <th className={TH_NUM}>Salary</th>
                  <th className={TH}>EPF no.</th>
                  <th className={TH}>SOCSO no.</th>
                  <th className={TH}>Tax no.</th>
                  <th className={TH}>Bank</th>
                  <th className={TH}>Joined</th>
                  <th className={TH}>Status</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr
                    key={row.employeeProfileId}
                    className="border-b border-border/40 last:border-0"
                  >
                    <td className={TD}>
                      <div className="font-medium text-foreground">{row.name}</div>
                      <div className="text-xs text-muted-foreground">
                        {row.employeeNumber ?? "No employee number"}
                      </div>
                    </td>
                    <td className={`${TD} text-muted-foreground`}>{row.department ?? "—"}</td>
                    <td className={TD_NUM}>
                      <Salary row={row} />
                    </td>
                    <td className={TD}>
                      <Value
                        value={row.epfNumber}
                        // An exempt employee has no EPF number to be missing,
                        // so an empty cell there is correct rather than a gap.
                        absent={row.contributeToEpf ? "Missing" : "Exempt"}
                        muted={!row.contributeToEpf}
                      />
                    </td>
                    <td className={TD}>
                      <Value value={row.socsoNumber} absent="Missing" />
                    </td>
                    <td className={TD}>
                      {/* Deliberately not a gap: PCB computes without a TIN,
                          and a new joiner waiting on one must not hold up
                          everyone else's pay. */}
                      <Value value={row.incomeTaxNumber} absent="Not issued" muted />
                    </td>
                    <td className={TD}>
                      {row.bankName ? (
                        <>
                          <div className="text-foreground">{row.bankName}</div>
                          <div className="text-xs text-muted-foreground">
                            {row.bankAccountNumber ?? "No account number"}
                          </div>
                        </>
                      ) : (
                        <span className="text-muted-foreground">—</span>
                      )}
                    </td>
                    <td className={`${TD} text-muted-foreground`}>{shortDate(row.joinDate)}</td>
                    <td className={TD}>
                      <Status row={row} />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <PayrollBulkFillPanel onImported={() => void load()} />
    </div>
  );
}

// An hourly employee's rate is not comparable to a monthly salary, so the
// unit is shown rather than left for the reader to assume.
function Salary({ row }: { row: PayrollEmployee }) {
  const amount = row.salaryType === "MONTHLY" ? row.monthlySalary : row.hourlyRate;

  if (amount === null) {
    return <span className="font-medium text-destructive">Not set</span>;
  }

  return (
    <>
      {rm(amount)}
      <span className="ml-1 text-xs text-muted-foreground">
        {row.salaryType === "MONTHLY" ? "/mo" : "/hr"}
      </span>
    </>
  );
}

function Value({
  value,
  absent,
  muted,
}: {
  value: string | null;
  absent: string;
  muted?: boolean;
}) {
  if (value) return <span className="text-foreground">{value}</span>;

  return (
    <span className={muted ? "text-xs text-muted-foreground" : "text-xs font-medium text-amber-700 dark:text-amber-400"}>
      {absent}
    </span>
  );
}

function Status({ row }: { row: PayrollEmployee }) {
  if (row.notPayableReason) {
    return (
      <span className={`${BADGE} border-border bg-muted/60 text-muted-foreground`}>
        {row.notPayableReason}
      </span>
    );
  }

  if (row.missing.length > 0) {
    return (
      <span
        className={`${BADGE} border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-400`}
        title={row.missing.join(" · ")}
      >
        <TriangleAlert className="size-3" aria-hidden />
        {row.missing.join(", ")}
      </span>
    );
  }

  return (
    <span className={`${BADGE} border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-400`}>
      Ready
    </span>
  );
}
