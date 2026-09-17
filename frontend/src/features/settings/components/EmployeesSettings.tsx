import { useEffect, useMemo, useState } from "react";
import { ChevronRight, CircleAlert, CircleCheck, Plus, Upload, Users } from "lucide-react";
import { getEmployees, type Employee } from "@/features/employees/api";
import { getPolicies, type Policy } from "@/features/policies/api";
import { getPayrollEmployees } from "@/features/payroll/api";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonRows } from "@/shared/components/Skeleton";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { SearchInput } from "@/shared/components/SearchInput";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { PayrollBulkFillPanel } from "@/features/payroll/components/PayrollBulkFillPanel";
import {
  CLAIMS_PAGE_SIZE,
  PaginationControls,
} from "@/features/claims/components/PaginationControls";
import { AddEmployeeModal } from "./AddEmployeeModal";
import { ImportEmployeesDialog } from "./ImportEmployeesDialog";
import { EmployeeDetail } from "@/features/employees/components/EmployeeDetail";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";

// A card whose table runs edge to edge: the padding belongs to the header and
// the pagination bar, not to the rows between them. Border and background are
// left off deliberately — the two boxes set their own, and a colour declared
// here would fight theirs (Tailwind resolves a conflict by stylesheet order,
// not by which class was written last).
const BOX = "overflow-hidden rounded-[28px] shadow-ambient backdrop-blur-sm";
const BOX_HEADER = "px-5 pb-3 pt-5 sm:px-6";
const BOX_FOOT =
  "flex flex-col items-start justify-between gap-2 border-t border-border/60 px-5 py-3 sm:flex-row sm:items-center sm:px-6";
// The rows run edge to edge so a hover covers the whole box, but their
// CONTENT has to line up with the box's header and pagination bar — at a flat
// px-3 the names sat 8px to the left of the heading above them. So the outer
// two columns carry the box's own padding and the inner ones stay dense.
//
// Written as pl-/pr- beside a matching pr-/pl- rather than as `px-3 pl-6`: a
// px and a pl on one element is a conflict Tailwind settles by stylesheet
// order, and this file already has a note about losing that bet.
const TH_BASE =
  "h-11 text-left text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground";
const TH = `${TH_BASE} px-3`;
const TH_FIRST = `${TH_BASE} pl-5 pr-3 sm:pl-6`;
const TH_LAST = `${TH_BASE} pl-3 pr-5 sm:pr-6`;

const TD = "px-3 py-3";
const TD_FIRST = "py-3 pl-5 pr-3 sm:pl-6";
const TD_LAST = "py-3 pl-3 pr-5 text-right sm:pr-6";

const ALL = "ALL";
const ROLE_FILTERS = ["Employee", "Supervisor"] as const;
type RoleFilter = (typeof ROLE_FILTERS)[number] | typeof ALL;

// Supervisors read differently at a glance than staff, and that difference is
// what an admin scans this list for.
const ROLE_PILL: Record<string, string> = {
  Supervisor: "bg-warning text-warning-foreground",
};


