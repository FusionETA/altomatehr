import { Fragment, useCallback, useEffect, useMemo, useState } from "react";
import {
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  Download,
  FileText,
  MapPin,
  Plus,
} from "lucide-react";
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
import {
  SessionsExpander,
  SessionsToggle,
} from "@/features/attendance/components/SessionsExpander";
import { AttendancePhotoButton } from "@/features/attendance/components/AttendancePhotoButton";
import { displayStatus } from "@/features/attendance/lib/attendance-status";
import { dayOffsetLabel } from "@/features/attendance/lib/attendance-time";
import { OvertimeStatusBadge } from "@/features/overtime/components/OvertimeStatusBadge";
import { getEmployees, type Employee } from "@/features/employees/api";
import { exportEmployeeLeaveSummaryPdf } from "@/features/leave/api";
import { getProjects } from "@/features/settings/api";
import { getTeams, type TeamMember } from "@/features/teams/api";
import {
  getAllOvertime,
  openOvertimePhoto,
  type OvertimeRequest,
} from "@/features/overtime/api";
import { overtimeStatusLabels } from "@/features/overtime/lib/overtime-status";
import { getShifts, type Shift } from "@/features/shifts/api";
import { getOrganization, type Organization } from "@/features/settings/api";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { OverflowTabList } from "@/shared/components/OverflowTabList";
import { TablePager } from "@/shared/components/TablePager";
import { usePaged } from "@/shared/lib/use-paged";
import { CARD_BARE } from "../lib/dashboard-styles";
import {
  ALL_FILTER,
  formatBytes,
  formatMinutes,
  formatWorkingDays,
} from "../lib/attendance-format";
import { ShiftEditor } from "./ShiftEditor";
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

