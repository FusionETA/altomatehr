import { useCallback, useEffect, useMemo, useState } from "react";
import { ChevronLeft, ChevronRight, Download, MapPin } from "lucide-react";
import {
  getApprovalAudit,
  exportEmployeeAttendancePdf,
  getAttendanceHistory,
  getOrgHoursSummary,
  getSelfieStorage,
  getSupervisorPerformance,
  type AdminAttendanceFilter,
  type ApprovalAuditEntry,
  type AttendanceApprovalRequest,
  type AttendanceRecord,
  type OrgHoursSummary,
  type HoursBuckets,
  type SelfieStorage,
  type SupervisorPerformance,
} from "@/features/attendance/api";
import { AttendanceStatusBadge } from "@/features/attendance/components/AttendanceStatusBadge";
import { OvertimeStatusBadge } from "@/features/overtime/components/OvertimeStatusBadge";
import { getEmployees, type Employee } from "@/features/employees/api";
import { exportEmployeeLeaveSummaryPdf } from "@/features/leave/api";
import { getProjects } from "@/features/settings/api";
import { getTeams, type TeamMember } from "@/features/teams/api";
import { getAllOvertime, type OvertimeRequest } from "@/features/overtime/api";
import { getShifts, type Shift } from "@/features/shifts/api";
import { getOrganization, type Organization } from "@/features/settings/api";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { OverflowTabList } from "@/shared/components/OverflowTabList";
import { CARD_BARE } from "../lib/dashboard-styles";
import { formatBytes, formatMinutes, formatWorkingDays } from "../lib/attendance-format";
import {
  AttendanceFilterBar,
  DateRangeBar,
  type FilterOption,
} from "./AttendanceFilterBar";

// Two levels, mirroring production's own split:
//
//   Section  Overview | Employees | Overtime | Shifts   — separate surfaces
//   Report   Today | Analytics | Performance | History  — views of Overview
//
// Production puts the sections in a sidebar sub-nav; here they are horizontal
// tabs like the claims screen, so the sidebar stays one level deep.
type Section = "overview" | "employees" | "overtime" | "shifts";

type ReportTab = "today" | "analytics" | "performance" | "history";

// Teams flattened to what the admin view actually needs: a name to show, and a
// membership list to filter by. Kept here rather than holding whole Team objects
// with their approval config, which this screen never reads.
type TeamIndexEntry = {
  id: string;
  name: string;
  projectId: string;
  members: string[];
  // The same people with their approval layer, which is what "reports to" is
  // read from. Kept beside `members` rather than replacing it: the scope
  // filters only ever ask "is this person in?", and a set of ids answers that
  // without walking objects.
  roster: TeamMember[];
};

const REPORT_TABS: { key: ReportTab; label: string }[] = [
  { key: "today", label: "Today" },
  { key: "analytics", label: "Analytics" },
  { key: "performance", label: "Performance" },
  { key: "history", label: "History" },
];

// One row of the daily board: a person, and today's record if they have one.
export type TodayRow = {
  employee: Employee;
  record: AttendanceRecord | null;
};

export type TodayCounts = {
  all: number;
  onTime: number;
  late: number;
  onSite: number;
  offSite: number;
  noClockIn: number;
  onLeave: number;
};

type TodayChip = keyof TodayCounts;

// One employee card: the person, the sites they are attached to, their hours
// over the selected range, and today's record if they have one.
export type EmployeeRow = {
  employee: Employee;
  projects: string[];
  buckets: HoursBuckets | null;
  record: AttendanceRecord | null;
};

const TODAY_CHIPS: { key: TodayChip; label: string }[] = [
  { key: "all", label: "All" },
  { key: "onTime", label: "On time" },
  { key: "late", label: "Late" },
  { key: "onSite", label: "On-site" },
  { key: "offSite", label: "Off-site" },
  { key: "noClockIn", label: "No clock-in" },
  { key: "onLeave", label: "On leave" },
];

// The chip predicates and the counters have to agree, so both read from here.
function matchesChip(row: TodayRow, chip: TodayChip): boolean {
  const record = row.record;
  switch (chip) {
    case "all":
      return true;
    case "onTime":
      return Boolean(record?.timeIn) && !record?.lateByMin;
    case "late":
      return (record?.lateByMin ?? 0) > 0;
    case "onSite":
      return Boolean(record?.timeIn) && !isOffSite(record);
    case "offSite":
      return isOffSite(record);
    case "noClockIn":
      return !record?.timeIn && record?.status !== "ON_LEAVE";
    case "onLeave":
      return record?.status === "ON_LEAVE";
  }
}

const TH =
  "h-12 px-4 text-left text-xs font-bold uppercase tracking-[0.18em] text-muted-foreground first:pl-6 last:pr-6";

function isoDay(d: Date) {
  return d.toISOString().slice(0, 10);
}

function startOfMonth() {
  const d = new Date();
  d.setUTCDate(1);
  return isoDay(d);
}

// Beyond the geofence. 200m matches the threshold the clock-in path itself
// uses, so the board agrees with what the employee was told at the time.
const OFF_SITE_METERS = 200;

function isOffSite(record: AttendanceRecord | null | undefined): boolean {
  return (record?.clockInDistanceMeters ?? 0) > OFF_SITE_METERS;
}

function metreLabel(metres: number | null): string {
  if (metres === null) return "—";
  return metres >= 1000 ? `${(metres / 1000).toFixed(1)}KM` : `${Math.round(metres)}M`;
}

// Which ISO weekdays this person is expected in. Their shift wins; failing
// that the org's policy; failing that Mon-Fri, which is what the backend
// assumes too.
function workingDaysFor(shift: Shift | null, org: Organization | null): Set<number> {
  const csv = shift?.workingDays ?? org?.workingDays ?? "1,2,3,4,5";
  const days = csv
    .split(",")
    .map((part) => Number(part.trim()))
    .filter((n) => n >= 1 && n <= 7);
  return new Set(days.length > 0 ? days : [1, 2, 3, 4, 5]);
}

function timeLabel(iso: string | null): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleTimeString("en-US", {
    hour: "2-digit",
    minute: "2-digit",
    hour12: true,
  });
}

function dateLabel(key: string): string {
  const d = new Date(key);
  return Number.isNaN(d.getTime())
    ? key
    : d.toLocaleDateString("en-GB", { day: "2-digit", month: "short", year: "numeric" });
}

