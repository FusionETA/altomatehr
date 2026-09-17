import { useEffect, useMemo, useState } from "react";
import { ChevronRight, CircleAlert, Plus, Upload, Users } from "lucide-react";
import { getEmployees, type Employee } from "@/features/employees/api";
import { getPolicies, type Policy } from "@/features/policies/api";
import { getPayrollEmployees } from "@/features/payroll/api";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonRows } from "@/shared/components/Skeleton";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { SearchInput } from "@/shared/components/SearchInput";
import { StatusFilterTabs } from "@/shared/components/StatusFilterTabs";
import {
  CLAIMS_PAGE_SIZE,
  PaginationControls,
} from "@/features/claims/components/PaginationControls";
import { AddEmployeeModal } from "./AddEmployeeModal";
import { ImportEmployeesDialog } from "./ImportEmployeesDialog";
import { EmployeeDetail } from "@/features/employees/components/EmployeeDetail";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const TH = "h-11 px-3 text-left text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground";

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
    const byUser = new Map<string, { archived: boolean; sections: string[] }>();
    for (const row of payrollQuery.data ?? []) {
      byUser.set(row.userId, {
        archived: row.isArchived,
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
    if (!row) return "No payroll profile yet";
    if (row.archived) return null;
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

  // Never leave the viewer stranded on a page that no longer exists after the
  // visible set narrows.
  useEffect(() => setPage(1), [searchTerm, roleFilter]);
  const totalPages = Math.max(1, Math.ceil(filtered.length / CLAIMS_PAGE_SIZE));
  const currentPage = Math.min(page, totalPages);
  const paged = filtered.slice(
    (currentPage - 1) * CLAIMS_PAGE_SIZE,
    currentPage * CLAIMS_PAGE_SIZE,
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
    <div className={`${CARD} space-y-5`}>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <div className="flex items-center gap-2">
            <h2 className="text-lg font-black text-foreground">Employees</h2>
            {/* Hidden until the roster is in. A count of 0 next to a table of
                skeleton rows reads as "this company has no employees", which is
                a different and alarming statement. */}
            {loading ? null : (
              <span className="inline-flex items-center gap-1 rounded-full bg-muted px-2.5 py-1 text-[11px] font-bold text-muted-foreground">
                <Users className="h-3 w-3" />
                {narrowed ? `${filtered.length} of ${staff.length}` : staff.length}
              </span>
            )}

            {/* The total, so "is anyone unpayable?" is answerable without
                scanning every row — the per-row icon then says who. */}
            {!loading && needsSetupCount > 0 ? (
              <span className="inline-flex items-center gap-1 rounded-full bg-warning px-2.5 py-1 text-[11px] font-bold text-warning-foreground">
                <CircleAlert className="h-3 w-3" />
                {needsSetupCount} need setup
              </span>
            ) : null}
          </div>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <SearchInput
            value={searchTerm}
            onChange={setSearchTerm}
            placeholder="Search employees"
            className="w-full sm:w-56"
            inputClassName="h-10 rounded-xl border-border/70 bg-card/90 focus-visible:ring-primary focus-visible:ring-offset-0"
          />
          {/* Beside Add employee, quieter than it: onboarding a batch is the
              rarer act, and the single-add button is what most visits want. */}
          <button
            type="button"
            onClick={() => setShowImport(true)}
            className="inline-flex h-10 shrink-0 items-center gap-1.5 rounded-xl border border-border/70 bg-card px-3.5 text-sm font-semibold text-foreground transition hover:bg-muted"
          >
            <Upload className="h-4 w-4" />
            Import
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

      <StatusFilterTabs<RoleFilter>
        value={roleFilter}
        onChange={setRoleFilter}
        statuses={ROLE_FILTERS}
        labels={{}}
        allLabel="All roles"
        ariaLabel="Role filters"
      />

      {loadError ? <p className="text-sm font-medium text-destructive">{loadError}</p> : null}

      <>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] text-sm">
              <thead>
                <tr className="border-b border-border/60">
                  <th className={TH}>Person</th>
                  <th className={TH}>Role</th>
                  <th className={TH}>Policy</th>
                  <th className={TH} />
                </tr>
              </thead>
              <tbody>
                {loading ? (
                  // Same four columns, same row height: the real rows replace
                  // these in place rather than pushing the page around.
                  <SkeletonRows
                    rows={5}
                    widths={["w-44", "w-20", "w-24", "w-4"]}
                  />
                ) : null}
                {paged.map((emp) => (
                  <tr
                    key={emp.id}
                    tabIndex={0}
                    onClick={() => setSelectedId(emp.id)}
                    onKeyDown={(event) => {
                      if (event.key === "Enter" || event.key === " ") {
                        event.preventDefault();
                        setSelectedId(emp.id);
                      }
                    }}
                    className="group cursor-pointer border-b border-border/60 transition-colors last:border-0 hover:bg-muted/70 focus-visible:bg-muted/70 focus-visible:outline-none"
                  >
                    <td className="px-3 py-3">
                      <div className="min-w-0">
                        <div className="flex min-w-0 items-center gap-1.5">
                          <p className="truncate font-semibold text-foreground">
                            {emp.name?.trim() || buildName(emp.email)}
                          </p>
                          <PayrollSetupFlag gap={setupGap(emp.id)} />
                        </div>
                        <p className="truncate text-xs text-muted-foreground">
                          {[emp.jobTitle, emp.employeeNumber].filter(Boolean).join(" · ") ||
                            emp.email}
                        </p>
                      </div>
                    </td>
                    <td className="px-3 py-3">
                      <span
                        className={`rounded-full px-2.5 py-1 text-[11px] font-bold ${
                          ROLE_PILL[emp.role] ?? "bg-muted text-muted-foreground"
                        }`}
                      >
                        {emp.role}
                      </span>
                    </td>
                    <td className="px-3 py-3 text-muted-foreground">{policyName(emp.policyId)}</td>
                    <td className="px-3 py-3 text-right">
                      <ChevronRight className="ml-auto h-4 w-4 text-muted-foreground transition-transform group-hover:translate-x-0.5 group-hover:text-foreground" />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            {!loading && filtered.length === 0 ? (
              <p className="py-8 text-center text-sm text-muted-foreground">
                {staff.length === 0
                  ? "No employees yet. Add the first one to get started."
                  : "No employees match these filters."}
              </p>
            ) : null}
          </div>

          <PaginationControls
            className="flex flex-col items-start justify-between gap-2 border-t border-border/60 pt-4 sm:flex-row sm:items-center"
            currentPage={currentPage}
            totalItems={filtered.length}
            itemNoun="employees"
            onPageChange={setPage}
          />
      </>

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

// The one-glance answer to "will payroll actually include this person?".
//
// Icon-only in the row: the list is scanned, not read, and a full "Needs setup"
// pill on every incomplete row drowns out the names. The tooltip names the
// sections so the admin knows which tab to open — which is the whole point of
// not having to open the profile to find out.
function PayrollSetupFlag({ gap }: { gap: string | null }) {
  if (!gap) return null;

  const label = `Not ready for payroll — ${gap}`;

  return (
    <span
      title={label}
      aria-label={label}
      role="img"
      className="inline-flex shrink-0 items-center rounded-full bg-warning/15 p-1 text-warning-foreground"
    >
      <CircleAlert className="h-3.5 w-3.5 text-warning-foreground" aria-hidden />
    </span>
  );
}