// A shift still in progress has no clock-out, and rendering that as
// "08:42 AM – —" reads as missing data rather than as someone still at work.
// A night shift gets its "+1d", without which the pair reads backwards.
function spanLabel(timeIn: string | null, timeOut: string | null): string {
  if (!timeIn) return "No clock-in";
  if (!timeOut) return `${timeLabel(timeIn)} – still in`;
  const offset = dayOffsetLabel(timeIn, timeOut);
  return `${timeLabel(timeIn)} – ${timeLabel(timeOut)}${offset ? ` ${offset}` : ""}`;
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
  // Site address per project NAME, because that is what the employee rows
  // carry — they are built from team membership and clocked-in projects, both
  // of which resolve to names before they reach a row.
  const [projectSites, setProjectSites] = useState<Map<string, string | null>>(new Map());

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
        setProjectSites(new Map(projectList.map((p) => [p.name, p.location])));
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
            projectSites={projectSites}
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
        <ShiftsTab
          rows={shifts}
          projects={projects}
          projectNames={projectNames}
          // Refetch rather than append. Claiming the default clears it from
          // whichever shift held it before, and a local append cannot know
          // that — it left two rows both badged DEFAULT, which the server
          // would never return.
          onCreated={() => void getShifts().then(setShifts).catch(() => {})}
        />
      ) : tab === "today" ? (
        <TodayTab
          rows={todayRows}
          counts={todayCounts}
          projectNames={projectNames}
          date={today}
        />
      ) : tab === "analytics" ? (
        <AnalyticsTab summary={hours} records={records} name={name} from={from} to={to} />
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
  const [openShifts, setOpenShifts] = useState<string | null>(null);

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
                const sessions = record?.sessions ?? [];
                // Today's board answers "where is everyone now", so a
                // split-shift day shows its CURRENT stint rather than the
                // roll-up's first-in/last-out pair, which spans the gap
                // between shifts and belongs to neither.
                const latest = sessions.length > 1 ? sessions[sessions.length - 1] : null;
                const clockIn = latest ? latest.startedAt : record?.timeIn ?? null;
                const clockOut = latest ? latest.endedAt : record?.timeOut ?? null;
                const split = sessions.length > 1;
                const open = openShifts === employee.id;
                return (
                  <Fragment key={employee.id}>
                  <tr className="border-b border-border/60 last:border-0">
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
                      {/* Control only. The panel goes in a full-width row
                          below, so opening it never resizes this column and
                          the table cannot shift sideways. */}
                      {split ? (
                        <SessionsToggle
                          count={sessions.length}
                          open={open}
                          onToggle={() => setOpenShifts(open ? null : employee.id)}
                          label={employee.name}
                          variant="pill"
                        />
                      ) : null}
                    </td>

                    <td className="max-w-[240px] p-4 align-top text-muted-foreground">
                      <span className="block truncate" title={jobLabel(record, projectNames, employee)}>
                        {jobLabel(record, projectNames, employee)}
                      </span>
                    </td>

                    <td className="p-4 align-top">
                      {clockIn ? (
                        <>
                          <p className="flex items-center gap-2 font-semibold tabular-nums text-foreground">
                            {timeLabel(clockIn)}
                            <AttendancePhotoButton
                              url={record?.clockInPhotoUrl ?? null}
                              label={`Clock-in photo for ${employee.name}`}
                            />
                          </p>
                          {record?.clockInLat != null && record.clockInLng != null ? (
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
                      {clockOut ? (
                        <p className="flex items-center gap-2 whitespace-nowrap font-semibold tabular-nums text-foreground">
                          <span>
                            {timeLabel(clockOut)}
                            {dayOffsetLabel(clockIn, clockOut) ? (
                              <span className="ml-1 text-[10px] font-bold text-tertiary">
                                {dayOffsetLabel(clockIn, clockOut)}
                              </span>
                            ) : null}
                          </span>
                          <AttendancePhotoButton
                            url={record?.clockOutPhotoUrl ?? null}
                            label={`Clock-out photo for ${employee.name}`}
                          />
                        </p>
                      ) : clockIn ? (
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

                  {/* One sub-row per stint, reusing the parent's columns so
                      each shift's times land under Clock in / Clock out. A
                      floating panel in a colSpan cell lined up with nothing
                      above it. */}
                  {open
                    ? sessions.map((s, i) => (
                        <tr
                          key={s.id}
                          className={`bg-surface-low/60 text-xs ${
                            i === sessions.length - 1
                              ? "border-b border-border/60"
                              : "border-b border-border/30"
                          }`}
                        >
                          {/* A short rule before the label reads as a branch
                              off the row above, so these look like the day's
                              parts rather than more days. */}
                          <td className="py-2 pl-10 pr-4">
                            <span className="flex items-center gap-2 text-[10px] font-bold uppercase tracking-[0.12em] text-muted-foreground">
                              <span aria-hidden className="h-px w-3 shrink-0 bg-border" />
                              Shift {i + 1}
                            </span>
                          </td>
                          <td className="px-4 py-2" />
                          {/* Time only. The late note lives in the last
                              column instead: inline here it made the Clock in
                              column 12px wider on open, which is exactly the
                              sideways shift the sub-rows exist to avoid. */}
                          <td className="px-4 py-2">
                            <span className="flex items-center gap-2">
                              <span className="font-medium tabular-nums text-foreground">
                                {timeLabel(s.startedAt)}
                              </span>
                              {/* Per stint, not per day: a second clock-in has
                                  its own selfie, and the day roll-up only
                                  keeps the first one's. */}
                              <AttendancePhotoButton
                                url={s.clockInPhotoUrl}
                                label={`Clock-in photo, shift ${i + 1}`}
                              />
                            </span>
                          </td>
                          <td className="whitespace-nowrap px-4 py-2">
                            {s.endedAt ? (
                              <span className="font-medium tabular-nums text-foreground">
                                {timeLabel(s.endedAt)}
                                {dayOffsetLabel(s.startedAt, s.endedAt) ? (
                                  <span className="ml-1 text-[10px] font-bold text-tertiary">
                                    {dayOffsetLabel(s.startedAt, s.endedAt)}
                                  </span>
                                ) : null}
                                <AttendancePhotoButton
                                  url={s.clockOutPhotoUrl}
                                  label={`Clock-out photo, shift ${i + 1}`}
                                />
                              </span>
                            ) : (
                              <span className="italic text-muted-foreground">Still working</span>
                            )}
                          </td>
                          {/* Duration and lateness both land here, under a
                              column already sized for a status badge, so
                              nothing this row prints can widen the table.
                              `!= null`, not truthy: a 0m stint is a real
                              answer and was rendering as nothing at all. */}
                          <td className="px-4 py-2 pr-6">
                            <span className="inline-flex items-center gap-2">
                              <span className="font-medium tabular-nums text-foreground">
                                {s.durationMin != null ? formatMinutes(s.durationMin) : "—"}
                              </span>
                              {i === 0 && (s.lateByMin ?? 0) > 0 ? (
                                <span className="whitespace-nowrap rounded-full bg-tertiary/12 px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-[0.1em] text-tertiary">
                                  Late {formatMinutes(s.lateByMin)}
                                </span>
                              ) : null}
                            </span>
                          </td>
                        </tr>
                      ))
                    : null}
                  </Fragment>
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

// The status a row earns, worst first. Ranked rather than blended because
// "under expected" and "worked past the shift with nobody approving it" can
// both be true of the same person, and the second one is the one that costs
// money.
type HoursVerdict = "unscheduled" | "noRecords" | "unapprovedOt" | "under" | "onTarget";

const VERDICT: Record<HoursVerdict, { label: string; className: string }> = {
  unscheduled: { label: "Not scheduled", className: "text-muted-foreground/70" },
  // Deliberately quiet. Nobody clocked anything, which is a gap in the data
  // rather than a person falling behind, and eight loud badges saying it
  // drown out the two rows that need someone to act.
  noRecords: { label: "No records", className: "text-muted-foreground/70" },
  unapprovedOt: { label: "Unapproved OT", className: "bg-destructive/12 text-destructive" },
  under: { label: "Under", className: "bg-tertiary/15 text-tertiary" },
  onTarget: { label: "On target", className: "bg-success/15 text-success" },
};

function verdictFor(b: HoursBuckets): HoursVerdict {
  if (b.expectedMin === 0 && b.totalMin === 0) return "unscheduled";
  // Nothing clocked at all is its own state, not a shortfall. Ranked above the
  // shortfall check because "under by 56h" is a misleading way to say "we have
  // no attendance for this person".
  if (b.totalMin === 0) return "noRecords";
  // Hours past the shift that no approved submission covers. The gap, not the
  // raw figure: beyond-shift time with matching approved OT is just overtime.
  if (b.beyondShiftMin > b.otApprovedMin) return "unapprovedOt";
  // Normal, not total. Total carries rest-day and holiday time, which was
  // never part of what the schedule asked for.
  if (b.normalMin < b.expectedMin) return "under";
  return "onTarget";
}

const TH_NUM =
  "h-10 whitespace-nowrap px-3 text-right text-[10px] font-bold uppercase tracking-[0.1em] text-muted-foreground";

// A figure in a flat strip: no box of its own, and a sentence-case label rather
// than the tracked-out caps the boxed tiles use. Eight of these read as one
// panel; eight bordered tiles read as eight things.
function Metric({
  label,
  value,
  tone = "text-foreground",
}: {
  label: string;
  value: string;
  tone?: string;
}) {
  return (
    <div className="min-w-0">
      <p className={`truncate text-xl font-black leading-none tabular-nums ${tone}`}>{value}</p>
      <p className="mt-1.5 truncate text-xs text-muted-foreground">{label}</p>
    </div>
  );
}

// Zeros as a dash. Across a roster where most people have no beyond-shift,
// rest-day or holiday time, printing "0m" six times a row buries the two
// figures that someone actually has to do something about.
function HourCell({ min, className = "" }: { min: number; className?: string }) {
  return (
    <td className={`px-3 py-2 text-right tabular-nums ${className}`}>
      {min > 0 ? formatMinutes(min) : <span className="text-muted-foreground/40">—</span>}
    </td>
  );
}

function AnalyticsTab({
  summary,
  records,
  name,
  from,
  to,
}: {
  summary: OrgHoursSummary | null;
  records: AttendanceRecord[];
  name: (id: string) => string;
  from: string;
  to: string;
}) {
  // Counts, which the hours report does not carry — it buckets minutes. Scoped
  // to the employees the server put in `summary` rather than filtered again
  // here: the roster is the server's answer, so the strip and the table below
  // it cannot end up describing different populations.
  const counts = useMemo(() => {
    if (!summary) return null;
    const inScope = new Set(summary.employees.map((e) => e.employeeId));
    const rows = records.filter(
      (r) => inScope.has(r.employeeId) && r.date >= from && r.date <= to,
    );
    return {
      records: rows.length,
      late: rows.filter((r) => (r.lateByMin ?? 0) > 0).length,
      missing: rows.filter((r) => !r.timeIn && r.status !== "ON_LEAVE").length,
      leave: rows.filter((r) => r.status === "ON_LEAVE").length,
    };
  }, [summary, records, from, to]);

  if (!summary || !counts) {
    return (
      <section className={CARD_BARE}>
        <EmptyRow>Loading the hours summary…</EmptyRow>
      </section>
    );
  }

  const t = summary.totals;

  return (
    <div className="space-y-4 sm:space-y-6">
      <section className={`${CARD_BARE} space-y-5 p-5 sm:p-6`}>
        <header>
          <h3 className="text-base font-black text-foreground">Working hours summary</h3>
          {/* This rule is not guessable from the column headings, and getting it
              wrong is how a 143h row next to 56h expected reads as someone
              over-delivering rather than as unapproved overtime. */}
          <p className="mt-1 text-xs leading-relaxed text-muted-foreground">
            Worked is clocked time less breaks. Only{" "}
            <strong className="font-semibold text-foreground">normal</strong> hours count toward{" "}
            <strong className="font-semibold text-foreground">expected</strong> — a normal day caps at the shift
            length, so beyond-shift, rest-day and holiday time sit outside it.
          </p>
        </header>

        {/* One flat strip, following production, rather than eight individually
            bordered tiles: the boxes were taking more vertical room than the
            table they introduce. Hours above, counts below — minutes say how
            much was worked, counts say how often something went wrong. */}
        <div className="rounded-2xl border border-border/60 bg-surface-low px-5 py-4">
          <div className="grid grid-cols-2 gap-x-4 gap-y-4 sm:grid-cols-4">
            <Metric label="Worked" value={formatMinutes(t.totalMin)} />
            <Metric
              label="Normal / expected"
              value={`${formatMinutes(t.normalMin)} / ${formatMinutes(t.expectedMin)}`}
            />
            {/* Beyond-shift is separated from approved OT on purpose: hours past
                the shift that nobody approved are a liability, not overtime. */}
            <Metric
              label="Beyond shift"
              value={formatMinutes(t.beyondShiftMin)}
              tone={t.beyondShiftMin > t.otApprovedMin ? "text-tertiary" : undefined}
            />
            <Metric label="OT approved" value={formatMinutes(t.otApprovedMin)} />
          </div>

          <div className="mt-4 grid grid-cols-2 gap-x-4 gap-y-4 border-t border-border/50 pt-4 sm:grid-cols-4">
            <Metric label="Records" value={String(counts.records)} />
            {/* Coloured only when there is something there. Production tints
                these unconditionally, which paints a red "0 Missing" — an
                alarm about nothing. */}
            <Metric
              label="Late instances"
              value={String(counts.late)}
              tone={counts.late > 0 ? "text-tertiary" : undefined}
            />
            <Metric
              label="Missing"
              value={String(counts.missing)}
              tone={counts.missing > 0 ? "text-destructive" : undefined}
            />
            <Metric
              label="Leave days"
              value={String(counts.leave)}
              tone={counts.leave > 0 ? "text-primary" : undefined}
            />
          </div>
        </div>
      </section>

      <section className={CARD_BARE}>
        {summary.employees.length === 0 ? (
          <EmptyRow>No hours recorded in this range.</EmptyRow>
        ) : (
          <div className="nice-scrollbar overflow-x-auto">
            <table className="w-full min-w-[900px] text-sm">
              <thead>
                {/* Grouped so the pairs read as pairs: normal answers expected,
                    beyond-shift answers approved OT. Nine flat columns of
                    numbers make the reader work out which relate to which.
                    The group rules live in the header only — carried down
                    through every row they turned the table into a cage. */}
                <tr className="text-[10px] font-bold uppercase tracking-[0.12em] text-muted-foreground/60">
                  <th />
                  <th />
                  <th className="border-l border-border/30 px-3 pt-4 text-right" colSpan={2}>
                    Against schedule
                  </th>
                  <th className="border-l border-border/30 px-3 pt-4 text-right" colSpan={2}>
                    Overtime
                  </th>
                  <th className="border-l border-border/30 px-3 pt-4 text-right" colSpan={2}>
                    Outside schedule
                  </th>
                  <th className="border-l border-border/30" />
                </tr>
                <tr className="border-b border-border/60">
                  <th className={`${TH} h-10`}>Employee</th>
                  <th className={TH_NUM}>Worked</th>
                  <th className={`${TH_NUM} border-l border-border/30`}>Normal</th>
                  <th className={TH_NUM}>Expected</th>
                  <th className={`${TH_NUM} border-l border-border/30`}>Beyond shift</th>
                  <th className={TH_NUM}>Approved</th>
                  <th className={`${TH_NUM} border-l border-border/30`}>Rest day</th>
                  <th className={TH_NUM}>Holiday</th>
                  <th className={`${TH_NUM} border-l border-border/30 pr-6 text-left`}>Status</th>
                </tr>
              </thead>
              <tbody>
                {summary.employees.map((row) => {
                  const b = row.buckets;
                  const verdict = verdictFor(b);
                  // Only a genuine shortfall is coloured. Someone with no
                  // records at all is not "behind", and marking their zero
                  // turned the whole column amber.
                  const short = verdict === "under";
                  const owing = b.beyondShiftMin > b.otApprovedMin;
                  return (
                    <tr
                      key={row.employeeId}
                      className="border-b border-border/50 transition-colors duration-150 hover:bg-surface-low/70"
                    >
                      <td className="py-2 pl-6 pr-3">
                        <p className="truncate font-semibold leading-tight text-foreground">
                          {name(row.employeeId)}
                        </p>
                        {/* Truncated rather than wrapped: one long address used
                            to make its row half again as tall as the rest. */}
                        <p
                          className="max-w-[24ch] truncate text-xs leading-tight text-muted-foreground"
                          title={row.email ?? undefined}
                        >
                          {row.email ?? ""}
                        </p>
                      </td>
                      <HourCell min={b.totalMin} className="font-bold text-foreground" />
                      <HourCell
                        min={b.normalMin}
                        className={`border-l border-border/30 ${
                          short ? "font-semibold text-tertiary" : "text-foreground"
                        }`}
                      />
                      <HourCell min={b.expectedMin} className="text-muted-foreground" />
                      <HourCell
                        min={b.beyondShiftMin}
                        className={`border-l border-border/30 ${
                          owing ? "font-semibold text-destructive" : "text-foreground"
                        }`}
                      />
                      <HourCell min={b.otApprovedMin} className="text-muted-foreground" />
                      <HourCell min={b.restDayMin} className="border-l border-border/30 text-muted-foreground" />
                      <HourCell min={b.publicHolidayMin} className="text-muted-foreground" />
                      <td className="border-l border-border/30 py-2 pl-3 pr-6">
                        {/* The two states worth acting on keep a pill. The
                            quiet ones are plain text, so a roster that is
                            mostly fine looks mostly fine. */}
                        <span
                          className={`inline-flex whitespace-nowrap text-[10px] font-bold uppercase tracking-[0.1em] ${
                            verdict === "noRecords" || verdict === "unscheduled"
                              ? VERDICT[verdict].className
                              : `rounded-full px-2.5 py-1 ${VERDICT[verdict].className}`
                          }`}
                        >
                          {VERDICT[verdict].label}
                        </span>
                      </td>
                    </tr>
                  );
                })}
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
          <h3 className="text-base font-black text-foreground">Supervisor response time</h3>
          <p className="mt-1 text-xs text-muted-foreground">
            How long each approver takes to decide. The slow count is measured against the
            organisation's SLA.
          </p>
        </div>

        {rows.length === 0 ? (
          <EmptyRow>Nothing was decided in this range.</EmptyRow>
        ) : (
          <div className="nice-scrollbar overflow-x-auto">
            <table className="w-full min-w-[760px] text-sm">
              <thead>
                {/* Counts and durations right-align, matching the Analytics
                    table. Left-aligned numbers under left-aligned headings made
                    two tables on adjacent tabs read as two products. */}
                <tr className="border-b border-border/60">
                  <th className={`${TH} h-10`}>Supervisor</th>
                  <th className={TH_NUM}>Decisions</th>
                  <th className={TH_NUM}>Approved</th>
                  <th className={TH_NUM}>Rejected</th>
                  <th className={`${TH_NUM} border-l border-border/30`}>Slow</th>
                  <th className={TH_NUM}>Average</th>
                  <th className={`${TH_NUM} pr-6`}>Worst</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr
                    key={r.reviewerId}
                    className="border-b border-border/50 transition-colors duration-150 hover:bg-surface-low/70"
                  >
                    <td className="py-2 pl-6 pr-3 font-semibold text-foreground">{r.reviewerName}</td>
                    <td className="px-3 py-2 text-right font-bold tabular-nums text-foreground">
                      {r.totalDecisions}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums text-muted-foreground">
                      {r.approvedCount}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums text-muted-foreground">
                      {r.rejectedCount}
                    </td>
                    <td
                      className={`border-l border-border/30 px-3 py-2 text-right tabular-nums ${
                        r.slowDecisionCount > 0 ? "font-bold text-tertiary" : "text-muted-foreground/40"
                      }`}
                    >
                      {r.slowDecisionCount > 0 ? r.slowDecisionCount : "—"}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums text-foreground">
                      {formatMinutes(r.avgDelayMinutes)}
                    </td>
                    {/* The worst case earns a column because an average hides it. */}
                    <td className="px-3 py-2 pr-6 text-right tabular-nums text-muted-foreground">
                      {formatMinutes(r.maxDelayMinutes)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className={`${CARD_BARE} space-y-4 p-5 sm:p-6`}>
        <header>
          <h3 className="text-base font-black text-foreground">Clock-in photo storage</h3>
          <p className="mt-1 text-xs text-muted-foreground">
            What the clock-in selfies are costing, and whether any have gone missing since they
            were taken.
          </p>
        </header>
        {/* The same flat strip the Analytics summary uses, rather than three
            bordered tiles — three metrics do not need three boxes. */}
        <div className="rounded-2xl border border-border/60 bg-surface-low px-5 py-4">
          <div className="grid grid-cols-2 gap-x-4 gap-y-4 sm:grid-cols-3">
            <Metric label="Photos held" value={selfies ? String(selfies.photoCount) : "—"} />
            <Metric label="Storage used" value={selfies ? formatBytes(selfies.totalBytes) : "—"} />
            {/* Missing is worth its own figure: a pruned file is fine, but a
                rising count is what a broken upload path looks like. */}
            <Metric
              label="Missing files"
              value={selfies ? String(selfies.missingCount) : "—"}
              tone={selfies && selfies.missingCount > 0 ? "text-destructive" : undefined}
            />
          </div>
        </div>
      </section>
    </div>
  );
}

// Rows per page in both History tables. Shared so the two sections page in
// step — a reader comparing an approval against the day it belongs to should
// not have to track two different page numbers.
const HISTORY_PAGE_SIZE = 10;

// The audit DTO types `kind` as a plain string, so this falls back rather than
// asserting the map covers every value the server might add later.
function eventLabel(kind: string): string {
  return (EVENT_LABEL as Record<string, string>)[kind] ?? kind.replace(/_/g, " ");
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
  // Paged rather than cut at 200. Both lists are whole-range reads filtered in
  // the browser, so paging here costs no request — and the previous slice
  // silently dropped everything past the 200th row.
  const auditPaged = usePaged(audit, HISTORY_PAGE_SIZE);
  const recordsPaged = usePaged(rows, HISTORY_PAGE_SIZE);
  // One open row at a time: two expanded shift panels in a dense table stop
  // being easier to read than the table itself.
  const [expandedRecord, setExpandedRecord] = useState<string | null>(null);

  return (
    <div className="space-y-4 sm:space-y-6">
      {/* Approvals lead. The question that brings someone to this tab is almost
          always "is anything sitting unreviewed" — the raw record list is what
          you check afterwards to see why. */}
      <section className={CARD_BARE}>
        <header className="flex flex-wrap items-start justify-between gap-2 border-b border-border/60 px-6 py-4">
          <div>
            <h3 className="text-base font-black text-foreground">Approval trail</h3>
            <p className="mt-1 text-xs text-muted-foreground">
              Who decided what. Pending rows are included — a request nobody has touched is the
              more urgent half of the question.
            </p>
          </div>
        </header>
        {audit.length === 0 ? (
          <EmptyRow>No approvals in this range.</EmptyRow>
        ) : (
          <div className="nice-scrollbar overflow-x-auto">
            <table className="w-full min-w-[820px] text-sm">
              <thead>
                <tr className="border-b border-border/60">
                  <th className={`${TH} h-10`}>Submitted</th>
                  <th className={`${TH} h-10`}>Employee</th>
                  <th className={`${TH} h-10`}>Event</th>
                  <th className={`${TH} h-10`}>Status</th>
                  <th className={`${TH} h-10`}>Decided by</th>
                  <th className={`${TH_NUM} pr-6`}>Took</th>
                </tr>
              </thead>
              <tbody>
                {auditPaged.pageItems.map((a) => (
                  <tr
                    key={a.id}
                    className="border-b border-border/50 transition-colors duration-150 hover:bg-surface-low/70"
                  >
                    <td className="whitespace-nowrap py-2 pl-6 pr-3 text-muted-foreground">
                      {dateLabel(a.submittedAt.slice(0, 10))}
                    </td>
                    <td className="px-3 py-2 font-semibold text-foreground">{a.employeeName}</td>
                    {/* The shared label map, so this column reads "Clock out"
                        rather than shouting the raw enum as "CLOCK OUT". */}
                    <td className="px-3 py-2 text-muted-foreground">{eventLabel(a.kind)}</td>
                    <td className="px-3 py-2"><ApprovalStatusBadge status={a.status} /></td>
                    <td className="px-3 py-2 text-muted-foreground">{a.reviewerName ?? "—"}</td>
                    <td className="px-3 py-2 pr-6 text-right tabular-nums text-foreground">
                      {formatMinutes(a.delayMinutes)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <TablePager paged={auditPaged} noun="approval" />
      </section>

      <section className={CARD_BARE}>
        <header className="flex flex-wrap items-baseline justify-between gap-2 border-b border-border/60 px-6 py-4">
          <h3 className="text-base font-black text-foreground">Attendance records</h3>
        </header>
        {rows.length === 0 ? (
          <EmptyRow>No attendance in this range under these filters.</EmptyRow>
        ) : (
          <div className="nice-scrollbar overflow-x-auto">
            <table className="w-full min-w-[820px] text-sm">
              <thead>
                <tr className="border-b border-border/60">
                  {/* Leading column for the expand arrow on split-shift days.
                      The chevron used to sit under the employee name at 12px,
                      where nobody found it. */}
                  <th className="h-10 w-10 pl-6" />
                  <th className={`${TH} h-10 !pl-3`}>Date</th>
                  <th className={`${TH} h-10`}>Employee</th>
                  <th className={`${TH} h-10`}>Project</th>
                  <th className={TH_NUM}>Clock in</th>
                  <th className={TH_NUM}>Clock out</th>
                  <th className={TH_NUM}>Worked</th>
                  <th className={`${TH} h-10 pr-6`}>Status</th>
                </tr>
              </thead>
              <tbody>
                {recordsPaged.pageItems.map((r) => {
                  const sessions = r.sessions ?? [];
                  // A split-shift day shows its LATEST stint in the clock
                  // columns, with the earlier ones a click away. The roll-up's
                  // first-in/last-out pair spans the gap between sessions, so
                  // a second shift running past midnight printed a clock-out
                  // that looked earlier than the clock-in.
                  const latest = sessions.length > 1 ? sessions[sessions.length - 1] : null;
                  const clockIn = latest ? latest.startedAt : r.timeIn;
                  const clockOut = latest ? latest.endedAt : r.timeOut;
                  const split = sessions.length > 1;
                  const open = expandedRecord === r.id;
                  return (
                    <Fragment key={r.id}>
                    <tr
                      className="border-b border-border/50 transition-colors duration-150 hover:bg-surface-low/70"
                    >
                      <td className="py-2 pl-6 align-top">
                        {split ? (
                          <SessionsToggle
                            count={sessions.length}
                            open={open}
                            onToggle={() => setExpandedRecord(open ? null : r.id)}
                            label={`${name(r.employeeId)} on ${dateLabel(r.date)}`}
                          />
                        ) : null}
                      </td>
                      <td className="whitespace-nowrap px-3 py-2 align-top text-muted-foreground">
                        {dateLabel(r.date)}
                      </td>
                      <td className="px-3 py-2 align-top">
                        <p className="font-semibold text-foreground">{name(r.employeeId)}</p>
                        {split ? (
                          <p className="text-[10px] font-bold uppercase tracking-[0.1em] text-muted-foreground">
                            {sessions.length} shifts
                          </p>
                        ) : null}
                      </td>
                      <td className="px-3 py-2 align-top text-muted-foreground">
                        {r.projectId ? projectNames.get(r.projectId) ?? "—" : "—"}
                      </td>
                      <td className="px-3 py-2 align-top">
                        <span className="flex items-center justify-end gap-2 tabular-nums">
                          {timeLabel(clockIn)}
                          <AttendancePhotoButton
                            url={latest ? latest.clockInPhotoUrl : r.clockInPhotoUrl}
                            label={`Clock-in photo, ${name(r.employeeId)} on ${dateLabel(r.date)}`}
                          />
                        </span>
                      </td>
                      <td className="whitespace-nowrap px-3 py-2 align-top">
                        <span className="flex items-center justify-end gap-2 tabular-nums">
                          <span>
                            {timeLabel(clockOut)}
                            {/* Night shift. Without this the column reads as a
                                clock-out that happened before the clock-in. */}
                            {dayOffsetLabel(clockIn, clockOut) ? (
                              <span className="ml-1 text-[10px] font-bold text-tertiary">
                                {dayOffsetLabel(clockIn, clockOut)}
                              </span>
                            ) : null}
                          </span>
                          <AttendancePhotoButton
                            url={latest ? latest.clockOutPhotoUrl : r.clockOutPhotoUrl}
                            label={`Clock-out photo, ${name(r.employeeId)} on ${dateLabel(r.date)}`}
                          />
                        </span>
                      </td>
                      {/* The day total either way — the sum of every stint, not
                          just the one shown beside it. */}
                      <td className="px-3 py-2 text-right align-top font-semibold tabular-nums text-foreground">
                        {formatMinutes(r.durationMin)}
                      </td>
                      <td className="px-3 py-2 pr-6 align-top">
                        <AttendanceStatusBadge status={displayStatus(r)} />
                      </td>
                    </tr>

                    {/* Sub-rows on the parent's columns, so each stint's
                        times sit under Clock in / Clock out. */}
                    {open
                      ? sessions.map((s, i) => (
                          <tr
                            key={s.id}
                            className={`bg-surface-low/60 text-xs ${
                              i === sessions.length - 1
                                ? "border-b border-border/60"
                                : "border-b border-border/30"
                            }`}
                          >
                            <td />
                            <td className="py-2 pl-3 pr-3">
                              <span className="flex items-center gap-2 text-[10px] font-bold uppercase tracking-[0.12em] text-muted-foreground">
                                <span aria-hidden className="h-px w-3 shrink-0 bg-border" />
                                Shift {i + 1}
                              </span>
                            </td>
                            <td className="px-3 py-2" />
                            <td className="px-3 py-2" />
                            <td className="px-3 py-2">
                              <span className="flex items-center justify-end gap-2">
                                <span className="font-medium tabular-nums text-foreground">
                                  {timeLabel(s.startedAt)}
                                </span>
                                <AttendancePhotoButton
                                  url={s.clockInPhotoUrl}
                                  label={`Clock-in photo, shift ${i + 1}`}
                                />
                              </span>
                            </td>
                            <td className="whitespace-nowrap px-3 py-2 text-right">
                              {s.endedAt ? (
                                <span className="font-medium tabular-nums text-foreground">
                                  {timeLabel(s.endedAt)}
                                  {dayOffsetLabel(s.startedAt, s.endedAt) ? (
                                    <span className="ml-1 text-[10px] font-bold text-tertiary">
                                      {dayOffsetLabel(s.startedAt, s.endedAt)}
                                    </span>
                                  ) : null}
                                  <AttendancePhotoButton
                                    url={s.clockOutPhotoUrl}
                                    label={`Clock-out photo, shift ${i + 1}`}
                                  />
                                </span>
                              ) : (
                                <span className="italic text-muted-foreground">Still in</span>
                              )}
                            </td>
                            <td className="px-3 py-2 text-right">
                              <span className="font-medium tabular-nums text-foreground">
                                {s.durationMin != null ? formatMinutes(s.durationMin) : "—"}
                              </span>
                            </td>
                            <td className="px-3 py-2 pr-6">
                              {i === 0 && (s.lateByMin ?? 0) > 0 ? (
                                <span className="whitespace-nowrap rounded-full bg-tertiary/12 px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-[0.1em] text-tertiary">
                                  Late {formatMinutes(s.lateByMin)}
                                </span>
                              ) : null}
                            </td>
                          </tr>
                        ))
                      : null}
                    </Fragment>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
        <TablePager paged={recordsPaged} noun="record" />
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
      {/* No "Employees" heading: the active tab directly above already says
          it. Only the count is kept — that is the part the tab cannot tell
          you, and it reflects the filters rather than the roster. */}
      <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
        {rows.length} {rows.length === 1 ? "person" : "people"}
      </p>

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
              {/* One project plus a count, not the whole list. Concatenating
                  every site produced a line that truncated mid-word and told
                  you nothing — the reference app renders the same data as
                  three wrapped lines of project codes per row. The full list
                  is on hover and on the detail page. */}
              <p className="truncate text-xs text-muted-foreground" title={detailLine(employee, projects)}>
                {detailSummary(employee, projects)}
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

// Every site, for the hover title and nothing else.
function detailLine(employee: Employee, projects: string[]): string {
  return [employee.jobTitle, ...projects].filter(Boolean).join(" • ") || "—";
}

// What the row actually prints: the job title, then one site and how many
// others. Bounded, so a person on two projects and a person on eleven produce
// the same shaped line.
function detailSummary(employee: Employee, projects: string[]): string {
  const [first, ...rest] = projects;
  const sites = first ? (rest.length > 0 ? `${first} +${rest.length}` : first) : null;
  return [employee.jobTitle, sites].filter(Boolean).join(" • ") || "—";
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
type DayKind = "onTime" | "late" | "absent" | "leave" | "offDuty" | "noData" | "future";

type DayCell = {
  date: string;
  kind: DayKind;
  record: AttendanceRecord | null;
};

// Salience is ordered on purpose, faintest to loudest: no-records is barely
// there, a routine on-time day is legible but calm, and absent is the loudest
// thing on the card. The previous ramp had this inverted by accident — on-time
// and late were the two palest tokens in the theme, so the states you actually
// scan for were the hardest to see.
const CELL_CLASS: Record<DayKind, string> = {
  onTime: "bg-accent",
  late: "bg-tertiary/75",
  absent: "bg-destructive/85",
  leave: "bg-primary/45",
  // A day nobody was expected in reads as background, not as a gap in the
  // record — otherwise every weekend looks like an absence.
  offDuty: "bg-muted",
  // Before this employee has any record at all. Distinct from absent on
  // purpose: we have no evidence either way, and colouring it red would invent
  // months of absence out of a system that simply was not recording yet.
  noData: "bg-transparent ring-1 ring-inset ring-border/50",
  future: "bg-muted/30",
};

const KIND_LABEL: Record<DayKind, string> = {
  onTime: "On time",
  late: "Late",
  absent: "Absent",
  leave: "On leave",
  offDuty: "Not scheduled",
  noData: "No records",
  future: "Upcoming",
};

const KIND_TONE: Record<DayKind, string> = {
  onTime: "text-foreground",
  late: "text-tertiary",
  absent: "text-destructive",
  leave: "text-primary",
  offDuty: "text-muted-foreground",
  noData: "text-muted-foreground",
  future: "text-muted-foreground",
};

// `future` gets no swatch: it is self-evident from position and would only add
// a seventh near-invisible grey to the row.
const LEGEND: DayKind[] = ["onTime", "late", "absent", "leave", "offDuty", "noData"];

// Named periods rather than raw week counts. "Weekly" is deliberately absent:
// a single week is one column of seven cells, which is a list, not a heatmap —
// the smallest span the grid can actually show a shape over is a month.
const RANGES: { label: string; weeks: number }[] = [
  { label: "Month", weeks: 5 },
  { label: "Quarter", weeks: 13 },
  { label: "Year", weeks: 52 },
];

const WEEKDAYS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

function AttendanceHeatmap({
  records,
  workingDays,
  today,
  initialWeeks = 13,
}: {
  records: AttendanceRecord[];
  workingDays: Set<number>;
  today: string;
  initialWeeks?: number;
}) {
  const [weeks, setWeeks] = useState<number>(initialWeeks);
  // Hover drives the readout; a click pins a day there so it survives the
  // mouse moving away to the day-by-day list below.
  const [hovered, setHovered] = useState<string | null>(null);
  const [pinned, setPinned] = useState<string | null>(null);

  const { columns, monthLabels, cellsByDate, tally } = useMemo(() => {
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
    const index = new Map<string, DayCell>();
    const counts: Record<DayKind, number> = {
      onTime: 0, late: 0, absent: 0, leave: 0, offDuty: 0, noData: 0, future: 0,
    };
    let scheduled = 0;
    let offSite = 0;
    // Worked minutes over the visible window. `durationMin` is the day
    // roll-up — the sum of its stints, already net of breaks — so this is the
    // same quantity the hours report calls Worked, just scoped to the range on
    // screen rather than to the calendar month.
    let workedMin = 0;
    let longestMin = 0;

    for (let w = 0; w < weeks; w++) {
      const column: DayCell[] = [];
      for (let d = 0; d < 7; d++) {
        const day = new Date(start);
        day.setUTCDate(start.getUTCDate() + w * 7 + d);
        const iso = day.toISOString().slice(0, 10);
        const isoWeekday = ((day.getUTCDay() + 6) % 7) + 1;   // Mon = 1
        const record = byDate.get(iso) ?? null;

        let kind: DayKind;
        if (iso > today) kind = "future";
        else if (record?.status === "ON_LEAVE") kind = "leave";
        else if (record?.timeIn) kind = (record.lateByMin ?? 0) > 0 ? "late" : "onTime";
        else if (!workingDays.has(isoWeekday)) kind = "offDuty";
        else if (firstRecorded === null || iso < firstRecorded) kind = "noData";
        else kind = "absent";

        counts[kind]++;
        if (kind !== "future" && kind !== "offDuty" && kind !== "noData") scheduled++;
        if (isOffSite(record)) offSite++;
        workedMin += record?.durationMin ?? 0;
        if ((record?.durationMin ?? 0) > longestMin) longestMin = record!.durationMin!;

        const cell: DayCell = { date: iso, kind, record };
        column.push(cell);
        index.set(iso, cell);
      }
      cols.push(column);
    }

    // A column is labelled when its week opens a month the previous column did
    // not — which puts "Mar" above the first week of March rather than above
    // whichever column happened to be divisible by four. Two columns of
    // clearance stops "Feb" and "Mar" printing on top of each other when the
    // window starts on the last week of a month.
    const labels: (string | null)[] = [];
    let lastLabelAt = -99;
    cols.forEach((week, i) => {
      const iso = week[0].date;
      const month = Number(iso.slice(5, 7)) - 1;
      const prevMonth = i === 0 ? -1 : Number(cols[i - 1][0].date.slice(5, 7)) - 1;
      if (month !== prevMonth && i - lastLabelAt >= 2) {
        // January carries its year: a 52-week window crosses a new year, and
        // "Jan" alone leaves you guessing which one.
        labels.push(month === 0 ? `${MONTHS[month]} ${iso.slice(2, 4)}` : MONTHS[month]);
        lastLabelAt = i;
      } else {
        labels.push(null);
      }
    });

    return {
      columns: cols,
      monthLabels: labels,
      cellsByDate: index,
      tally: { counts, scheduled, offSite, workedMin, longestMin },
    };
  }, [records, workingDays, today, weeks]);

  const active = cellsByDate.get(hovered ?? pinned ?? "") ?? null;
  const present = tally.counts.onTime + tally.counts.late;
  const rate = tally.scheduled > 0 ? Math.round((present / tally.scheduled) * 100) : null;
  // Averaged over days actually PRESENT, not scheduled days: dividing by
  // scheduled would quietly fold absences into the figure and read as short
  // days rather than missing ones.
  const avgMin = present > 0 ? Math.round(tally.workedMin / present) : null;

  return (
    <section className={`${CARD_BARE} space-y-4 p-5 sm:p-6`}>
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h4 className="font-bold text-foreground">Activity</h4>
          {/* The headline read, so the grid never has to be counted by eye. */}
          {/* Worked hours moved here from the removed This-month card, so the
              figure follows the range being looked at. Expected hours are NOT
              shown: they come from the server for the roster's date range, and
              re-deriving them per window would need the employee's standard
              day, which this component does not have. */}
          <p className="mt-0.5 text-xs text-muted-foreground">
            {rate === null
              ? "No scheduled days in this range."
              : `Present ${present} of ${tally.scheduled} scheduled days · ${rate}% attendance`}
          </p>
        </div>

        <div className="flex items-center gap-0.5 rounded-full border border-border/60 bg-surface-low p-1">
          {RANGES.map((range) => (
            <button
              key={range.label}
              type="button"
              onClick={() => setWeeks(range.weeks)}
              aria-pressed={weeks === range.weeks}
              className={`rounded-full px-3 py-1 text-[11px] font-bold uppercase tracking-[0.12em] transition-colors duration-200 ${
                weeks === range.weeks
                  ? "bg-card text-foreground shadow-ambient"
                  : "text-muted-foreground hover:text-foreground"
              }`}
            >
              {range.label}
            </button>
          ))}
        </div>
      </header>

      <div className="nice-scrollbar overflow-x-auto pb-1">
        <div
          className="inline-flex flex-col gap-1"
          onMouseLeave={() => setHovered(null)}
        >
          {/* Month ruler. Without it the grid is 182 anonymous squares — you
              can see a red day but not which month it landed in. */}
          <div className="flex gap-1 pl-9" aria-hidden>
            {monthLabels.map((label, i) => (
              <div key={columns[i][0].date} className="w-4 shrink-0">
                {label ? (
                  <span className="block whitespace-nowrap text-[10px] font-semibold uppercase tracking-[0.1em] text-muted-foreground">
                    {label}
                  </span>
                ) : null}
              </div>
            ))}
          </div>

          <div className="flex gap-1">
            {/* Every other weekday, so the gutter labels the rows without
                crowding them. */}
            <div className="flex w-9 shrink-0 flex-col gap-1 pr-1.5 text-right" aria-hidden>
              {WEEKDAYS.map((day, i) => (
                <span key={day} className="h-4 text-[10px] leading-4 text-muted-foreground">
                  {i % 2 === 0 ? day : ""}
                </span>
              ))}
            </div>

            {columns.map((week) => (
              <div key={week[0].date} className="flex shrink-0 flex-col gap-1">
                {week.map((cell) => {
                  const isPinned = pinned === cell.date;
                  return (
                    <button
                      key={cell.date}
                      type="button"
                      aria-label={describeCell(cell)}
                      aria-pressed={isPinned}
                      onMouseEnter={() => setHovered(cell.date)}
                      onFocus={() => setHovered(cell.date)}
                      onBlur={() => setHovered(null)}
                      onClick={() => setPinned(isPinned ? null : cell.date)}
                      className={`h-4 w-4 rounded-[4px] transition-transform duration-150 hover:scale-[1.2] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring ${
                        CELL_CLASS[cell.kind]
                      } ${isPinned ? "ring-2 ring-inset ring-foreground/70" : ""}`}
                    />
                  );
                })}
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* The hovered day, spelled out. This replaces a native `title` tooltip:
          those arrive a second late, cannot be reached by keyboard, and get
          clipped by the scroll container the grid sits in. */}
      <div
        aria-live="polite"
        className="flex min-h-[2.75rem] flex-wrap items-center gap-x-3 gap-y-1 rounded-2xl border border-border/60 bg-surface-low px-4 py-2.5"
      >
        {active ? (
          <>
            <span className="text-xs font-bold tabular-nums text-foreground">
              {dateLabel(active.date)}
            </span>
            <span
              className={`inline-flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-[0.14em] ${KIND_TONE[active.kind]}`}
            >
              <span className={`h-2.5 w-2.5 shrink-0 rounded-[3px] ${CELL_CLASS[active.kind]}`} aria-hidden />
              {KIND_LABEL[active.kind]}
            </span>
            {active.record?.timeIn ? (
              <span className="text-xs tabular-nums text-muted-foreground">
                {spanLabel(active.record.timeIn, active.record.timeOut)}
              </span>
            ) : null}
            {active.record?.durationMin ? (
              <span className="text-xs tabular-nums text-muted-foreground">
                {formatMinutes(active.record.durationMin)}
              </span>
            ) : null}
            {active.record?.lateByMin ? (
              <span className="text-xs font-semibold tabular-nums text-tertiary">
                Late {formatMinutes(active.record.lateByMin)}
              </span>
            ) : null}
            {isOffSite(active.record) ? (
              <span className="text-[11px] font-bold uppercase tracking-[0.14em] text-destructive">
                Off-site · {metreLabel(active.record?.clockInDistanceMeters ?? null)}
              </span>
            ) : null}
            {pinned === active.date ? (
              <button
                type="button"
                onClick={() => setPinned(null)}
                className="ml-auto text-[11px] font-semibold text-primary hover:underline"
              >
                Unpin
              </button>
            ) : null}
          </>
        ) : (
          <span className="text-xs text-muted-foreground">
            Hover a day for its detail, or click to keep it here.
          </span>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 border-t border-border/60 pt-3">
        {LEGEND.map((kind) => (
          <span key={kind} className="flex items-center gap-1.5 text-[11px] text-muted-foreground">
            <span className={`h-3 w-3 shrink-0 rounded-[3px] ${CELL_CLASS[kind]}`} aria-hidden />
            {KIND_LABEL[kind]}
            {/* The count makes the key double as a tally, so "how many late"
                does not send you back to counting squares. */}
            <span className="font-bold tabular-nums text-foreground">{tally.counts[kind]}</span>
          </span>
        ))}
        {tally.offSite > 0 ? (
          <span className="text-[11px] text-muted-foreground">
            Off-site <span className="font-bold tabular-nums text-foreground">{tally.offSite}</span>
          </span>
        ) : null}
      </div>

      {/* Hours for the selected range. Deliberately hours only — the legend
          above already carries the day counts, and the header the presence
          rate, so repeating either here would give the same number three
          places to disagree. All three move with the range buttons. */}
      <div className="grid grid-cols-2 gap-x-4 gap-y-4 rounded-2xl border border-border/60 bg-surface-low px-5 py-4 sm:grid-cols-3">
        <Metric label="Total worked" value={formatMinutes(tally.workedMin)} />
        <Metric
          label="Average per day present"
          value={avgMin === null ? "—" : formatMinutes(avgMin)}
        />
        {/* The longest single day, because an average hides the eleven-hour
            one that a reader opened this page to find. */}
        <Metric
          label="Longest day"
          value={tally.longestMin > 0 ? formatMinutes(tally.longestMin) : "—"}
        />
      </div>
    </section>
  );
}

function describeCell(cell: DayCell): string {
  const when = dateLabel(cell.date);
  if (cell.kind === "future") return `${when} · upcoming`;
  if (cell.kind === "offDuty") return `${when} · not scheduled`;
  if (cell.kind === "noData") return `${when} · no records`;
  if (cell.kind === "absent") return `${when} · no clock-in`;
  if (cell.kind === "leave") return `${when} · on leave`;

  const parts = [when, KIND_LABEL[cell.kind], timeLabel(cell.record?.timeIn ?? null)];
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
  projectSites,
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
  projectSites: Map<string, string | null>;
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

  const { employee, projects } = row;

  // No `.slice(0, 30)` any more — the list is paged instead. The cap quietly
  // hid every day past the thirtieth, which on a year of history is most of
  // it, with nothing on screen saying so.
  const history = useMemo(
    () => [...records].sort((a, b) => b.date.localeCompare(a.date)),
    [records],
  );
  const historyPaged = usePaged(history, HISTORY_PAGE_SIZE);

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

        {/* Addresses get their own block rather than a row in the fact grid.
            Every Fact above is one inline "label: value" line; a name with a
            street address under it is two, and forcing that into the grid put
            the value at an offset that lined up with nothing. Only rendered
            when an address actually exists — otherwise the Projects fact above
            already says everything. */}
        {projects.some((projectName) => projectSites.get(projectName)) ? (
          <div className="mt-4 border-t border-border/60 pt-3">
            <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
              Sites
            </p>
            <ul className="mt-2 grid gap-2 sm:grid-cols-2">
              {projects.map((projectName) => {
                const site = projectSites.get(projectName);
                return (
                  <li key={projectName} className="min-w-0">
                    <p className="truncate text-xs font-semibold text-foreground" title={projectName}>
                      {projectName}
                    </p>
                    {site ? (
                      <p className="truncate text-xs text-muted-foreground" title={site}>
                        {site}
                      </p>
                    ) : (
                      <p className="text-xs text-muted-foreground/60">No address on file</p>
                    )}
                  </li>
                );
              })}
            </ul>
          </div>
        ) : null}
      </section>

      {/* Today and This-month cards removed: the heatmap below answers the
          same questions over a range you can choose, rather than two fixed
          windows. What each carried and where it now lives:
            · today's clock + lateness  → the heatmap's newest cells and readout
            · the month's status counts → the heatmap legend, per range
            · worked hours             → the heatmap header, per range
            · today's per-event approvals → History › Approval trail */}
      {/* The shape of the year, above the day-by-day detail that explains it */}
      <AttendanceHeatmap records={records} workingDays={workingDays} today={today} />

      {/* Day by day */}
      <section className={`${CARD_BARE} overflow-hidden`}>
        <header className="px-5 pt-5 sm:px-6 sm:pt-6">
          <h4 className="font-bold text-foreground">Recent attendance</h4>
        </header>

        {history.length === 0 ? (
          <EmptyRow>No attendance recorded for this employee.</EmptyRow>
        ) : (
          <ul className="mt-4 divide-y divide-border/60 border-t border-border/60">
            {historyPaged.pageItems.map((record) => (
              <li key={record.id} className="flex flex-wrap items-start justify-between gap-3 p-4 px-6">
                <div className="min-w-0 space-y-0.5">
                  {/* `dateLabel`, not the raw ISO string: the heatmap readout
                      and this list sit inches apart and were printing the same
                      day two different ways. */}
                  <p className="text-sm font-bold tabular-nums text-foreground">
                    {dateLabel(record.date)}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {/* Latest stint on a split-shift day, same as the org
                        history table, so the two never describe one day
                        differently. */}
                    <span className="tabular-nums">
                      {(record.sessions ?? []).length > 1
                        ? spanLabel(
                            record.sessions![record.sessions!.length - 1].startedAt,
                            record.sessions![record.sessions!.length - 1].endedAt,
                          )
                        : spanLabel(record.timeIn, record.timeOut)}
                    </span>
                    {record.projectId ? ` • ${projectNames.get(record.projectId) ?? record.projectId}` : ""}
                    {record.durationMin ? ` • ${formatMinutes(record.durationMin)}` : ""}
                  </p>

                  {/* The day's selfies. On a split shift the roll-up keeps the
                      first clock-in's and the last clock-out's, and the
                      per-stint ones sit in the shifts panel below. */}
                  {record.clockInPhotoUrl || record.clockOutPhotoUrl ? (
                    <p className="flex items-center gap-2 pt-0.5">
                      <AttendancePhotoButton
                        url={record.clockInPhotoUrl}
                        label={`Clock-in photo, ${dateLabel(record.date)}`}
                      />
                      <AttendancePhotoButton
                        url={record.clockOutPhotoUrl}
                        label={`Clock-out photo, ${dateLabel(record.date)}`}
                      />
                    </p>
                  ) : null}

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
                  {/* This list is a stack of <li>, not a table, so it carries
                      its own toggle rather than borrowing a column. */}
                  <SessionsExpander sessions={record.sessions ?? []} />
                </div>

                <div className="flex shrink-0 items-center gap-2">
                  <AttendanceStatusBadge status={displayStatus(record)} />
                  <ApprovalStatusBadge status={record.approvalStatus} />
                </div>
              </li>
            ))}
          </ul>
        )}
        <TablePager paged={historyPaged} noun="day" />
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

const OT_STATUSES = ["ALL", "PENDING", "APPROVED", "REJECTED", "CANCELLED"] as const;
type OtStatusFilter = (typeof OT_STATUSES)[number];

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
  const [status, setStatus] = useState<OtStatusFilter>("ALL");
  const [expanded, setExpanded] = useState<string | null>(null);

  const shown = useMemo(
    () => (status === "ALL" ? rows : rows.filter((r) => r.status === status)),
    [rows, status],
  );

  return (
    <section className={`${CARD_BARE} overflow-hidden`}>
      <header className="flex flex-wrap items-start justify-between gap-3 px-5 pt-5 sm:px-6 sm:pt-6">
        <div>
          <h4 className="text-lg font-bold text-foreground">OT submissions</h4>
          <p className="text-xs text-muted-foreground">
            All overtime requests across the organisation.
          </p>
        </div>

        <select
          value={status}
          onChange={(event) => setStatus(event.target.value as OtStatusFilter)}
          aria-label="Status"
          className="h-11 rounded-2xl border border-border/70 bg-card px-3 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          {OT_STATUSES.map((value) => (
            <option key={value} value={value}>
              {value === "ALL" ? "All statuses" : overtimeStatusLabels[value]}
            </option>
          ))}
        </select>
      </header>

      {/* Two numbers, because "showing 4" alone hides how much was filtered
          away — the gap between them is the point. */}
      <p className="px-5 pt-3 text-xs text-muted-foreground sm:px-6">
        Showing {shown.length} of {rows.length} submissions
      </p>

      {shown.length === 0 ? (
        <EmptyRow>
          {rows.length === 0
            ? "No overtime requests in this range."
            : "No submissions match this status."}
        </EmptyRow>
      ) : (
        <div className="mt-4 overflow-x-auto border-t border-border/60">
          <table className="w-full min-w-[960px] text-sm">
            <thead>
              <tr className="border-b border-border/60">
                {["Employee", "Date", "Time range", "Duration", "Reviewed by", "Status"].map((h) => (
                  <th key={h} className={TH}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {shown.map((r) => {
                const attachments = [r.beforePhotoUrl, r.afterPhotoUrl].filter(Boolean).length;
                const open = expanded === r.id;
                return (
                  <Fragment key={r.id}>
                    <tr className="border-b border-border/60">
                      <td className="max-w-[280px] p-4 pl-6 align-top">
                        <p className="font-semibold uppercase text-foreground">{name(r.employeeId)}</p>
                        <p className="truncate text-xs text-muted-foreground" title={otSubtitle(r, projectNames)}>
                          {otSubtitle(r, projectNames)}
                        </p>
                      </td>
                      <td className="p-4 align-top tabular-nums">{dateLabel(r.workDate.slice(0, 10))}</td>
                      <td className="p-4 align-top tabular-nums">
                        {timeLabel(r.startAt)} – {timeLabel(r.endAt)}
                      </td>
                      <td className="p-4 align-top tabular-nums">{formatMinutes(r.requestedMinutes)}</td>
                      <td className="p-4 align-top">
                        {/* Never a guessed name. A decided row with no reviewer
                            is either one the rules resolved with nobody to ask,
                            or one decided before this column existed — and the
                            two are indistinguishable in the data, so the cell
                            says what is true of both rather than picking. */}
                        {r.reviewerId ? (
                          <>
                            <p className="font-semibold uppercase text-foreground">{name(r.reviewerId)}</p>
                            {r.decidedAt ? (
                              <p className="text-xs text-muted-foreground">
                                {new Date(r.decidedAt).toLocaleDateString()}
                              </p>
                            ) : null}
                          </>
                        ) : (
                          <span className="text-xs italic text-muted-foreground">
                            {r.decidedAt ? "Not recorded" : "—"}
                          </span>
                        )}
                      </td>
                      <td className="p-4 pr-6 align-top">
                        <div className="flex items-center justify-end gap-2">
                          <OvertimeStatusBadge status={r.status} />
                          {attachments > 0 ? (
                            <button
                              type="button"
                              onClick={() => setExpanded(open ? null : r.id)}
                              aria-expanded={open}
                              aria-label={`${open ? "Hide" : "Show"} attachments for ${name(r.employeeId)}`}
                              className="inline-flex items-center gap-1 rounded-full border border-border/70 px-2 py-1 text-xs font-semibold text-muted-foreground hover:text-foreground"
                            >
                              <FileText className="h-3 w-3" aria-hidden />
                              {attachments}
                              {open ? (
                                <ChevronUp className="h-3 w-3" aria-hidden />
                              ) : (
                                <ChevronDown className="h-3 w-3" aria-hidden />
                              )}
                            </button>
                          ) : null}
                        </div>
                      </td>
                    </tr>

                    {open ? (
                      <tr className="border-b border-border/60 bg-muted/30">
                        <td colSpan={6} className="px-6 py-4">
                          <div className="grid gap-6 sm:grid-cols-2">
                            <PhotoSlot label="Before (justification)" url={r.beforePhotoUrl} />
                            <PhotoSlot label="After (evidence)" url={r.afterPhotoUrl ?? null} />
                          </div>
                          {r.reviewNotes ? (
                            <p className="mt-3 text-xs text-muted-foreground">
                              <span className="font-semibold text-foreground">Review notes:</span>{" "}
                              {r.reviewNotes}
                            </p>
                          ) : null}
                        </td>
                      </tr>
                    ) : null}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function otSubtitle(r: OvertimeRequest, projectNames: Map<string, string>): string {
  const project = r.projectId ? projectNames.get(r.projectId) : null;
  return [project, r.reason].filter(Boolean).join(" · ") || "—";
}

// One of the two OT photos. The photos are behind auth, so they open through
// the API client rather than as a plain href — a bare src would 401.
function PhotoSlot({ label, url }: { label: string; url: string | null }) {
  return (
    <div className="min-w-0">
      <p className="text-[10px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">
        {label}
      </p>
      {url ? (
        <button
          type="button"
          onClick={() => void openOvertimePhoto(url)}
          className="mt-1 inline-flex items-center gap-1 text-xs font-semibold text-primary hover:underline"
        >
          <FileText className="h-3 w-3 shrink-0" aria-hidden />
          <span className="max-w-[220px] truncate">{url.split("/").pop()}</span>
        </button>
      ) : (
        <p className="mt-1 text-xs text-muted-foreground">None uploaded.</p>
      )}
    </div>
  );
}

// ---- Shifts ----

// The working patterns everything else on this screen is measured against: a
// late clock-in is only late relative to one of these, so they belong next to
// the reports that use them.
function ShiftsTab({
  rows,
  projects,
  projectNames,
  onCreated,
}: {
  rows: Shift[];
  projects: FilterOption[];
  projectNames: Map<string, string>;
  onCreated: () => void;
}) {
  // Scoped here rather than in the shared bar above: that one searches
  // employees and filters by team, and neither applies to a standing pattern.
  const [projectId, setProjectId] = useState<string>(ALL_FILTER);
  const [adding, setAdding] = useState(false);

  const shown = useMemo(
    () => (projectId === ALL_FILTER ? rows : rows.filter((s) => s.projectId === projectId)),
    [rows, projectId],
  );

  return (
    <div className="space-y-4">
      {/* No "Shifts" heading — the active tab above says it. The explanation
          stays: how the default interacts with a per-employee assignment, and
          that late detection reads from it, is not guessable from the table. */}
      <p className="max-w-3xl text-sm text-muted-foreground">
        One project can have several named shifts (Day 8am–5pm, Night 10pm–7am).
        Mark one as the project default; an employee can still be assigned a
        different one. Late detection and expected daily hours both read from
        whichever shift applies to the employee.
      </p>

      <section className={`${CARD_BARE} flex flex-col gap-3 p-5 sm:flex-row sm:items-end sm:p-6`}>
        <div className="min-w-0 flex-1 space-y-1.5">
          <label
            htmlFor="shifts-project"
            className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground"
          >
            Project
          </label>
          <select
            id="shifts-project"
            value={projectId}
            onChange={(event) => setProjectId(event.target.value)}
            className="h-11 w-full rounded-2xl border border-border/70 bg-card px-3 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            <option value={ALL_FILTER}>All projects</option>
            {projects.map((project) => (
              <option key={project.id} value={project.id}>{project.name}</option>
            ))}
          </select>
        </div>

        <button
          type="button"
          onClick={() => setAdding(true)}
          className="inline-flex h-11 shrink-0 items-center gap-1.5 rounded-2xl bg-primary px-4 text-sm font-bold text-primary-foreground"
        >
          <Plus className="h-4 w-4" aria-hidden />
          Add shift
        </button>
      </section>

      {shown.length === 0 ? (
        <section className={CARD_BARE}>
          <EmptyRow>
            {rows.length === 0
              ? "No shifts defined yet. Until one exists, attendance has no expected hours to compare against."
              : "No shifts match this filter."}
          </EmptyRow>
        </section>
      ) : (
        <section className={`${CARD_BARE} overflow-hidden`}>
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
                {shown.map((shift) => (
                  <tr key={shift.id} className="border-b border-border/60 last:border-0">
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
      )}

      {adding ? (
        <ShiftEditor
          projects={projects}
          defaultProjectId={projectId === ALL_FILTER ? undefined : projectId}
          onClose={() => setAdding(false)}
          onCreated={() => {
            setAdding(false);
            onCreated();
          }}
        />
      ) : null}
    </div>
  );
}