export function EmployeesSettings() {
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [policies, setPolicies] = useState<Policy[]>([]);
  const [searchTerm, setSearchTerm] = useState("");
  const [roleFilter, setRoleFilter] = useState<RoleFilter>(ALL);
  const [page, setPage] = useState(1);
  const [readyPage, setReadyPage] = useState(1);
  const [showAdd, setShowAdd] = useState(false);
  const [showImport, setShowImport] = useState(false);
  // Which employee's full record is open. Null = the list.
  const [selectedId, setSelectedId] = useState<string | null>(null);

  // Served from cache on a revisit, so coming back to this screen shows the
  // roster immediately and refreshes it behind the list rather than blanking
  // it. Both paths are the cache keys apiGet uses.
  const employeesQuery = useCachedQuery("/employees", getEmployees);
  const policiesQuery = useCachedQuery("/policies", getPolicies);
  const loading = employeesQuery.loading || policiesQuery.loading;
  const loadError = employeesQuery.error ?? policiesQuery.error;

  useEffect(() => {
    if (employeesQuery.data) setEmployees(employeesQuery.data);
  }, [employeesQuery.data]);
  useEffect(() => {
    if (policiesQuery.data) setPolicies(policiesQuery.data);
  }, [policiesQuery.data]);

  // Employees and supervisors only — an admin is not an employee. They hold no
  // place in an approval chain, carry no payroll profile, and their access is
  // granted rather than employed, so this screen would offer them a form full
  // of fields that mean nothing for them. Mirrors the previous system, whose
  // Manage Employee query is scoped to EMPLOYEE + SUPERVISOR.
  const staff = useMemo(
    () => employees.filter((e) => e.role === "Employee" || e.role === "Supervisor"),
    [employees],
  );
  // Which profiles are short of what payroll needs to include them. Read from
  // the payroll roster rather than recomputed here: that endpoint already runs
  // the same PayrollProfileReadiness check a run uses, so this badge and the
  // one inside the profile cannot disagree.
  //
  // A failure degrades to no badges rather than an error — an admin managing
  // roles should not be blocked by the payroll roster being unavailable.
  // includeArchived, so that being ABSENT from this roster means exactly one
  // thing: no payroll profile exists yet. Without it an archived profile would
  // look identical to a missing one.
  const payrollQuery = useCachedQuery("/payroll/employees?all", () =>
    getPayrollEmployees(true),
  );

  const readiness = useMemo(() => {
    const byUser = new Map<string, { archived: boolean; hasProfile: boolean; sections: string[] }>();
    for (const row of payrollQuery.data ?? []) {
      byUser.set(row.userId, {
        archived: row.isArchived,
        hasProfile: row.hasPayrollProfile,
        sections: row.profileIncompleteSections,
      });
    }
    return byUser;
  }, [payrollQuery.data]);

  // What to warn about for one person, or null when there is nothing to say.
  //
  // Someone with NO payroll profile is the strongest case, not the absent one:
  // they cannot be paid at all, and before this they showed no icon simply by
  // virtue of missing from the roster — which read as "fine".
  //
  // Archived is deliberately not a warning. They are not being paid, but that
  // is a decision rather than an omission, and the profile shows "Archived"
  // rather than "Needs setup" for exactly the same reason.
  const setupGap = (userId: string): string | null => {
    // Until the roster lands, say nothing rather than flag everyone.
    if (payrollQuery.data === undefined) return null;

    const row = readiness.get(userId);
    // Absent from the payroll roster at all should no longer happen — it lists
    // every member now — but a stale cache could still miss someone.
    if (!row) return "No payroll details yet";
    if (row.archived) return null;
    if (!row.hasProfile) return "No payroll details yet";
    return row.sections.length > 0 ? `${row.sections.join(", ")} incomplete` : null;
  };

  // Counted over the whole roster, not the filtered page: "3 need setup" must
  // not change because someone typed in the search box.
  const needsSetupCount = staff.filter((e) => setupGap(e.id) !== null).length;

  const policyName = (id: string | null) =>
    id ? (policies.find((p) => p.id === id)?.name ?? "—") : "Default";

  const filtered = useMemo(() => {
    const query = searchTerm.trim().toLowerCase();
    return staff.filter((emp) => {
      if (roleFilter !== ALL && emp.role !== roleFilter) return false;
      if (!query) return true;
      return [
        emp.name,
        emp.email,
        emp.employeeNumber,
        emp.jobTitle,
        emp.role,
        emp.employeeNumber,
        emp.jobTitle,
        policyName(emp.policyId),
      ]
        .filter(Boolean)
        .join(" ")
        .toLowerCase()
        .includes(query);
    });
  }, [staff, policies, searchTerm, roleFilter]);

  // Two boxes, not two tabs: the people who cannot be paid yet are the whole
  // reason an admin opens this screen, so they sit at the top of the same page
  // as everyone else rather than behind a tab nobody clicks.
  const needsSetup = useMemo(
    () => filtered.filter((e) => setupGap(e.id) !== null),
    // setupGap closes over the payroll roster; `filtered` and that are the
    // only inputs that move.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [filtered, readiness, payrollQuery.data],
  );
  const ready = useMemo(
    () => filtered.filter((e) => setupGap(e.id) === null),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [filtered, readiness, payrollQuery.data],
  );

  // Paged apart, so clearing the backlog at the top does not shuffle the
  // list of people who are already fine.
  useEffect(() => {
    setPage(1);
    setReadyPage(1);
  }, [searchTerm, roleFilter]);

  const setupPages = Math.max(1, Math.ceil(needsSetup.length / CLAIMS_PAGE_SIZE));
  const setupCurrent = Math.min(page, setupPages);
  const setupSlice = needsSetup.slice(
    (setupCurrent - 1) * CLAIMS_PAGE_SIZE,
    setupCurrent * CLAIMS_PAGE_SIZE,
  );

  const readyPages = Math.max(1, Math.ceil(ready.length / CLAIMS_PAGE_SIZE));
  const readyCurrent = Math.min(readyPage, readyPages);
  const readySlice = ready.slice(
    (readyCurrent - 1) * CLAIMS_PAGE_SIZE,
    readyCurrent * CLAIMS_PAGE_SIZE,
  );

  // Distinct from "needs setup": these pass every readiness check and would
  // simply be paid nothing, which blocks a submission just as hard.
  const noSalary = (payrollQuery.data ?? []).filter(
    (row) =>
      row.notPayableReason === null &&
      (row.salaryType === "MONTHLY" ? row.monthlySalary : row.hourlyRate) === null,
  );

  const selected = employees.find((e) => e.id === selectedId) ?? null;
  const narrowed = filtered.length !== staff.length;

  // A row opens the full record, as in the previous system. The list stays a
  // list — every field is edited in one place rather than three of them being
  // editable inline and the rest hidden.
  if (selected) {
    return (
      <EmployeeDetail
        employee={selected}
        policies={policies}
        onBack={() => setSelectedId(null)}
        onSaved={(updated) =>
          setEmployees((cur) => cur.map((e) => (e.id === updated.id ? updated : e)))
        }
      />
    );
  }

  return (
    <div className="space-y-5">
      {/* Who you are looking at: the roster's size, its backlog, and the two
          controls that change it. Everything below is a consequence of this
          card, which is why it carries no rows of its own. */}
      <div className={`${CARD} space-y-5`}>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <div className="flex items-center gap-2">
              <h2 className="text-lg font-black text-foreground">Employees</h2>
              {/* Hidden until the roster is in. A count of 0 next to a table of
                  skeleton rows reads as "this company has no employees", which is
                  a different and alarming statement. */}
              {loading ? null : (
                <span className="inline-flex items-center gap-1 whitespace-nowrap rounded-full bg-muted px-2.5 py-1 text-[11px] font-bold text-muted-foreground">
                  <Users className="h-3 w-3" />
                  {narrowed ? `${filtered.length} of ${staff.length}` : staff.length}
                </span>
              )}

              {/* The total, so "is anyone unpayable?" is answerable without
                  scanning every row — the box below then says who. */}
              {!loading && needsSetupCount > 0 ? (
                <span className="inline-flex items-center gap-1 whitespace-nowrap rounded-full bg-warning px-2.5 py-1 text-[11px] font-bold text-warning-foreground">
                  <CircleAlert className="h-3 w-3" />
                  {needsSetupCount} need setup
                </span>
              ) : null}
            </div>
          </div>
          {/* Wraps rather than holding its width: three controls pinned in one
              row pushed Add employee off the card's right edge on a phone, and
              off the page entirely at 1024px. */}
          <div className="flex flex-wrap items-center justify-end gap-2">
            <SearchInput
              value={searchTerm}
              onChange={setSearchTerm}
              placeholder="Search employees"
              className="w-full sm:w-56"
              inputClassName="h-10 rounded-xl border-border/70 bg-card/90 focus-visible:ring-primary focus-visible:ring-offset-0"
            />

            {/* A filter, not a tab row. Three roles is not a navigation
                decision — it sits with the search box because it does the same
                job, and it leaves the card a row shorter. */}
            <Select
              value={roleFilter}
              onValueChange={(next) => setRoleFilter(next as RoleFilter)}
            >
              <SelectTrigger
                aria-label="Filter by role"
                className="h-10 w-full rounded-xl border-border/70 bg-card/90 text-sm sm:w-40"
              >
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ALL}>All roles</SelectItem>
                {ROLE_FILTERS.map((role) => (
                  <SelectItem key={role} value={role}>
                    {role}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            {/* Beside Add employee, quieter than it: onboarding a batch is the
                rarer act, and the single-add button is what most visits want.
                "People", because the panel below imports payroll details and
                two unqualified Imports on one screen is a coin toss. */}
            <button
              type="button"
              onClick={() => setShowImport(true)}
              className="inline-flex h-10 shrink-0 items-center gap-1.5 rounded-xl border border-border/70 bg-card px-3.5 text-sm font-semibold text-foreground transition hover:bg-muted"
            >
              <Upload className="h-4 w-4" />
              Import people
            </button>

            <button
              type="button"
              onClick={() => setShowAdd(true)}
              className="inline-flex h-10 shrink-0 items-center gap-1.5 rounded-xl bg-primary px-4 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:opacity-90"
            >
              <Plus className="h-4 w-4" />
              Add employee
            </button>
          </div>
        </div>

        {loadError ? <p className="text-sm font-medium text-destructive">{loadError}</p> : null}

        {/* Passes every readiness check and would still be paid nothing — a
            different problem from an incomplete profile, and one that blocks a
            submission just as hard, so it is said separately. */}
        {!loading && noSalary.length > 0 ? (
          <p className="rounded-2xl border border-warning/30 bg-warning/10 p-3 text-xs leading-snug text-foreground">
            <span className="font-bold">
              {noSalary.length} {noSalary.length === 1 ? "person has" : "people have"} no salary on
              file.
            </span>{" "}
            They generate a zero payslip, which blocks the run from being submitted:{" "}
            {noSalary.map((r) => r.name).join(", ")}.
          </p>
        ) : null}

        {/* The statutory round-trip, in the header card rather than a card of
            its own: it is the fastest way to empty the box below, so it
            belongs with the roster's other controls — and this page has
            enough boxes already. */}
        <PayrollBulkFillPanel onImported={() => void payrollQuery.refresh()} />
      </div>

      {loading ? (
        <div className={CARD}>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] text-sm">
              <tbody>
                <SkeletonRows rows={5} widths={["w-44", "w-20", "w-24", "w-4"]} />
              </tbody>
            </table>
          </div>
        </div>
      ) : filtered.length === 0 ? (
        <div className={CARD}>
          <p className="py-8 text-center text-sm text-muted-foreground">
            {staff.length === 0
              ? "No employees yet. Add the first one to get started."
              : "No employees match these filters."}
          </p>
        </div>
      ) : (
        <>
          {/* Two boxes, not two tabs and not two sections of one card: the
              people who cannot be paid yet are a different list with a
              different job, and giving the backlog its own walls is what
              makes "how much is left?" answerable at a glance. It comes
              first because it is what the admin came to clear. */}
          {/* `--warning` is ALREADY a pale amber (93% lightness), so the /5 and
              /10 this box started at were a tint of a tint and it read as
              white. The strip is the token at full strength — the same pairing
              as the warning chips — and the body a light wash of it. */}
          {needsSetup.length > 0 ? (
            <section
              className={`${BOX} border border-warning-foreground/30 bg-warning/30`}
            >
              <header className={`${BOX_HEADER} bg-warning`}>
                <h3 className="flex items-center gap-2 text-sm font-bold text-warning-foreground">
                  <CircleAlert className="size-4" aria-hidden />
                  {needsSetup.length}{" "}
                  {needsSetup.length === 1 ? "person needs" : "people need"} payroll setup
                </h3>
                <p className="mt-0.5 text-xs text-warning-foreground/80">
                  Missing statutory or compensation details. Open anyone to complete their profile,
                  or fill everyone in at once with the panel above.
                </p>
              </header>
              <EmployeeRows
                rows={setupSlice}
                policyName={policyName}
                setupGap={setupGap}
                onOpen={setSelectedId}
              />
              <PaginationControls
                className={BOX_FOOT}
                currentPage={setupCurrent}
                totalItems={needsSetup.length}
                itemNoun="people"
                onPageChange={setPage}
              />
            </section>
          ) : null}

          {ready.length > 0 ? (
            <section className={`${BOX} border border-border/70 bg-card/90`}>
              <header className={BOX_HEADER}>
                <h3 className="flex items-center gap-2 text-sm font-bold text-foreground">
                  <CircleCheck className="size-4 text-success" aria-hidden />
                  {ready.length} ready for payroll
                </h3>
                <p className="mt-0.5 text-xs text-muted-foreground">
                  Complete profiles — these are the people a run will include.
                </p>
              </header>
              <EmployeeRows
                rows={readySlice}
                policyName={policyName}
                setupGap={setupGap}
                onOpen={setSelectedId}
              />
              <PaginationControls
                className={BOX_FOOT}
                currentPage={readyCurrent}
                totalItems={ready.length}
                itemNoun="people"
                onPageChange={setReadyPage}
              />
            </section>
          ) : null}
        </>
      )}

      {/* Dialogs sit outside the list branch: their buttons live in the header
          card, so neither can be open while the list is still loading. */}
      {showAdd ? (
        <AddEmployeeModal
          policies={policies}
          onClose={() => setShowAdd(false)}
          onCreated={(created) => {
            setEmployees((cur) => [created, ...cur]);
            setShowAdd(false);
          }}
        />
      ) : null}

      {showImport ? (
        <ImportEmployeesDialog
          onClose={() => setShowImport(false)}
          // A bulk import can both create and update, so the list is
          // refetched rather than patched from the response.
          onImported={() => void employeesQuery.refresh()}
        />
      ) : null}
    </div>
  );
}

// One table body, used by both sections so a row reads identically whichever
// half it is in — only the Status cell differs, and it differs because the
// person does.
function EmployeeRows({
  rows,
  policyName,
  setupGap,
  onOpen,
}: {
  rows: Employee[];
  policyName: (id: string | null) => string;
  setupGap: (userId: string) => string | null;
  onOpen: (id: string) => void;
}) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[760px] text-sm">
        <thead>
          <tr className="border-y border-border/60">
            <th className={TH_FIRST}>Person</th>
            <th className={TH}>Role</th>
            <th className={TH}>Policy</th>
            <th className={TH}>Status</th>
            <th className={TH_LAST} />
          </tr>
        </thead>
        <tbody>
          {rows.map((emp) => {
            const gap = setupGap(emp.id);
            return (
              <tr
                key={emp.id}
                tabIndex={0}
                onClick={() => onOpen(emp.id)}
                onKeyDown={(event) => {
                  if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    onOpen(emp.id);
                  }
                }}
                className="group cursor-pointer border-b border-border/60 transition-colors last:border-0 hover:bg-muted/70 focus-visible:bg-muted/70 focus-visible:outline-none"
              >
                <td className={TD_FIRST}>
                  <div className="min-w-0">
                    <p className="truncate font-semibold text-foreground">
                      {emp.name?.trim() || buildName(emp.email)}
                    </p>
                    <p className="truncate text-xs text-muted-foreground">
                      {[emp.jobTitle, emp.employeeNumber].filter(Boolean).join(" · ") || emp.email}
                    </p>
                  </div>
                </td>
                <td className={TD}>
                  <span
                    className={`rounded-full px-2.5 py-1 text-[11px] font-bold ${
                      ROLE_PILL[emp.role] ?? "bg-muted text-muted-foreground"
                    }`}
                  >
                    {emp.role}
                  </span>
                </td>
                <td className={`${TD} text-muted-foreground`}>{policyName(emp.policyId)}</td>
                <td className={TD}>
                  {/* The row's own answer to "can this person be paid?", in
                      words rather than an icon — it is the column an admin is
                      scanning, so it should not need a hover to read. */}
                  {/* The warning chip is bordered like the Ready one beside
                      it: its row sits on an amber wash, so a fill alone no
                      longer separates it from its background. */}
                  {gap ? (
                    <span className="inline-flex items-center gap-1.5 rounded-full border border-warning-foreground/25 bg-warning px-2.5 py-1 text-[11px] font-bold text-warning-foreground">
                      <CircleAlert className="size-3" aria-hidden />
                      {gap}
                    </span>
                  ) : (
                    <span className="inline-flex items-center gap-1.5 rounded-full border border-success/30 bg-success/10 px-2.5 py-1 text-[11px] font-bold text-success">
                      <CircleCheck className="size-3" aria-hidden />
                      Ready
                    </span>
                  )}
                </td>
                <td className={TD_LAST}>
                  <ChevronRight className="ml-auto h-4 w-4 text-muted-foreground transition-transform group-hover:translate-x-0.5 group-hover:text-foreground" />
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
