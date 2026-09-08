import { useCallback, useEffect, useMemo, useState } from "react";
import {
  getApprovalAudit,
  getAttendanceHistory,
  getOrgHoursSummary,
  getSelfieStorage,
  getSupervisorPerformance,
  type AdminAttendanceFilter,
  type ApprovalAuditEntry,
  type AttendanceRecord,
  type OrgHoursSummary,
  type SelfieStorage,
  type SupervisorPerformance,
} from "@/features/attendance/api";
import { AttendanceStatusBadge } from "@/features/attendance/components/AttendanceStatusBadge";
import { OvertimeStatusBadge } from "@/features/overtime/components/OvertimeStatusBadge";
import { getEmployees, type Employee } from "@/features/employees/api";
import { getProjects } from "@/features/settings/api";
import { getTeams } from "@/features/teams/api";
import { getAllOvertime, type OvertimeRequest } from "@/features/overtime/api";
import { getShifts, type Shift } from "@/features/shifts/api";
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
};

const REPORT_TABS: { key: ReportTab; label: string }[] = [
  { key: "today", label: "Today" },
  { key: "analytics", label: "Analytics" },
  { key: "performance", label: "Performance" },
  { key: "history", label: "History" },
];

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
      if (section === "overtime") setOvertime(await getAllOvertime());
      if (section === "shifts") setShifts(await getShifts());
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
  const todayRows = useMemo(() => {
    const term = (filter.q ?? "").trim().toLowerCase();
    return records
      .filter((r) => r.date === today)
      .filter((r) => !filter.projectId || r.projectId === filter.projectId)
      .filter((r) =>
        !term
          ? true
          : `${name(r.employeeId)} ${emails.get(r.employeeId) ?? ""}`.toLowerCase().includes(term),
      )
      .sort((a, b) => (b.timeIn ?? "").localeCompare(a.timeIn ?? ""));
  }, [records, filter, emails, today, name]);

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

  const teamOf = useMemo(() => {
    const map = new Map<string, string>();
    for (const team of teamIndex) {
      for (const employeeId of team.members) {
        // First team wins. Someone on two teams gets one label rather than a
        // joined string that breaks the column width.
        if (!map.has(employeeId)) map.set(employeeId, team.name);
      }
    }
    return map;
  }, [teamIndex]);

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

  const employeeRows = useMemo(() => {
    const term = (filter.q ?? "").trim().toLowerCase();
    const inRange = records.filter((r) => r.date >= from && r.date <= to);

    return roster
      .filter((e) => !scopedIds || scopedIds.has(e.id))
      .filter((e) =>
        !term
          ? true
          : `${e.name} ${e.email} ${e.employeeNumber ?? ""} ${e.jobTitle ?? ""}`
              .toLowerCase()
              .includes(term),
      )
      .map((employee) => {
        const own = inRange.filter((r) => r.employeeId === employee.id);
        const days = new Set(own.map((r) => r.date));
        const last = own
          .map((r) => r.timeIn)
          .filter((t): t is string => Boolean(t))
          .sort()
          .at(-1) ?? null;

        return {
          employee,
          team: teamOf.get(employee.id) ?? null,
          daysPresent: days.size,
          minutes: own.reduce((sum, r) => sum + (r.durationMin ?? 0), 0),
          lastSeen: last,
        };
      })
      .sort((a, b) => b.daysPresent - a.daysPresent || a.employee.name.localeCompare(b.employee.name));
  }, [roster, records, from, to, filter.q, scopedIds, teamOf]);

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

  const showFilters = section !== "shifts";
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
        onChange={setSection}
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
        <EmployeesTab rows={employeeRows} />
      ) : section === "overtime" ? (
        <OvertimeTab rows={overtimeRows} name={name} projectNames={projectNames} />
      ) : section === "shifts" ? (
        <ShiftsTab rows={shifts} projectNames={projectNames} />
      ) : tab === "today" ? (
        <TodayTab rows={todayRows} name={name} projectNames={projectNames} />
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

function TodayTab({
  rows,
  name,
  projectNames,
}: {
  rows: AttendanceRecord[];
  name: (id: string) => string;
  projectNames: Map<string, string>;
}) {
  if (rows.length === 0) {
    return (
      <section className={CARD_BARE}>
        <EmptyRow>Nobody has clocked in today under these filters.</EmptyRow>
      </section>
    );
  }

  return (
    <section className={CARD_BARE}>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[760px] text-sm">
          <thead>
            <tr className="border-b border-border/60">
              {["Employee", "Project", "Clock in", "Clock out", "Worked", "Status"].map((h) => (
                <th key={h} className={TH}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} className="border-b border-border/60">
                <td className="p-4 pl-6 font-semibold text-foreground">{name(r.employeeId)}</td>
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
    </section>
  );
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

// The roster as attendance sees it: who is on it, and how much of the selected
// range each person actually turned up for. Zero days present is the row worth
// having — an employee nobody has noticed is missing.
function EmployeesTab({
  rows,
}: {
  rows: {
    employee: Employee;
    team: string | null;
    daysPresent: number;
    minutes: number;
    lastSeen: string | null;
  }[];
}) {
  if (rows.length === 0) {
    return (
      <section className={CARD_BARE}>
        <EmptyRow>No employees match these filters.</EmptyRow>
      </section>
    );
  }

  return (
    <section className={CARD_BARE}>
      <div className="overflow-x-auto">
        <table className="w-full min-w-[860px] text-sm">
          <thead>
            <tr className="border-b border-border/60">
              {["Employee", "Team", "Job title", "Days present", "Worked", "Last clock in"].map((h) => (
                <th key={h} className={TH}>{h}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.employee.id} className="border-b border-border/60">
                <td className="p-4 pl-6">
                  <p className="font-semibold text-foreground">{row.employee.name}</p>
                  <p className="text-xs text-muted-foreground">{row.employee.email}</p>
                </td>
                <td className="p-4 text-muted-foreground">{row.team ?? "—"}</td>
                <td className="p-4 text-muted-foreground">{row.employee.jobTitle ?? "—"}</td>
                <td className="p-4 tabular-nums">
                  {row.daysPresent === 0 ? (
                    <span className="font-semibold text-warning-foreground">None</span>
                  ) : (
                    row.daysPresent
                  )}
                </td>
                <td className="p-4 tabular-nums">{formatMinutes(row.minutes)}</td>
                <td className="p-4 pr-6 tabular-nums text-muted-foreground">
                  {row.lastSeen ? new Date(row.lastSeen).toLocaleDateString() : "—"}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
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