// Org-wide attendance, in the four questions an admin actually arrives with:
// who is in today, how many hours the org worked, whether approvals are being
// sat on, and what happened historically.
//
// The filter bar and the date range live here rather than in each tab, so
// switching tabs keeps you looking at the same slice of the org — a filter that
// resets on every tab change is a filter nobody trusts.
export function AdminAttendance() {
  const [section, setSection] = useState<Section>("overview");
  // Which employee the roster has drilled into, if any.
  const [openEmployeeId, setOpenEmployeeId] = useState<string | null>(null);
  const [tab, setTab] = useState<ReportTab>("today");
  const [filter, setFilter] = useState<AdminAttendanceFilter>({});
  const [from, setFrom] = useState(startOfMonth);
  const [to, setTo] = useState(() => isoDay(new Date()));

  const [projects, setProjects] = useState<FilterOption[]>([]);
  const [teams, setTeams] = useState<FilterOption[]>([]);
  const [emails, setEmails] = useState<Map<string, string>>(new Map());
  const [roster, setRoster] = useState<Employee[]>([]);
  const [teamIndex, setTeamIndex] = useState<TeamIndexEntry[]>([]);
  const [projectNames, setProjectNames] = useState<Map<string, string>>(new Map());

  const [records, setRecords] = useState<AttendanceRecord[]>([]);
  const [hours, setHours] = useState<OrgHoursSummary | null>(null);
  const [performance, setPerformance] = useState<SupervisorPerformance[]>([]);
  const [audit, setAudit] = useState<ApprovalAuditEntry[]>([]);
  const [selfies, setSelfies] = useState<SelfieStorage | null>(null);
  const [overtime, setOvertime] = useState<OvertimeRequest[]>([]);
  const [shifts, setShifts] = useState<Shift[]>([]);
  const [org, setOrg] = useState<Organization | null>(null);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // The labels every tab needs, fetched once. A missing list degrades to ids
  // rather than failing the page.
  useEffect(() => {
    Promise.all([
      getAttendanceHistory(),
      getEmployees().catch(() => []),
      getProjects().catch(() => []),
      getTeams().catch(() => []),
    ])
      .then(([recs, employees, projectList, teamList]) => {
        setRecords(recs);
        setEmails(new Map(employees.map((e) => [e.id, e.email])));
        setRoster(employees);
        setTeamIndex(
          teamList.map((t) => ({
            id: t.id,
            name: t.name,
            projectId: t.projectId,
            members: t.members.map((m) => m.employeeId),
            roster: t.members,
          })),
        );
        setProjectNames(new Map(projectList.map((p) => [p.id, p.name])));
        setProjects(projectList.filter((p) => !p.isArchived).map((p) => ({ id: p.id, name: p.name })));
        setTeams(teamList.map((t) => ({ id: t.id, name: t.name })));
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  // Each report is fetched for the tab that shows it, when its inputs change —
  // loading all four up front would make opening the page four org-wide
  // queries when the reader wanted one.
  const loadTab = useCallback(async () => {
    setError(null);
    try {
      // Employees loads overtime too — the drill-in shows a person's OT beside
      // their attendance, which is the pairing that explains a long day.
      if (section === "overtime" || section === "employees") setOvertime(await getAllOvertime());
      if (section === "shifts") setShifts(await getShifts());
      // The detail heatmap needs to tell "wasn't scheduled" from "didn't turn
      // up", which means knowing the employee's working days: their shift if
      // they have one, else the org's.
      if (section === "employees" && shifts.length === 0) {
        const [shiftList, organization] = await Promise.all([
          getShifts().catch(() => []),
          getOrganization().catch(() => null),
        ]);
        setShifts(shiftList);
        setOrg(organization);
      }
      // Employees shows each person's hours against the same range, so it reads
      // the same report Analytics does rather than a second source that could
      // total differently.
      if (section === "employees") setHours(await getOrgHoursSummary(from, to, filter.teamId));
      if (section !== "overview") return;

      if (tab === "analytics") setHours(await getOrgHoursSummary(from, to, filter.teamId));
      if (tab === "performance") {
        const [perf, selfie] = await Promise.all([
          getSupervisorPerformance(from, to, filter),
          getSelfieStorage().catch(() => null),
        ]);
        setPerformance(perf);
        setSelfies(selfie);
      }
      if (tab === "history") setAudit(await getApprovalAudit(from, to, filter));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not load that report.");
    }
    // shifts.length is read to avoid refetching, not to react to — a
    // dependency on it would refetch the moment it lands.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [section, tab, from, to, filter]);

  useEffect(() => {
    void loadTab();
  }, [loadTab]);

  // Wrapped so the memos below can depend on it honestly. It closes over
  // `emails`, and listing that instead only works for as long as nothing else
  // is added to this function.
  const names = useMemo(
    () =>
      new Map(
        roster
          .filter((e) => e.name.trim().length > 0)
          .map((e) => [e.id, e.name] as const),
      ),
    [roster],
  );

  // The person's real name where the org has one, falling back to the address
  // it used to be derived from. Every tab reads this, so a row on Overtime and
  // the same person on Employees cannot end up labelled differently.
  const name = useCallback(
    (employeeId: string) => {
      const stored = names.get(employeeId);
      if (stored) return stored;
      const email = emails.get(employeeId);
      return email ? buildName(email) : employeeId;
    },
    [names, emails],
  );

  // Today's rows come from the records already loaded — the day is a slice of
  // the same roll call, not a separate query.
  const today = isoDay(new Date());
  // Who the project/team filters admit. null = no filter at all, which is not
  // the same as an empty set — that one matched nobody.
  const scopedIds = useMemo(() => {
    if (!filter.projectId && !filter.teamId) return null;

    const scoped = new Set<string>();
    for (const team of teamIndex) {
      if (filter.teamId && team.id !== filter.teamId) continue;
      if (filter.projectId && team.projectId !== filter.projectId) continue;
      for (const employeeId of team.members) scoped.add(employeeId);
    }
    return scoped;
  }, [teamIndex, filter.projectId, filter.teamId]);

  // The day's board is the ROSTER joined onto today's records, not the records
  // alone. Whoever has not clocked in has no row to list, and they are the
  // people an admin opens this screen to find — listing only records would
  // silently answer "everyone is here".
  const todayRows = useMemo(() => {
    const term = (filter.q ?? "").trim().toLowerCase();
    const byEmployee = new Map(
      records.filter((r) => r.date === today).map((r) => [r.employeeId, r] as const),
    );

    return roster
      .filter((e) => !scopedIds || scopedIds.has(e.id))
      .filter((e) =>
        !term ? true : `${e.name} ${e.email}`.toLowerCase().includes(term),
      )
      .map((employee) => ({ employee, record: byEmployee.get(employee.id) ?? null }))
      // A project filter is about where someone worked, so it can only speak
      // for people who actually clocked somewhere. Applied to the absent it
      // would quietly drop them from every filtered view.
      .filter((row) =>
        !filter.projectId
          ? true
          : row.record
            ? row.record.projectId === filter.projectId
            : scopedIds?.has(row.employee.id) ?? false,
      )
      .sort((a, b) => {
        // Clocked-in first, newest clock first; the absent settle at the bottom
        // in name order rather than scattered through the list.
        const aIn = a.record?.timeIn ?? "";
        const bIn = b.record?.timeIn ?? "";
        if (aIn && bIn) return bIn.localeCompare(aIn);
        if (aIn) return -1;
        if (bIn) return 1;
        return a.employee.name.localeCompare(b.employee.name);
      });
  }, [records, roster, filter.q, filter.projectId, scopedIds, today]);

  // What the counters above the table each stand for. Derived from the same
  // rows the table renders, so a chip can never disagree with the list under it.
  const todayCounts = useMemo(
    () =>
      TODAY_CHIPS.reduce(
        (acc, chip) => ({
          ...acc,
          [chip.key]: todayRows.filter((row) => matchesChip(row, chip.key)).length,
        }),
        {} as TodayCounts,
      ),
    [todayRows],
  );

  const historyRows = useMemo(() => {
    const term = (filter.q ?? "").trim().toLowerCase();
    return records
      .filter((r) => r.date >= from && r.date <= to)
      .filter((r) => !filter.projectId || r.projectId === filter.projectId)
      .filter((r) =>
        !term
          ? true
          : `${name(r.employeeId)} ${emails.get(r.employeeId) ?? ""}`.toLowerCase().includes(term),
      )
      .sort((a, b) =>
        a.date === b.date
          ? (b.timeIn ?? "").localeCompare(a.timeIn ?? "")
          : b.date.localeCompare(a.date),
      );
  }, [records, filter, emails, from, to, name]);

  // ---- Employees / Overtime rows ----
  //
  // These two filter client-side against lists already in hand, which is only
  // safe because both are whole-org reads. The Overview reports filter on the
  // server instead; mixing the two would let a total and its table disagree.

  const employeeRows = useMemo(() => {
    const term = (filter.q ?? "").trim().toLowerCase();
    const todayByEmployee = new Map(
      records.filter((r) => r.date === today).map((r) => [r.employeeId, r] as const),
    );
    const hoursByEmployee = new Map(
      (hours?.employees ?? []).map((row) => [row.employeeId, row.buckets] as const),
    );

    // Where the person works: the projects their teams are on, plus anywhere
    // they have actually clocked in. Team membership alone understates it —
    // someone can be rostered to one site and spend the month on another, and
    // the row that says only the first is the misleading one.
    const clockedProjects = new Map<string, Set<string>>();
    for (const record of records) {
      if (!record.projectId) continue;
      const name = projectNames.get(record.projectId);
      if (!name) continue;
      const seen = clockedProjects.get(record.employeeId) ?? new Set<string>();
      seen.add(name);
      clockedProjects.set(record.employeeId, seen);
    }

    const projectsOf = (employeeId: string) =>
      [
        ...new Set([
          ...teamIndex
            .filter((team) => team.members.includes(employeeId))
            .map((team) => projectNames.get(team.projectId))
            .filter((name): name is string => Boolean(name)),
          ...(clockedProjects.get(employeeId) ?? []),
        ]),
      ];

    return roster
      .filter((e) => !scopedIds || scopedIds.has(e.id))
      .filter((e) =>
        !term
          ? true
          : `${e.name} ${e.email} ${e.employeeNumber ?? ""} ${e.jobTitle ?? ""}`
              .toLowerCase()
              .includes(term),
      )
      .map((employee) => ({
        employee,
        projects: projectsOf(employee.id),
        buckets: hoursByEmployee.get(employee.id) ?? null,
        record: todayByEmployee.get(employee.id) ?? null,
      }))
      .sort((a, b) => a.employee.name.localeCompare(b.employee.name));
  }, [roster, records, hours, teamIndex, projectNames, today, filter.q, scopedIds]);

  const overtimeRows = useMemo(() => {
    const term = (filter.q ?? "").trim().toLowerCase();
    return overtime
      .filter((r) => {
        const day = r.workDate.slice(0, 10);
        return day >= from && day <= to;
      })
      .filter((r) => !scopedIds || scopedIds.has(r.employeeId))
      .filter((r) => !filter.projectId || !r.projectId || r.projectId === filter.projectId)
      .filter((r) =>
        !term
          ? true
          : `${name(r.employeeId)} ${r.employeeEmail ?? ""} ${r.reason}`
              .toLowerCase()
              .includes(term),
      )
      .sort((a, b) => b.workDate.localeCompare(a.workDate));
  }, [overtime, from, to, filter.q, filter.projectId, scopedIds, name]);

  // Sits on the Overtime tab so a queue nobody is watching still says so.
  const pendingOvertime = useMemo(
    () => overtimeRows.filter((r) => r.status === "PENDING").length,
    [overtimeRows],
  );

  // "Reports to", rebuilt from the team hierarchy. The old membership column
  // that held this was dropped when approvals moved to Teams, so the answer now
  // lives where the routing itself reads it: the next occupied layer above the
  // person in their own team.
  const reportsTo = useCallback(
    (employeeId: string): string | null => {
      for (const team of teamIndex) {
        const self = team.roster.find((m) => m.employeeId === employeeId);
        if (!self) continue;

        const above = team.roster
          .filter((m) => m.layer > self.layer)
          .sort((a, b) => a.layer - b.layer);
        if (above.length === 0) continue;

        const nextLayer = above[0].layer;
        const approvers = above
          .filter((m) => m.layer === nextLayer)
          .map((m) => names.get(m.employeeId) ?? m.email ?? m.employeeId);
        return approvers.join(", ");
      }
      return null;
    },
    [teamIndex, names],
  );

  const openEmployee = useMemo(
    () => employeeRows.find((row) => row.employee.id === openEmployeeId) ?? null,
    [employeeRows, openEmployeeId],
  );

  // The drill-in stands alone: the filter bar scopes a roster, and there is
  // only one person on screen.
  const showFilters = section !== "shifts" && !openEmployee;
  const showDateRange =
    section === "overtime" ||
    section === "employees" ||
    (section === "overview" && tab !== "today");

  return (
    <div className="space-y-4 sm:space-y-6">
      {/* Production keeps these four in a sidebar sub-nav; here they are
          horizontal tabs, same as the claims screen. sm:flex-1 is load-bearing
          — see the note in AdminClaims. */}
      <OverflowTabList<Section>
        items={[
          { id: "overview", label: "Overview" },
          { id: "employees", label: "Employees" },
          { id: "overtime", label: "Overtime", badge: pendingOvertime || undefined },
          { id: "shifts", label: "Shifts" },
        ]}
        value={section}
        onChange={(next) => {
          setSection(next);
          setOpenEmployeeId(null);
        }}
        className="sm:max-w-lg sm:flex-1"
        ariaLabel="Attendance views"
      />

      {/* The reports inside Overview. Pills rather than a second tab bar, so
          the two levels stay visually distinct. */}
      {section === "overview" ? (
        <nav className="nice-scrollbar -mx-1 overflow-x-auto px-1">
          <div className="flex gap-2 pb-0.5">
            {REPORT_TABS.map((t) => (
              <button
                key={t.key}
                type="button"
                onClick={() => setTab(t.key)}
                className={`shrink-0 rounded-full border px-4 py-1.5 text-xs font-bold transition-colors ${
                  tab === t.key
                    ? "border-primary bg-primary text-primary-foreground"
                    : "border-border/60 bg-card text-muted-foreground hover:text-foreground"
                }`}
              >
                {t.label}
              </button>
            ))}
          </div>
        </nav>
      ) : null}

      {showFilters ? (
        <section className={`${CARD_BARE} space-y-3 p-5 sm:p-6`}>
          <AttendanceFilterBar
            value={filter}
            onChange={setFilter}
            projects={projects}
            teams={teams}
          />
          {/* Today reads one day by definition, so a range would be a control
              that changes nothing. Shifts are standing definitions, so neither
              a range nor an employee search applies to them at all. */}
          {showDateRange ? (
            <DateRangeBar
              from={from}
              to={to}
              onChange={(nextFrom, nextTo) => {
                setFrom(nextFrom);
                setTo(nextTo);
              }}
            />
          ) : null}
        </section>
      ) : null}

      {error ? (
        <section className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-4 text-sm font-medium text-destructive">
          {error}
        </section>
      ) : null}

      {loading ? (
        <section className={`${CARD_BARE} p-6 text-sm text-muted-foreground`}>
          Loading attendance…
        </section>
      ) : section === "employees" ? (
        openEmployee ? (
          <EmployeeDetail
            row={openEmployee}
            records={records.filter((r) => r.employeeId === openEmployee.employee.id)}
            overtime={overtime.filter((r) => r.employeeId === openEmployee.employee.id)}
            reportsTo={reportsTo(openEmployee.employee.id)}
            workingDays={workingDaysFor(
              shifts.find((sh) => sh.id === openEmployee.employee.shiftId) ?? null,
              org,
            )}
            projectNames={projectNames}
            today={today}
            from={from}
            to={to}
            onBack={() => setOpenEmployeeId(null)}
          />
        ) : (
          <EmployeesTab rows={employeeRows} onOpen={setOpenEmployeeId} />
        )
      ) : section === "overtime" ? (
        <OvertimeTab rows={overtimeRows} name={name} projectNames={projectNames} />
      ) : section === "shifts" ? (
        <ShiftsTab rows={shifts} projectNames={projectNames} />
      ) : tab === "today" ? (
        <TodayTab
          rows={todayRows}
          counts={todayCounts}
          projectNames={projectNames}
          date={today}
        />
      ) : tab === "analytics" ? (
        <AnalyticsTab summary={hours} name={name} />
      ) : tab === "performance" ? (
        <PerformanceTab rows={performance} selfies={selfies} />
      ) : (
        <HistoryTab
          rows={historyRows}
          audit={audit}
          name={name}
          projectNames={projectNames}
        />
      )}
    </div>
  );
}

function EmptyRow({ children }: { children: React.ReactNode }) {
  return (
    <div className="p-8 text-center text-sm text-muted-foreground">{children}</div>
  );
}

// The day's board: who is in, who is late, who is nowhere near the site, and
// who has not appeared at all. Modelled on the production daily activity view.
function TodayTab({
  rows,
  counts,
  projectNames,
  date,
}: {
  rows: TodayRow[];
  counts: TodayCounts;
  projectNames: Map<string, string>;
  date: string;
}) {
  const [chip, setChip] = useState<TodayChip>("all");

  const shown = useMemo(() => rows.filter((row) => matchesChip(row, chip)), [rows, chip]);

  return (
    <section className={`${CARD_BARE} overflow-hidden`}>
      <header className="flex flex-wrap items-baseline justify-between gap-2 px-5 pt-5 sm:px-6 sm:pt-6">
        <h3 className="text-lg font-bold text-foreground">Daily activity</h3>
        <span className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          {dateLabel(date)}
        </span>
      </header>

      {/* Counters double as filters, so a reader who spots "Late · 6" can see
          which six without rebuilding the thought in the search box. */}
      <nav className="nice-scrollbar overflow-x-auto px-5 py-4 sm:px-6">
        <div className="flex gap-2">
          {TODAY_CHIPS.map((item) => {
            const count = counts[item.key];
            const active = chip === item.key;
            return (
              <button
                key={item.key}
                type="button"
                onClick={() => setChip(item.key)}
                aria-pressed={active}
                // A zero count stays clickable rather than disabled: "Off-site
                // · 0" is a real answer, and a dead control makes it look
                // like the number failed to load.
                className={`shrink-0 rounded-full border px-3.5 py-1.5 text-xs font-bold transition-colors ${
                  active
                    ? "border-primary bg-primary text-primary-foreground"
                    : "border-border/60 bg-card text-muted-foreground hover:text-foreground"
                }`}
              >
                {item.label} · {count}
              </button>
            );
          })}
        </div>
      </nav>

      {shown.length === 0 ? (
        <EmptyRow>
          {rows.length === 0
            ? "Nobody on the roster matches these filters."
            : `No one is ${TODAY_CHIPS.find((c) => c.key === chip)?.label.toLowerCase()} today.`}
        </EmptyRow>
      ) : (
        <div className="overflow-x-auto border-t border-border/60">
          <table className="w-full min-w-[920px] text-sm">
            <thead>
              <tr className="border-b border-border/60">
                {["Employee", "Project / job", "Clock in", "Clock out", "Status"].map((h) => (
                  <th key={h} className={TH}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {shown.map(({ employee, record }) => {
                const offSite = isOffSite(record);
                return (
                  <tr key={employee.id} className="border-b border-border/60 last:border-0">
                    <td className="p-4 pl-6 align-top">
                      <p className="font-semibold text-foreground">{employee.name}</p>
                      {offSite ? (
                        <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-destructive">
                          Off-site · {metreLabel(record?.clockInDistanceMeters ?? null)}
                        </p>
                      ) : null}
                      {/* The employee's own words for why, which is the whole
                          value of an off-site flag — without it an admin is
                          left guessing at a red label. */}
                      {record?.remark ? (
                        <p className="text-xs text-muted-foreground">
                          <span className="font-semibold text-foreground">Reason:</span>{" "}
                          {record.remark}
                        </p>
                      ) : null}
                    </td>

                    <td className="max-w-[240px] p-4 align-top text-muted-foreground">
                      <span className="block truncate" title={jobLabel(record, projectNames, employee)}>
                        {jobLabel(record, projectNames, employee)}
                      </span>
                    </td>

                    <td className="p-4 align-top">
                      {record?.timeIn ? (
                        <>
                          <p className="font-semibold tabular-nums text-foreground">
                            {timeLabel(record.timeIn)}
                          </p>
                          {record.clockInLat !== null && record.clockInLng !== null ? (
                            <>
                              <p className="text-xs tabular-nums text-muted-foreground">
                                {record.clockInLat.toFixed(5)}, {record.clockInLng.toFixed(5)}
                              </p>
                              <a
                                href={`https://www.google.com/maps/search/?api=1&query=${record.clockInLat},${record.clockInLng}`}
                                target="_blank"
                                rel="noreferrer"
                                className="mt-1 inline-flex items-center gap-1 text-xs font-semibold text-primary hover:underline"
                              >
                                <MapPin className="h-3 w-3" aria-hidden />
                                Open in Maps
                              </a>
                            </>
                          ) : null}
                        </>
                      ) : (
                        <span className="text-muted-foreground">—</span>
                      )}
                    </td>

                    <td className="p-4 align-top">
                      {record?.timeOut ? (
                        <p className="font-semibold tabular-nums text-foreground">
                          {timeLabel(record.timeOut)}
                        </p>
                      ) : record?.timeIn ? (
                        <span className="italic text-muted-foreground">Still working</span>
                      ) : (
                        <span className="text-muted-foreground">—</span>
                      )}
                    </td>

                    <td className="p-4 pr-6 align-top">
                      {record ? (
                        <AttendanceStatusBadge status={record.status} />
                      ) : (
                        <span className="inline-flex rounded-full bg-muted px-3.5 py-1.5 text-[11px] font-bold uppercase tracking-[0.16em] text-muted-foreground">
                          No clock-in
                        </span>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

// What someone was booked to today: the site they clocked at, then their job
// title. An absent employee has no project, so their title is all there is.
function jobLabel(
  record: AttendanceRecord | null,
  projectNames: Map<string, string>,
  employee: Employee,
): string {
  const project = record?.projectId ? projectNames.get(record.projectId) : null;
  const parts = [project, employee.jobTitle].filter(Boolean);
  return parts.length > 0 ? parts.join(" · ") : "—";
}

function AnalyticsTab({
  summary,
  name,
}: {
  summary: OrgHoursSummary | null;
  name: (id: string) => string;
}) {
  if (!summary) {
    return (
      <section className={CARD_BARE}>
        <EmptyRow>Loading the hours summary…</EmptyRow>
      </section>
    );
  }

  const t = summary.totals;

  return (
    <div className="space-y-4 sm:space-y-6">
      <section className={`${CARD_BARE} p-5 sm:p-6`}>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <Stat label="Worked" value={formatMinutes(t.totalMin)} />
          <Stat label="Expected" value={formatMinutes(t.expectedMin)} />
          {/* Beyond-shift is separated from approved OT on purpose: hours past
              the shift that nobody approved are a liability, not overtime. */}
          <Stat label="Beyond shift" value={formatMinutes(t.beyondShiftMin)} />
          <Stat label="OT approved" value={formatMinutes(t.otApprovedMin)} />
        </div>
      </section>

      <section className={CARD_BARE}>
        {summary.employees.length === 0 ? (
          <EmptyRow>No hours recorded in this range.</EmptyRow>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[820px] text-sm">
              <thead>
                <tr className="border-b border-border/60">
                  {["Employee", "Worked", "Expected", "Break", "Beyond shift", "OT approved"].map(
                    (h) => (
                      <th key={h} className={TH}>{h}</th>
                    ),
                  )}
                </tr>
              </thead>
              <tbody>
                {summary.employees.map((row) => (
                  <tr key={row.employeeId} className="border-b border-border/60">
                    <td className="p-4 pl-6">
                      <p className="font-semibold text-foreground">{name(row.employeeId)}</p>
                      <p className="text-xs text-muted-foreground">{row.email ?? ""}</p>
                    </td>
                    <td className="p-4 tabular-nums">{formatMinutes(row.buckets.totalMin)}</td>
                    <td className="p-4 tabular-nums text-muted-foreground">
                      {formatMinutes(row.buckets.expectedMin)}
                    </td>
                    <td className="p-4 tabular-nums text-muted-foreground">
                      {formatMinutes(row.buckets.breakMin)}
                    </td>
                    <td className="p-4 tabular-nums">{formatMinutes(row.buckets.beyondShiftMin)}</td>
                    <td className="p-4 pr-6 tabular-nums">
                      {formatMinutes(row.buckets.otApprovedMin)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}

function PerformanceTab({
  rows,
  selfies,
}: {
  rows: SupervisorPerformance[];
  selfies: SelfieStorage | null;
}) {
  return (
    <div className="space-y-4 sm:space-y-6">
      <section className={CARD_BARE}>
        <div className="border-b border-border/60 px-6 py-4">
          <h3 className="text-sm font-black text-foreground">Supervisor response time</h3>
          <p className="mt-0.5 text-xs text-muted-foreground">
            How long each approver takes to decide. The slow count is measured against the
            organisation's SLA.
          </p>
        </div>

        {rows.length === 0 ? (
          <EmptyRow>Nothing was decided in this range.</EmptyRow>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] text-sm">
              <thead>
                <tr className="border-b border-border/60">
                  {["Supervisor", "Decisions", "Approved", "Rejected", "Slow", "Average", "Worst"].map(
                    (h) => (
                      <th key={h} className={TH}>{h}</th>
                    ),
                  )}
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.reviewerId} className="border-b border-border/60">
                    <td className="p-4 pl-6 font-semibold text-foreground">{r.reviewerName}</td>
                    <td className="p-4 tabular-nums">{r.totalDecisions}</td>
                    <td className="p-4 tabular-nums text-muted-foreground">{r.approvedCount}</td>
                    <td className="p-4 tabular-nums text-muted-foreground">{r.rejectedCount}</td>
                    <td className="p-4 tabular-nums">
                      <span className={r.slowDecisionCount > 0 ? "font-bold text-tertiary" : ""}>
                        {r.slowDecisionCount}
                      </span>
                    </td>
                    <td className="p-4 tabular-nums">{formatMinutes(r.avgDelayMinutes)}</td>
                    {/* The worst case earns a column because an average hides it. */}
                    <td className="p-4 pr-6 tabular-nums text-muted-foreground">
                      {formatMinutes(r.maxDelayMinutes)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className={`${CARD_BARE} p-5 sm:p-6`}>
        <h3 className="text-sm font-black text-foreground">Clock-in photo storage</h3>
        <div className="mt-3 grid gap-3 sm:grid-cols-3">
          <Stat label="Photos held" value={selfies ? String(selfies.photoCount) : "—"} />
          <Stat
            label="Storage used"
            value={selfies ? formatBytes(selfies.totalBytes) : "—"}
          />
          {/* Missing is worth its own tile: a pruned file is fine, but a rising
              count is what a broken upload path looks like. */}
          <Stat
            label="Missing files"
            value={selfies ? String(selfies.missingCount) : "—"}
            tone={selfies && selfies.missingCount > 0 ? "warn" : undefined}
          />
        </div>
      </section>
    </div>
  );
}

function HistoryTab({
  rows,
  audit,
  name,
  projectNames,
}: {
  rows: AttendanceRecord[];
  audit: ApprovalAuditEntry[];
  name: (id: string) => string;
  projectNames: Map<string, string>;
}) {
  return (
    <div className="space-y-4 sm:space-y-6">
      <section className={CARD_BARE}>
        <div className="border-b border-border/60 px-6 py-4">
          <h3 className="text-sm font-black text-foreground">Attendance records</h3>
        </div>
        {rows.length === 0 ? (
          <EmptyRow>No attendance in this range under these filters.</EmptyRow>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[820px] text-sm">
              <thead>
                <tr className="border-b border-border/60">
                  {["Date", "Employee", "Project", "Clock in", "Clock out", "Worked", "Status"].map(
                    (h) => (
                      <th key={h} className={TH}>{h}</th>
                    ),
                  )}
                </tr>
              </thead>
              <tbody>
                {rows.slice(0, 200).map((r) => (
                  <tr key={r.id} className="border-b border-border/60">
                    <td className="whitespace-nowrap p-4 pl-6 text-muted-foreground">
                      {dateLabel(r.date)}
                    </td>
                    <td className="p-4 font-semibold text-foreground">{name(r.employeeId)}</td>
                    <td className="p-4 text-muted-foreground">
                      {r.projectId ? projectNames.get(r.projectId) ?? "—" : "—"}
                    </td>
                    <td className="p-4 tabular-nums">{timeLabel(r.timeIn)}</td>
                    <td className="p-4 tabular-nums">{timeLabel(r.timeOut)}</td>
                    <td className="p-4 tabular-nums">{formatMinutes(r.durationMin)}</td>
                    <td className="p-4 pr-6"><AttendanceStatusBadge status={r.status} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className={CARD_BARE}>
        <div className="border-b border-border/60 px-6 py-4">
          <h3 className="text-sm font-black text-foreground">Approval trail</h3>
          <p className="mt-0.5 text-xs text-muted-foreground">
            Who decided what. Pending rows are included — a request nobody has touched is the
            more urgent half of the question.
          </p>
        </div>
        {audit.length === 0 ? (
          <EmptyRow>No approvals in this range.</EmptyRow>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[820px] text-sm">
              <thead>
                <tr className="border-b border-border/60">
                  {["Submitted", "Employee", "Event", "Status", "Decided by", "Took"].map((h) => (
                    <th key={h} className={TH}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {audit.slice(0, 200).map((a) => (
                  <tr key={a.id} className="border-b border-border/60">
                    <td className="whitespace-nowrap p-4 pl-6 text-muted-foreground">
                      {dateLabel(a.submittedAt.slice(0, 10))}
                    </td>
                    <td className="p-4 font-semibold text-foreground">{a.employeeName}</td>
                    <td className="p-4 text-muted-foreground">{a.kind.replace(/_/g, " ")}</td>
                    <td className="p-4"><ApprovalStatusBadge status={a.status} /></td>
                    <td className="p-4 text-muted-foreground">{a.reviewerName ?? "—"}</td>
                    <td className="p-4 pr-6 tabular-nums">{formatMinutes(a.delayMinutes)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}

// An approval's status, which is NOT a record's status — APPROVED / PENDING /
// REJECTED against an approval request, not PRESENT / LATE against a day. Reusing
// AttendanceStatusBadge here would have meant casting one enum to the other.
function ApprovalStatusBadge({ status }: { status: string }) {
  const tone =
    status === "APPROVED"
      ? "bg-secondary text-secondary-foreground"
      : status === "REJECTED"
        ? "bg-destructive/10 text-destructive"
        : "bg-warning text-warning-foreground";

  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-1 text-[10px] font-bold uppercase tracking-[0.14em] ${tone}`}
    >
      {status}
    </span>
  );
}

function Stat({
  label,
  value,
  tone,
}: {
  label: string;
  value: string;
  tone?: "warn";
}) {
  return (
    <div className="rounded-2xl border border-border/60 bg-surface-low px-4 py-3">
      <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-muted-foreground">
        {label}
      </p>
      <p
        className={`mt-1 text-xl font-black tabular-nums ${
          tone === "warn" ? "text-tertiary" : "text-foreground"
        }`}
      >
        {value}
      </p>
    </div>
  );
}


// ---- Employees ----

// The roster the way production presents it: one card per person, carrying the
// two things an admin scans for — how their hours are tracking against the
// range, and whether they turned up today.
//
// Each card opens that person's attendance detail, the way production's roster
// does.
function EmployeesTab({
  rows,
  onOpen,
}: {
  rows: EmployeeRow[];
  onOpen: (employeeId: string) => void;
}) {
  if (rows.length === 0) {
    return (
      <section className={CARD_BARE}>
        <EmptyRow>No employees match these filters.</EmptyRow>
      </section>
    );
  }

  return (
    <div className="space-y-4">
      <header>
        <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
          {rows.length} {rows.length === 1 ? "person" : "people"}
        </p>
        <h3 className="text-2xl font-bold text-foreground">Employees</h3>
      </header>

      <section className={`${CARD_BARE} divide-y divide-border/60`}>
        {rows.map(({ employee, projects, buckets, record }) => (
          <button
            key={employee.id}
            type="button"
            onClick={() => onOpen(employee.id)}
            className="flex w-full flex-col gap-3 p-5 text-left transition-colors hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary sm:flex-row sm:items-center sm:justify-between sm:gap-6 sm:p-6"
          >
            <div className="min-w-0 space-y-1">
              <div className="flex flex-wrap items-center gap-2">
                <h4 className="font-bold uppercase tracking-wide text-foreground">
                  {employee.name}
                </h4>
                <span className="inline-flex rounded-full border border-border/70 px-2.5 py-0.5 text-[10px] font-bold uppercase tracking-[0.16em] text-muted-foreground">
                  {employee.role}
                </span>
              </div>
              {/* Job title and sites on one line, truncated — the full list is
                  in the title attribute rather than wrapping a card to three
                  lines for the one person on six projects. */}
              <p className="truncate text-xs text-muted-foreground" title={detailLine(employee, projects)}>
                {detailLine(employee, projects)}
              </p>
            </div>

            <div className="flex shrink-0 items-center gap-6">
              <HoursMeter buckets={buckets} />
              <div className="w-24 text-right">
                <TodayPill record={record} />
              </div>
              <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden />
            </div>
          </button>
        ))}
      </section>
    </div>
  );
}

function detailLine(employee: Employee, projects: string[]): string {
  return [employee.jobTitle, ...projects].filter(Boolean).join(" • ") || "—";
}

// Hours worked against hours scheduled, as a number and a bar.
//
// The bar is capped at 100% so it cannot overflow its track, but the figures
// above it are not — someone on 52/48 should read as over, not as full.
function HoursMeter({ buckets }: { buckets: HoursBuckets | null }) {
  const worked = (buckets?.totalMin ?? 0) / 60;
  const expected = (buckets?.expectedMin ?? 0) / 60;
  const pct = expected > 0 ? Math.min(100, (worked / expected) * 100) : 0;

  return (
    <div className="w-32">
      <p className="text-[10px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
        Hours
      </p>
      <p className="text-sm font-bold tabular-nums text-foreground">
        {buckets ? `${round1(worked)}/${round1(expected)}` : "—"}
      </p>
      <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-muted">
        <div className="h-full rounded-full bg-primary" style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}

function round1(hours: number): string {
  return Number.isInteger(hours) ? String(hours) : hours.toFixed(1);
}

// Today at a glance. Deliberately quieter than the daily board's badge — here
// it is one column of many, not the subject of the screen.
function TodayPill({ record }: { record: AttendanceRecord | null }) {
  if (!record?.timeIn) {
    return <span className="text-xs text-muted-foreground">no clock-in</span>;
  }

  const late = (record.lateByMin ?? 0) > 0;
  return (
    <>
      <span
        className={`inline-flex rounded-full px-3 py-1 text-[10px] font-bold uppercase tracking-[0.16em] ${
          late ? "bg-warning text-warning-foreground" : "bg-secondary text-secondary-foreground"
        }`}
      >
        {late ? "Late" : "On time"}
      </span>
      <p className="mt-1 text-xs tabular-nums text-muted-foreground">{timeLabel(record.timeIn)}</p>
    </>
  );
}

// ---- Activity heatmap ----

// One cell per day for the last year, laid out like a contribution graph:
// columns are weeks, rows are Monday through Sunday.
//
// Colour encodes STATUS, not hours. The contribution-graph convention —
// darker means more — is exactly wrong for attendance: a run of eleven-hour
// days is the thing you want flagged, not the thing you want rewarded. Here
// the eye is drawn to the exception instead, which is what the page is for.
type DayCell = {
  date: string;
  kind: "onTime" | "late" | "absent" | "leave" | "offDuty" | "noData" | "future";
  record: AttendanceRecord | null;
};

const CELL_CLASS: Record<DayCell["kind"], string> = {
  onTime: "bg-secondary",
  late: "bg-warning",
  absent: "bg-destructive/70",
  leave: "bg-primary/60",
  // A day nobody was expected in reads as background, not as a gap in the
  // record — otherwise every weekend looks like an absence.
  offDuty: "bg-muted",
  // Before this employee has any record at all. Distinct from absent on
  // purpose: we have no evidence either way, and colouring it red would invent
  // months of absence out of a system that simply was not recording yet.
  noData: "bg-transparent ring-1 ring-inset ring-border/60",
  future: "bg-muted/40",
};

const LEGEND: { kind: DayCell["kind"]; label: string }[] = [
  { kind: "onTime", label: "On time" },
  { kind: "late", label: "Late" },
  { kind: "absent", label: "Absent" },
  { kind: "leave", label: "On leave" },
  { kind: "offDuty", label: "Not scheduled" },
  { kind: "noData", label: "No records" },
];

function AttendanceHeatmap({
  records,
  workingDays,
  today,
  weeks = 26,
}: {
  records: AttendanceRecord[];
  workingDays: Set<number>;
  today: string;
  weeks?: number;
}) {
  const { columns, summary } = useMemo(() => {
    const byDate = new Map(records.map((r) => [r.date, r] as const));

    // Nothing before the first record can be called an absence — there is no
    // evidence either way, and the employee may not have been here yet.
    const firstRecorded = records
      .map((r) => r.date)
      .sort()
      .at(0) ?? null;

    // Walk back to the Monday of the first week so the grid's rows line up
    // with weekdays rather than drifting by whatever day today happens to be.
    const end = new Date(`${today}T00:00:00Z`);
    const endMonday = new Date(end);
    const shift = (end.getUTCDay() + 6) % 7;           // Mon = 0
    endMonday.setUTCDate(end.getUTCDate() - shift);

    const start = new Date(endMonday);
    start.setUTCDate(endMonday.getUTCDate() - (weeks - 1) * 7);

    const cols: DayCell[][] = [];
    const tally = { onTime: 0, late: 0, absent: 0, leave: 0, offSite: 0, scheduled: 0 };

    for (let w = 0; w < weeks; w++) {
      const column: DayCell[] = [];
      for (let d = 0; d < 7; d++) {
        const day = new Date(start);
        day.setUTCDate(start.getUTCDate() + w * 7 + d);
        const iso = day.toISOString().slice(0, 10);
        const isoWeekday = ((day.getUTCDay() + 6) % 7) + 1;   // Mon = 1
        const record = byDate.get(iso) ?? null;

        let kind: DayCell["kind"];
        if (iso > today) kind = "future";
        else if (record?.status === "ON_LEAVE") kind = "leave";
        else if (record?.timeIn) kind = (record.lateByMin ?? 0) > 0 ? "late" : "onTime";
        else if (!workingDays.has(isoWeekday)) kind = "offDuty";
        else if (firstRecorded === null || iso < firstRecorded) kind = "noData";
        else kind = "absent";

        if (kind !== "future" && kind !== "offDuty" && kind !== "noData") tally.scheduled++;
        if (kind === "onTime") tally.onTime++;
        if (kind === "late") tally.late++;
        if (kind === "absent") tally.absent++;
        if (kind === "leave") tally.leave++;
        if (isOffSite(record)) tally.offSite++;

        column.push({ date: iso, kind, record });
      }
      cols.push(column);
    }

    return { columns: cols, summary: tally };
  }, [records, workingDays, today, weeks]);

  return (
    <section className={`${CARD_BARE} space-y-4 p-5 sm:p-6`}>
      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <h4 className="font-bold text-foreground">Activity</h4>
        <span className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          Last {weeks} weeks
        </span>
      </header>

      <div className="nice-scrollbar overflow-x-auto">
        <div className="flex gap-1">
          {columns.map((week) => (
            <div key={week[0].date} className="flex flex-col gap-1">
              {week.map((cell) => (
                <span
                  key={cell.date}
                  // A native title is enough here: the grid is a scanning aid,
                  // and the day-by-day list underneath carries the detail.
                  title={describeCell(cell)}
                  className={`h-3 w-3 rounded-[3px] ${CELL_CLASS[cell.kind]}`}
                />
              ))}
            </div>
          ))}
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        {LEGEND.map((item) => (
          <span key={item.kind} className="flex items-center gap-1.5 text-[11px] text-muted-foreground">
            <span className={`h-3 w-3 rounded-[3px] ${CELL_CLASS[item.kind]}`} />
            {item.label}
          </span>
        ))}
      </div>

      {/* The one-line read, so the grid does not have to be counted by eye. */}
      <p className="text-xs text-muted-foreground">
        Present {summary.onTime + summary.late} of {summary.scheduled} scheduled days
        {summary.late > 0 ? ` · ${summary.late} late` : ""}
        {summary.absent > 0 ? ` · ${summary.absent} absent` : ""}
        {summary.leave > 0 ? ` · ${summary.leave} on leave` : ""}
        {summary.offSite > 0 ? ` · ${summary.offSite} off-site` : ""}
      </p>
    </section>
  );
}

function describeCell(cell: DayCell): string {
  if (cell.kind === "future") return cell.date;
  if (cell.kind === "offDuty") return `${cell.date} · not scheduled`;
  if (cell.kind === "noData") return `${cell.date} · no records`;
  if (cell.kind === "absent") return `${cell.date} · no clock-in`;
  if (cell.kind === "leave") return `${cell.date} · on leave`;

  const parts = [cell.date, timeLabel(cell.record?.timeIn ?? null)];
  if (cell.record?.durationMin) parts.push(formatMinutes(cell.record.durationMin));
  if (cell.record?.lateByMin) parts.push(`late ${formatMinutes(cell.record.lateByMin)}`);
  if (isOffSite(cell.record)) parts.push(`off-site ${metreLabel(cell.record?.clockInDistanceMeters ?? null)}`);
  return parts.join(" · ");
}

// ---- One employee ----

const EVENT_LABEL: Record<AttendanceApprovalRequest["kind"], string> = {
  CLOCK_IN: "Clock in",
  CLOCK_OUT: "Clock out",
  BREAK_START: "Break start",
  BREAK_END: "Break end",
};

// One person's attendance in full: who they are, today, the month, the day-by-day
// history, and the overtime that explains the long days. Mirrors production's
// per-employee page.
//
// Every figure here is derived from records already loaded for the roster, so
// opening someone costs no extra request and cannot disagree with the list you
// came from.
function EmployeeDetail({
  row,
  records,
  overtime,
  reportsTo,
  workingDays,
  projectNames,
  today,
  from,
  to,
  onBack,
}: {
  row: EmployeeRow;
  records: AttendanceRecord[];
  overtime: OvertimeRequest[];
  reportsTo: string | null;
  workingDays: Set<number>;
  projectNames: Map<string, string>;
  today: string;
  from: string;
  to: string;
  onBack: () => void;
}) {
  const [exporting, setExporting] = useState<"attendance" | "leave" | null>(null);
  const [exportError, setExportError] = useState<string | null>(null);

  const runExport = async (which: "attendance" | "leave") => {
    setExporting(which);
    setExportError(null);
    try {
      if (which === "attendance") {
        await exportEmployeeAttendancePdf(row.employee.id, from, to);
      } else {
        await exportEmployeeLeaveSummaryPdf(row.employee.id);
      }
    } catch (e) {
      // Surfaced rather than swallowed: a download that silently does nothing
      // reads as a dead button.
      setExportError(e instanceof Error ? e.message : "Could not build that report.");
    } finally {
      setExporting(null);
    }
  };

  const { employee, projects, buckets } = row;
  const todayRecord = records.find((r) => r.date === today) ?? null;

  const history = useMemo(
    () => [...records].sort((a, b) => b.date.localeCompare(a.date)).slice(0, 30),
    [records],
  );

  // Counts over the month the roster is showing, so the tiles agree with the
  // hours figure beside them.
  const monthPrefix = today.slice(0, 7);
  const monthRecords = useMemo(
    () => records.filter((r) => r.date.startsWith(monthPrefix)),
    [records, monthPrefix],
  );

  const stats = {
    onTime: monthRecords.filter((r) => r.timeIn && !r.lateByMin).length,
    late: monthRecords.filter((r) => (r.lateByMin ?? 0) > 0).length,
    offSite: monthRecords.filter((r) => isOffSite(r)).length,
    onLeave: monthRecords.filter((r) => r.status === "ON_LEAVE").length,
    missing: monthRecords.filter((r) => !r.timeIn && r.status !== "ON_LEAVE").length,
  };

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <button
          type="button"
          onClick={onBack}
          className="inline-flex items-center gap-1 text-sm font-semibold text-primary hover:underline"
        >
          <ChevronLeft className="h-4 w-4" aria-hidden />
          All employees
        </button>

        <div className="flex flex-wrap gap-2">
          <ExportButton
            label="Export attendance PDF"
            busy={exporting === "attendance"}
            disabled={exporting !== null}
            onClick={() => void runExport("attendance")}
          />
          <ExportButton
            label="Export leave PDF"
            busy={exporting === "leave"}
            disabled={exporting !== null}
            onClick={() => void runExport("leave")}
          />
        </div>
      </div>

      {exportError ? (
        <p className="rounded-2xl border border-destructive/20 bg-destructive/5 p-3 text-sm font-medium text-destructive">
          {exportError}
        </p>
      ) : null}

      {/* Who */}
      <section className={`${CARD_BARE} p-5 sm:p-6`}>
        <div className="flex flex-wrap items-center gap-2">
          <h3 className="text-xl font-bold text-foreground">{employee.name}</h3>
          <span className="inline-flex rounded-full border border-border/70 px-2.5 py-0.5 text-[10px] font-bold uppercase tracking-[0.16em] text-muted-foreground">
            {employee.role}
          </span>
        </div>
        <p className="mt-0.5 text-xs text-muted-foreground">{employee.email}</p>
        <dl className="mt-3 grid gap-1 text-xs sm:grid-cols-2">
          <Fact label="Employee ID" value={employee.employeeNumber} />
          <Fact label="Title" value={employee.jobTitle} />
          <Fact label="Projects" value={projects.join(", ") || null} />
          <Fact label="Reports to" value={reportsTo} />
          <Fact label="Joined" value={employee.joinDate ? new Date(employee.joinDate).toLocaleDateString() : null} />
        </dl>
      </section>

      <div className="grid gap-4 lg:grid-cols-2">
        {/* Today, and the events behind it */}
        <section className={`${CARD_BARE} space-y-3 p-5 sm:p-6`}>
          <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
            Today
          </p>

          {todayRecord ? (
            <>
              <div className="flex flex-wrap items-center justify-between gap-2">
                <p className="text-sm font-bold tabular-nums text-foreground">
                  {timeLabel(todayRecord.timeIn)} – {timeLabel(todayRecord.timeOut)}
                </p>
                <AttendanceStatusBadge status={todayRecord.status} />
              </div>
              {todayRecord.projectId ? (
                <p className="text-xs text-muted-foreground">
                  {projectNames.get(todayRecord.projectId) ?? todayRecord.projectId}
                </p>
              ) : null}
              {isOffSite(todayRecord) ? (
                <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-destructive">
                  Off-site · {metreLabel(todayRecord.clockInDistanceMeters)}
                </p>
              ) : null}
              {todayRecord.lateByMin ? (
                <p className="text-xs text-muted-foreground">
                  Late by {formatMinutes(todayRecord.lateByMin)}
                </p>
              ) : null}
              {todayRecord.remark ? (
                <p className="rounded-2xl bg-muted/60 p-3 text-xs text-foreground">
                  <span className="font-semibold">Reason:</span> {todayRecord.remark}
                </p>
              ) : null}
            </>
          ) : (
            <p className="text-sm text-muted-foreground">No clock-in yet today.</p>
          )}

          {/* The audit trail: each clock event, when it happened, and whether
              anyone has signed it off. A record with an approved clock-in and a
              pending clock-out is the shape a query usually starts from. */}
          {(todayRecord?.approvals ?? []).length > 0 ? (
            <div className="border-t border-border/60 pt-3">
              <p className="mb-2 text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
                Events today
              </p>
              <ul className="space-y-1.5">
                {[...(todayRecord?.approvals ?? [])]
                  .sort((a, b) => a.eventAt.localeCompare(b.eventAt))
                  .map((event) => (
                    <li key={event.id} className="flex items-center gap-2 text-xs">
                      <span className="w-20 shrink-0 font-semibold text-foreground">
                        {EVENT_LABEL[event.kind]}
                      </span>
                      <span className="tabular-nums text-muted-foreground">
                        {timeLabel(event.eventAt)}
                      </span>
                      <span className="ml-auto">
                        <ApprovalStatusBadge status={event.approvalStatus} />
                      </span>
                    </li>
                  ))}
              </ul>
            </div>
          ) : null}
        </section>

        {/* The month */}
        <section className={`${CARD_BARE} p-5 sm:p-6`}>
          <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
            This month
          </p>
          <p className="mt-2 text-3xl font-bold tabular-nums text-foreground">
            {formatMinutes(buckets?.totalMin ?? 0)}
          </p>
          <p className="text-[11px] text-muted-foreground">
            Worked{buckets ? ` of ${formatMinutes(buckets.expectedMin)} scheduled` : ""}
          </p>
          <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3">
            <Stat label="On time" value={String(stats.onTime)} />
            <Stat label="Late" value={String(stats.late)} />
            <Stat label="Off-site" value={String(stats.offSite)} />
            <Stat label="Not clocked in" value={String(stats.missing)} />
            <Stat label="On leave" value={String(stats.onLeave)} />
          </div>
        </section>
      </div>

      {/* The shape of the year, above the day-by-day detail that explains it */}
      <AttendanceHeatmap records={records} workingDays={workingDays} today={today} />

      {/* Day by day */}
      <section className={`${CARD_BARE} overflow-hidden`}>
        <header className="flex items-baseline justify-between gap-2 px-5 pt-5 sm:px-6 sm:pt-6">
          <h4 className="font-bold text-foreground">Recent attendance</h4>
          <span className="text-xs text-muted-foreground">
            {history.length} {history.length === 1 ? "day" : "days"}
          </span>
        </header>

        {history.length === 0 ? (
          <EmptyRow>No attendance recorded for this employee.</EmptyRow>
        ) : (
          <ul className="mt-4 divide-y divide-border/60 border-t border-border/60">
            {history.map((record) => (
              <li key={record.id} className="flex flex-wrap items-start justify-between gap-3 p-4 px-6">
                <div className="min-w-0 space-y-0.5">
                  <p className="text-sm font-bold tabular-nums text-foreground">{record.date}</p>
                  <p className="text-xs text-muted-foreground">
                    {record.timeIn ? (
                      <span className="tabular-nums">
                        {timeLabel(record.timeIn)} – {timeLabel(record.timeOut)}
                      </span>
                    ) : (
                      "No clock-in"
                    )}
                    {record.projectId ? ` • ${projectNames.get(record.projectId) ?? record.projectId}` : ""}
                    {record.durationMin ? ` • ${formatMinutes(record.durationMin)}` : ""}
                  </p>

                  {record.clockInLat !== null && record.clockInLng !== null ? (
                    <p className="flex flex-wrap items-center gap-2 text-xs tabular-nums text-muted-foreground">
                      {record.clockInLat.toFixed(5)}, {record.clockInLng.toFixed(5)}
                      <a
                        href={`https://www.google.com/maps/search/?api=1&query=${record.clockInLat},${record.clockInLng}`}
                        target="_blank"
                        rel="noreferrer"
                        className="inline-flex items-center gap-1 font-semibold text-primary hover:underline"
                      >
                        <MapPin className="h-3 w-3" aria-hidden />
                        Clock-in map
                      </a>
                    </p>
                  ) : null}

                  {isOffSite(record) ? (
                    <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-destructive">
                      Off-site · {metreLabel(record.clockInDistanceMeters)}
                    </p>
                  ) : null}
                  {record.remark ? (
                    <p className="text-xs text-muted-foreground">
                      <span className="font-semibold text-foreground">Reason:</span> {record.remark}
                    </p>
                  ) : null}
                </div>

                <div className="flex shrink-0 items-center gap-2">
                  <AttendanceStatusBadge status={record.status} />
                  <ApprovalStatusBadge status={record.approvalStatus} />
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>

      {/* Overtime, beside the attendance it belongs to */}
      <section className={`${CARD_BARE} overflow-hidden`}>
        <header className="flex items-baseline justify-between gap-2 px-5 pt-5 sm:px-6 sm:pt-6">
          <h4 className="font-bold text-foreground">Overtime</h4>
          <span className="text-xs text-muted-foreground">
            {overtime.length} {overtime.length === 1 ? "entry" : "entries"}
          </span>
        </header>

        {overtime.length === 0 ? (
          <EmptyRow>No overtime entries.</EmptyRow>
        ) : (
          <ul className="mt-4 divide-y divide-border/60 border-t border-border/60">
            {[...overtime]
              .sort((a, b) => b.workDate.localeCompare(a.workDate))
              .map((request) => (
                <li key={request.id} className="flex items-start justify-between gap-4 p-4 px-6">
                  <div className="min-w-0">
                    <p className="text-sm font-semibold tabular-nums text-foreground">
                      {dateLabel(request.workDate.slice(0, 10))} · {formatMinutes(request.requestedMinutes)}
                    </p>
                    <p className="truncate text-xs text-muted-foreground" title={request.reason}>
                      {request.reason}
                    </p>
                  </div>
                  <OvertimeStatusBadge status={request.status} />
                </li>
              ))}
          </ul>
        )}
      </section>
    </div>
  );
}

function ExportButton({
  label,
  busy,
  disabled,
  onClick,
}: {
  label: string;
  busy: boolean;
  disabled: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className="inline-flex items-center gap-1.5 rounded-2xl border border-border/70 bg-card px-3.5 py-2 text-xs font-bold text-foreground shadow-sm transition-colors hover:bg-muted disabled:opacity-60"
    >
      <Download className="h-3.5 w-3.5" aria-hidden />
      {busy ? "Building…" : label}
    </button>
  );
}

// A labelled fact, dropped entirely when there is nothing to say — an empty
// dash next to "Employee ID" tells the reader less than the absence does.
function Fact({ label, value }: { label: string; value: string | null | undefined }) {
  if (!value) return null;
  return (
    <div>
      <dt className="inline text-muted-foreground">{label}: </dt>
      <dd className="inline font-semibold text-foreground">{value}</dd>
    </div>
  );
}

// ---- Overtime ----

// Org-wide overtime, read-only. Deciding happens in the approvals queue, where
// the approver and the routing rules are — an admin approving from here would
// bypass the chain entirely.
function OvertimeTab({
  rows,
  name,
  projectNames,
}: {
  rows: OvertimeRequest[];
  name: (id: string) => string;
  projectNames: Map<string, string>;
}) {
  if (rows.length === 0) {
    return (
      <section className={CARD_BARE}>
        <EmptyRow>No overtime requests in this range.</EmptyRow>
      </section>
    );
  }

  return (
    <section className={CARD_BARE}>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[900px] text-sm">
          <thead>
            <tr className="border-b border-border/60">
              {["Work date", "Employee", "Project", "Hours", "Reason", "Status"].map((h) => (
                <th key={h} className={TH}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} className="border-b border-border/60">
                <td className="p-4 pl-6 tabular-nums">{dateLabel(r.workDate.slice(0, 10))}</td>
                <td className="p-4">
                  <p className="font-semibold text-foreground">{name(r.employeeId)}</p>
                  {r.employeeEmail ? (
                    <p className="text-xs text-muted-foreground">{r.employeeEmail}</p>
                  ) : null}
                </td>
                <td className="p-4 text-muted-foreground">
                  {r.projectId ? projectNames.get(r.projectId) ?? "—" : "—"}
                </td>
                <td className="p-4 tabular-nums">{formatMinutes(r.requestedMinutes)}</td>
                {/* Reasons run long, so the cell truncates and keeps the full
                    text in the title rather than widening the whole table. */}
                <td className="max-w-[260px] p-4 text-muted-foreground">
                  <span className="block truncate" title={r.reason}>{r.reason}</span>
                </td>
                <td className="p-4 pr-6"><OvertimeStatusBadge status={r.status} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

// ---- Shifts ----

// The working patterns everything else on this screen is measured against: a
// late clock-in is only late relative to one of these, so it belongs next to
// the reports that use it.
function ShiftsTab({
  rows,
  projectNames,
}: {
  rows: Shift[];
  projectNames: Map<string, string>;
}) {
  if (rows.length === 0) {
    return (
      <section className={CARD_BARE}>
        <EmptyRow>
          No shifts defined yet. Until one exists, attendance has no expected
          hours to compare against.
        </EmptyRow>
      </section>
    );
  }

  return (
    <section className={CARD_BARE}>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[820px] text-sm">
          <thead>
            <tr className="border-b border-border/60">
              {["Shift", "Project", "Hours", "Working days", "Unpaid break"].map((h) => (
                <th key={h} className={TH}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((shift) => (
              <tr key={shift.id} className="border-b border-border/60">
                <td className="p-4 pl-6">
                  <span className="font-semibold text-foreground">{shift.name}</span>
                  {shift.isDefault ? (
                    <span className="ml-2 inline-flex rounded-full bg-secondary px-2.5 py-0.5 text-[10px] font-bold uppercase tracking-[0.16em] text-secondary-foreground">
                      Default
                    </span>
                  ) : null}
                </td>
                <td className="p-4 text-muted-foreground">
                  {projectNames.get(shift.projectId) ?? "—"}
                </td>
                <td className="p-4 tabular-nums">
                  {shift.startTime} – {shift.endTime}
                </td>
                <td className="p-4 text-muted-foreground">
                  {formatWorkingDays(shift.workingDays)}
                </td>
                <td className="p-4 pr-6 tabular-nums">
                  {formatMinutes(shift.lunchBreakMinutes)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
