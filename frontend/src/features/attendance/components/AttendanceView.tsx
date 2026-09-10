import { useEffect, useMemo, useState } from "react";
import {
  AlertTriangle,
  ArrowRight,
  CalendarClock,
  ChevronDown,
  CheckCircle2,
  Clock3,
  MapPin,
} from "lucide-react";
import {
  getAttendanceHistory,
  getMyHoursSummary,
  getTodayAttendance,
  type HoursBuckets,
  type AttendanceRecord,
  type AttendanceSession,
} from "../api";
import { StatusFilterTabs } from "@/shared/components/StatusFilterTabs";
import { AttendanceApprovals } from "./AttendanceApprovals";
import { TeamPresence } from "./TeamPresence";
import { OvertimeView } from "@/features/overtime/components/OvertimeView";
import { getOrganization, getProjects, type Project } from "@/features/settings/api";
import { formatDistance } from "@/shared/lib/geolocation";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonCards, SkeletonPanel } from "@/shared/components/Skeleton";

const TZ = "Asia/Kuala_Lumpur";
const CARD = "rounded-2xl border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";

function fmtTime(iso: string | null) {
  if (!iso) return "-";
  return new Intl.DateTimeFormat("en-US", {
    hour: "2-digit",
    minute: "2-digit",
    hour12: true,
    timeZone: TZ,
  }).format(new Date(iso));
}

function fmtDuration(min: number | null) {
  if (min == null) return "-";
  const h = Math.floor(min / 60);
  const m = min % 60;
  return h > 0 ? `${h}h ${m}m` : `${m}m`;
}

function monthKey(ymd: string) {
  const [y, m, day] = ymd.split("-").map(Number);
  return new Intl.DateTimeFormat("en-US", {
    month: "long",
    year: "numeric",
    timeZone: TZ,
  }).format(new Date(y, (m ?? 1) - 1, day ?? 1));
}

function shortDate(ymd: string) {
  const [y, m, day] = ymd.split("-").map(Number);
  return new Intl.DateTimeFormat("en-MY", {
    weekday: "short",
    day: "2-digit",
    month: "short",
  }).format(new Date(y, (m ?? 1) - 1, day ?? 1));
}

function formatHoursValue(minutes: number) {
  const hours = Math.max(0, minutes) / 60;
  return Number.isInteger(hours) ? String(hours) : (Math.round(hours * 10) / 10).toString();
}

function statusIcon(record: AttendanceRecord) {
  if (record.status === "MISSING") {
    return <AlertTriangle className="h-5 w-5 shrink-0 text-destructive" />;
  }
  return (
    <CheckCircle2
      className={`h-5 w-5 shrink-0 ${
        record.status === "ON_TIME" || record.status === "CLOCKED_OUT"
          ? "text-success"
          : "text-tertiary"
      }`}
    />
  );
}

function projectName(projects: Project[], id: string | null) {
  if (!id) return null;
  return projects.find((p) => p.id === id)?.name ?? "Project";
}

// Day counts only. The MINUTES deliberately aren't here: summing durationMin
// gives raw clock time, which ignores the shift cap, the break deduction and
// the overtime split — the exact apples-to-oranges figure the dashboard cards
// were fixed for. Counted hours come from /hours-summary/me instead.
function getMonthSummary(records: AttendanceRecord[]) {
  return {
    onTime: records.filter(
      (r) => (r.status === "ON_TIME" || r.status === "CLOCKED_OUT") && r.lateByMin == null,
    ).length,
    late: records.filter((r) => r.lateByMin != null || r.status === "LATE").length,
    missing: records.filter((r) => r.status === "MISSING").length,
  };
}

// First and last day of the month a record falls in, as the API's yyyy-MM-dd.
function monthRange(ymd: string) {
  const [y, m] = ymd.split("-").map(Number);
  const last = new Date(y, m, 0).getDate();
  const pad = (n: number) => String(n).padStart(2, "0");
  return { from: `${y}-${pad(m)}-01`, to: `${y}-${pad(m)}-${pad(last)}` };
}

function dateKey(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}

function weekStart(date: Date) {
  const start = new Date(date);
  const weekday = start.getDay();
  const mondayOffset = weekday === 0 ? -6 : 1 - weekday;
  start.setDate(start.getDate() + mondayOffset);
  start.setHours(0, 0, 0, 0);
  return start;
}

function buildWeekBreakdown(records: AttendanceRecord[], now: Date) {
  const monday = weekStart(now);
  return Array.from({ length: 5 }, (_, index) => {
    const day = new Date(monday);
    day.setDate(monday.getDate() + index);
    const key = dateKey(day);
    const record = records.find((r) => r.date === key);
    return {
      key,
      label: new Intl.DateTimeFormat("en-US", { weekday: "long" }).format(day),
      shortLabel: new Intl.DateTimeFormat("en-US", { weekday: "short" }).format(day),
      actualMin: record?.durationMin ?? 0,
      status: record?.status ?? null,
    };
  });
}

function formatDateRange(start: Date, end: Date) {
  const sameMonth = start.getMonth() === end.getMonth();
  const startLabel = new Intl.DateTimeFormat("en-US", {
    day: "numeric",
    month: sameMonth ? undefined : "short",
  }).format(start);
  const endLabel = new Intl.DateTimeFormat("en-US", { day: "numeric", month: "short" }).format(end);
  return `${startLabel} - ${endLabel}`;
}

function buildMonthBreakdown(records: AttendanceRecord[], now: Date) {
  const monthStart = new Date(now.getFullYear(), now.getMonth(), 1);
  const today = new Date(now);
  today.setHours(0, 0, 0, 0);

  const weeks: MonthBreakdownWeek[] = [];
  let cursor = weekStart(monthStart);

  while (cursor <= today) {
    const rangeStart = new Date(Math.max(cursor.getTime(), monthStart.getTime()));
    const weekEnd = new Date(cursor);
    weekEnd.setDate(cursor.getDate() + 6);
    const rangeEnd = new Date(Math.min(weekEnd.getTime(), today.getTime()));

    const startKey = dateKey(rangeStart);
    const endKey = dateKey(rangeEnd);
    const actualMin = records
      .filter((record) => record.date >= startKey && record.date <= endKey)
      .reduce((sum, record) => sum + (record.durationMin ?? 0), 0);

    if (actualMin > 0) {
      weeks.push({
        key: `${startKey}-${endKey}`,
        label: `Week ${weeks.length + 1}`,
        range: formatDateRange(rangeStart, rangeEnd),
        actualMin,
      });
    }

    cursor.setDate(cursor.getDate() + 7);
  }

  return weeks;
}

// One chip for the whole day, and only when something was off.
//
// This used to render per clock event, so an ordinary day carried "On-site 3m"
// AND "On-site 5m" — a good day described twice, wrapping onto a second line to
// say nothing happened. Silence is the better signal: a row with no chip is a
// row that behaved.
// History periods. Defaults to the current month rather than everything: "what
// did I work this month" is the question people actually arrive with, and a
// month of rows fits on a screen where a year does not.
const HISTORY_PAGE_SIZE = 10;

type HistoryPeriod = "THIS_MONTH" | "ALL";

// Two options, not four. Four made the tab bar wrap its labels onto two lines
// on a phone, and "last month" / "last 3 months" are answerable from All with a
// scroll — they weren't worth the width.
const historyPeriods = ["ALL"] as const;

const historyPeriodLabels: Partial<Record<HistoryPeriod, string>> = {
  ALL: "All",
};

function inPeriod(date: string, period: HistoryPeriod, now: Date) {
  if (period === "ALL") return true;
  const d = new Date(`${date}T00:00:00`);
  const startOfThisMonth = new Date(now.getFullYear(), now.getMonth(), 1);

  return d >= startOfThisMonth;
}

// A day worth looking at: late, absent, still open, or clocked from outside the
// geofence. With thirty shifts on screen this is the difference between reading
// a list and scanning one.
function isProblemDay(record: AttendanceRecord, radius: number) {
  if (record.status === "MISSING") return true;
  if (record.lateByMin != null || record.status === "LATE") return true;
  if (record.timeIn != null && record.timeOut == null) return true;
  return offSiteDistance(record, radius) != null;
}

function GeoChip({
  clockIn,
  clockOut,
  radius,
}: {
  clockIn: number | null;
  clockOut: number | null;
  radius: number;
}) {
  const distances = [clockIn, clockOut].filter((d): d is number => d != null);
  if (distances.length === 0) return null;

  // The worst end is the one worth reporting.
  const worst = Math.max(...distances);
  if (worst <= radius) return null;

  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-amber-800 dark:bg-amber-500/15 dark:text-amber-300">
      <MapPin className="h-3 w-3" />
      Off-site {formatDistance(worst)}
    </span>
  );
}

// Only the exceptions. "On time" is dropped entirely — the tick on the left of
// the row already says it, so the chip repeated itself and made a clean day look
// as busy as a problem one.
//
// Lateness carries its minutes: "Late 2h 8m" tells a supervisor whether it was
// traffic or a no-show, which a bare "Late" never did.
function StatusChip({ record }: { record: AttendanceRecord }) {
  if (record.status === "MISSING") {
    return (
      <span className="inline-flex rounded-full bg-destructive/10 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-destructive">
        Missing
      </span>
    );
  }
  if (record.status === "ON_LEAVE") {
    return (
      <span className="inline-flex rounded-full bg-primary/10 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-primary">
        On leave
      </span>
    );
  }
  if (record.timeIn && !record.timeOut) {
    return (
      <span className="inline-flex rounded-full bg-muted px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-muted-foreground">
        In progress
      </span>
    );
  }
  if (record.lateByMin != null) {
    return (
      <span className="inline-flex rounded-full bg-amber-100 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-amber-800 dark:bg-amber-500/15 dark:text-amber-300">
        Late {formatLateness(record.lateByMin)}
      </span>
    );
  }
  if (record.status === "LATE") {
    // Late but with no measurement — a row written before clock-in computed it.
    return (
      <span className="inline-flex rounded-full bg-amber-100 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-amber-800 dark:bg-amber-500/15 dark:text-amber-300">
        Late
      </span>
    );
  }
  return null;
}

export function AttendanceView({
  sub = "att-dashboard",
  onViewHistory,
}: {
  sub?: string;
  onViewHistory?: () => void;
}) {
  const [today, setToday] = useState<AttendanceRecord | null>(null);
  const [history, setHistory] = useState<AttendanceRecord[]>([]);
  const [projects, setProjects] = useState<Project[]>([]);
  const [radius, setRadius] = useState(200);
  // Org working hours. An employee with an assigned Shift is really measured
  // against that shift's times — the backend resolves shift first and only
  // falls back to these — so this header is right for anyone unshifted and
  // approximate for anyone shifted. Wiring the shift needs /shifts, which has
  // no frontend yet.
  const [orgHours, setOrgHours] = useState<{ start?: string | null; end?: string | null }>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [now] = useState(() => new Date());
  const [weekHours, setWeekHours] = useState<HoursBuckets | null>(null);
  const [monthHours, setMonthHours] = useState<HoursBuckets | null>(null);

  // The displayed week runs Mon-Fri and reaches back into the previous month,
  // so it needs its own range rather than a slice of the month's.
  const weekRange = useMemo(() => {
    const start = weekStart(now);
    const end = new Date(start);
    end.setDate(start.getDate() + 4);
    return { from: dateKey(start), to: dateKey(end), start, end };
  }, [now]);

  const monthRange = useMemo(() => {
    const start = new Date(now.getFullYear(), now.getMonth(), 1);
    return { from: dateKey(start), to: dateKey(now), start, end: now };
  }, [now]);

  // Today's record is read fresh every time. This screen's first job is
  // telling someone whether they are currently clocked in, and a cached answer
  // to that could have them clock in twice or think they already clocked out.
  useEffect(() => {
    getTodayAttendance()
      .then(setToday)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  // History, projects and the org's geofence/working hours are all cached — a
  // clock-in or clock-out invalidates /attendance*, so history can't go stale
  // behind a change the user just made.
  const historyQuery = useCachedQuery("/attendance", getAttendanceHistory);
  const projectsQuery = useCachedQuery("/projects", getProjects);
  const orgQuery = useCachedQuery("/organizations/current", getOrganization);

  useEffect(() => {
    if (historyQuery.data) setHistory(historyQuery.data);
  }, [historyQuery.data]);
  useEffect(() => {
    setProjects((projectsQuery.data ?? []).filter((x) => !x.isArchived));
  }, [projectsQuery.data]);
  useEffect(() => {
    const org = orgQuery.data;
    if (!org) return;
    setRadius(org.geofenceRadiusMeters);
    setOrgHours({ start: org.workingHoursStart, end: org.workingHoursEnd });
  }, [orgQuery.data]);

  // Totals are computed server-side so they match what payroll reads. A failure
  // here leaves the cards showing a dash rather than a wrong number.
  useEffect(() => {
    Promise.all([
      getMyHoursSummary(weekRange.from, weekRange.to),
      getMyHoursSummary(monthRange.from, monthRange.to),
    ])
      .then(([week, month]) => {
        setWeekHours(week);
        setMonthHours(month);
      })
      .catch(() => {
        setWeekHours(null);
        setMonthHours(null);
      });
  }, [weekRange, monthRange]);

  const clockedIn = today?.timeIn != null && today?.timeOut == null;
  const clockedOut = today?.timeOut != null;

  const weekRecords = useMemo(() => {
    const cutoff = new Date(now);
    cutoff.setDate(cutoff.getDate() - 7);
    return history.filter((r) => new Date(`${r.date}T00:00:00`) >= cutoff).slice(0, 5);
  }, [history, now]);

  const monthRecords = useMemo(
    () => history.filter((r) => r.date.startsWith(`${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}`)),
    [history, now],
  );

  const weekBreakdown = useMemo(() => buildWeekBreakdown(history, now), [history, now]);
  const monthBreakdown = useMemo(() => buildMonthBreakdown(monthRecords, now), [monthRecords, now]);

  if (sub === "att-history") {
    return (
      <HistoryView
        loading={loading}
        error={error}
        history={history}
        now={now}
        projects={projects}
        radius={radius}
      />
    );
  }

  if (sub === "att-overtime") {
    return <OvertimeView />;
  }

  if (sub === "att-approvals") {
    return <AttendanceApprovals />;
  }

  if (sub === "att-team") {
    return <TeamPresence />;
  }

  return (
    <div className="space-y-4 sm:space-y-6">
      <section className={`${CARD} p-4 sm:p-5`}>
        <div className="flex items-center justify-between gap-3">
          <div>
            <p className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
              Working hours
            </p>
            <p className="mt-0.5 text-lg font-extrabold text-foreground">
              {formatClockRange(orgHours.start, orgHours.end)}
            </p>
          </div>
          {/* Both, not one or the other: being late and being on the clock are
              different facts, and the old either/or hid whichever came second.
              Stacked rather than side by side — in a row the two pills squeezed
              the working-hours heading into wrapping mid-range ("09:00 AM -" /
              "06:00 PM"). Status leads; lateness qualifies it. */}
          <div className="flex shrink-0 flex-col items-end gap-1.5">
            <span
              className={`rounded-full px-3 py-1.5 text-[10px] font-bold uppercase tracking-wider ${
                clockedIn ? "bg-success/15 text-success" : "bg-muted text-muted-foreground"
              }`}
            >
              {clockedIn ? "On the clock" : clockedOut ? "Completed" : "Not started"}
            </span>
            {today?.lateByMin != null ? (
              <span className="rounded-full bg-amber-100 px-3 py-1.5 text-[10px] font-bold uppercase tracking-wider text-amber-800 dark:bg-amber-500/15 dark:text-amber-300">
                Late {formatLateness(today.lateByMin)}
              </span>
            ) : null}
          </div>
        </div>

        <TodayEvents today={today} radius={radius} />
      </section>

      <HoursProgress
        weekly={{ hours: weekHours, range: formatDateRange(weekRange.start, weekRange.end), days: weekBreakdown }}
        monthly={{ hours: monthHours, range: monthLabel(now), weeks: monthBreakdown }}
      />

      <section>
        <div className="mb-2 flex items-center justify-between gap-3">
          <p className="text-sm font-bold text-foreground">Recent shift</p>
          <button
            type="button"
            onClick={onViewHistory}
            className="inline-flex items-center gap-1 text-xs font-bold text-primary transition hover:text-primary/80"
          >
            View more
            <ArrowRight className="h-3.5 w-3.5" />
          </button>
        </div>
        {loading ? (
          <SkeletonPanel />
        ) : weekRecords.length === 0 ? (
          <div className={`${CARD} p-5 text-center text-sm text-muted-foreground`}>
            No attendance records yet this week.
          </div>
        ) : (
          <div className="overflow-hidden rounded-2xl border border-border/60 bg-card">
            {weekRecords.slice(0, 2).map((record) => (
              <ShiftRow
                key={record.id}
                record={record}
                projects={projects}
                radius={radius}
                showBadges={false}
              />
            ))}
          </div>
        )}
      </section>
    </div>
  );
}

type TodayEvent = {
  id: string;
  kind: "in" | "out";
  label: string;
  at: string;
  tone: string;
  distance: number | null;
};

function TodayEvents({ today, radius }: { today: AttendanceRecord | null; radius: number }) {
  const [expanded, setExpanded] = useState<string | null>(null);

  // Built from the day's SHIFTS, not from the record's roll-up. The roll-up
  // only knows the first clock-in and the last clock-out, so a day worked in
  // two stints quietly dropped the lunchtime clock-out and the afternoon
  // clock-in — the two events that explain the totals.
  const sessions = today?.sessions ?? [];

  // One row per SHIFT once there's more than one, rather than one row per clock
  // event: two shifts is four events, and four near-identical rows is a wall.
  // Collapsed, a shift is a single line; the clock times and their locations are
  // a tap away.
  if (sessions.length > 1) {
    return (
      <div className="mt-5">
        <p className="mb-3 text-sm font-bold text-foreground">Today&rsquo;s events</p>
        <div className="space-y-2">
          {sessions.map((session, index) => {
            const open = expanded === session.id;
            const events = shiftEvents(session, index, sessions.length);
            return (
              <div key={session.id} className="border-b border-border/50 py-2 last:border-0">
                <div className="flex items-center gap-3">
                  <span className="inline-flex w-[6.25rem] shrink-0 justify-center rounded-full bg-secondary px-2.5 py-1 text-[10px] font-bold uppercase tracking-normal text-secondary-foreground">
                    Shift {index + 1}
                  </span>
                  <span className="min-w-0 whitespace-nowrap text-sm font-semibold tabular-nums text-foreground">
                    {fmtTime(session.startedAt)} &ndash;{" "}
                    {session.endedAt ? fmtTime(session.endedAt) : "now"}
                  </span>
                  <span className="ml-auto shrink-0 text-xs font-semibold tabular-nums text-muted-foreground">
                    {session.durationMin != null ? fmtDuration(session.durationMin) : "running"}
                  </span>
                  <button
                    type="button"
                    aria-label={`${open ? "Hide" : "Show"} shift ${index + 1} details`}
                    aria-expanded={open}
                    onClick={() => setExpanded((current) => (current === session.id ? null : session.id))}
                    className="grid h-8 w-8 shrink-0 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground transition hover:text-foreground"
                  >
                    <ChevronDown className={`h-4 w-4 transition-transform ${open ? "rotate-180" : ""}`} />
                  </button>
                </div>
                {open ? (
                  <div className="mt-2 space-y-2 pl-1">
                    {events.map((event) => (
                      <div key={event.id}>
                        <div className="flex items-center gap-3">
                          <span
                            className={`inline-flex w-[6.25rem] shrink-0 justify-center rounded-full px-2.5 py-1 text-[10px] font-bold uppercase tracking-normal ${event.tone}`}
                          >
                            {event.label}
                          </span>
                          <span className="whitespace-nowrap text-sm font-semibold tabular-nums text-foreground">
                            {fmtTime(event.at)}
                          </span>
                        </div>
                        <EventLocationDetails event={event} radius={radius} today={today} />
                      </div>
                    ))}
                  </div>
                ) : null}
              </div>
            );
          })}
        </div>
      </div>
    );
  }

  // One shift (or a day predating shifts owning the clock): the events ARE the
  // list, so grouping them behind a toggle would only add a tap.
  const single = sessions[0];
  const events: TodayEvent[] = single
    ? shiftEvents(single, 0, 1)
    : ([
        today?.timeIn
          ? {
              id: "in",
              kind: "in" as const,
              label: "Clock in",
              at: today.timeIn,
              tone: "bg-secondary text-secondary-foreground",
              distance: today.clockInDistanceMeters,
            }
          : null,
        today?.timeOut
          ? {
              id: "out",
              kind: "out" as const,
              label: "Clock out",
              at: today.timeOut,
              tone: "bg-muted text-muted-foreground",
              distance: today.clockOutDistanceMeters,
            }
          : null,
      ].filter(Boolean) as TodayEvent[]);

  return (
    <div className="mt-5">
      <p className="mb-3 text-sm font-bold text-foreground">Today&rsquo;s events</p>
      {events.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-border/70 bg-surface-low/50 p-5 text-center">
          <Clock3 className="mx-auto h-5 w-5 text-muted-foreground" />
          <p className="mt-2 text-sm font-medium text-foreground">No events yet today.</p>
          <p className="mt-1 text-xs text-muted-foreground">Your clock activity appears here.</p>
        </div>
      ) : (
        <div className="space-y-2">
          {events.map((event) => (
            <div key={event.id} className="border-b border-border/50 py-2 last:border-0">
              <div className="flex items-center gap-3">
                <span
                  className={`inline-flex w-[6.25rem] shrink-0 justify-center rounded-full px-2.5 py-1 text-[10px] font-bold uppercase tracking-normal ${event.tone}`}
                >
                  {event.label}
                </span>
                <span className="min-w-0 whitespace-nowrap text-sm font-semibold tabular-nums text-foreground">
                  {fmtTime(event.at)}
                </span>
                <button
                  type="button"
                  aria-label={`${expanded === event.id ? "Hide" : "Show"} ${event.label} location`}
                  aria-expanded={expanded === event.id}
                  onClick={() => setExpanded((current) => (current === event.id ? null : event.id))}
                  className="ml-auto grid h-8 w-8 shrink-0 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground transition hover:text-foreground"
                >
                  <ChevronDown
                    className={`h-4 w-4 transition-transform ${expanded === event.id ? "rotate-180" : ""}`}
                  />
                </button>
              </div>
              {expanded === event.id ? (
                <EventLocationDetails event={event} radius={radius} today={today} />
              ) : null}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// The clock-in (and clock-out, once closed) of one shift.
function shiftEvents(session: AttendanceSession, index: number, total: number): TodayEvent[] {
  const suffix = total > 1 ? ` ${index + 1}` : "";
  const events: TodayEvent[] = [
    {
      id: `in-${session.id}`,
      kind: "in",
      label: `Clock in${suffix}`,
      at: session.startedAt,
      tone: "bg-secondary text-secondary-foreground",
      distance: session.clockInDistanceMeters,
    },
  ];
  if (session.endedAt) {
    events.push({
      id: `out-${session.id}`,
      kind: "out",
      label: `Clock out${suffix}`,
      at: session.endedAt,
      tone: "bg-muted text-muted-foreground",
      distance: session.clockOutDistanceMeters,
    });
  }
  return events;
}


function EventLocationDetails({
  event,
  radius,
  today,
}: {
  event: TodayEvent;
  radius: number;
  today: AttendanceRecord | null;
}) {
  const distance = event.distance;
  const captured = distance != null;
  const offSite = captured && distance > radius;
  const location =
    event.kind === "in"
      ? today?.location
      : today?.clockOutLat != null && today?.clockOutLng != null
        ? today?.location
        : null;

  return (
    <div className="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-muted-foreground">
      <MapPin className={`h-3.5 w-3.5 shrink-0 ${offSite ? "text-amber-700" : ""}`} />
      <span className={offSite ? "font-bold text-amber-800" : "font-bold text-foreground"}>
        {!captured ? "Location not captured" : offSite ? "Off-site" : "On-site"}
      </span>
      {captured ? <span>{formatDistance(distance)}</span> : null}
      {location ? <span>- {location}</span> : null}
      {offSite && today?.remark ? (
        <span className="basis-full pl-5 text-amber-800">{today.remark}</span>
      ) : null}
    </div>
  );
}

// How the clocked time became the counted time, as a visible subtraction.
//
// Shown as a ledger rather than a list of totals: the headline is smaller than
// the sum of the days below it, and three unrelated numbers don't explain why.
// With signs and a clocked starting point it reads itself.
function HoursBucketList({ hours }: { hours: HoursBuckets | null }) {
  if (!hours) return null;

  // totalMin is worked time — after breaks, before the shift cap.
  const clockedMin = hours.totalMin + hours.breakMin;

  const deductions = [
    { label: "Break deducted", min: hours.breakMin },
    { label: "Beyond shift", min: hours.beyondShiftMin, hint: "needs approved OT" },
  ].filter((row) => row.min > 0);

  // Time outside the schedule never fed the counted figure, so it sits apart
  // from the subtraction rather than inside it.
  const aside = [
    { label: "Rest day", min: hours.restDayMin },
    { label: "Public holiday", min: hours.publicHolidayMin },
    { label: "OT approved", min: hours.otApprovedMin },
    { label: "OT pending", min: hours.otPendingMin, hint: "awaiting approval" },
  ].filter((row) => row.min > 0);

  if (clockedMin === 0 && aside.length === 0) return null;

  return (
    <div className="space-y-1.5 pb-1">
      {clockedMin > 0 ? (
        <>
          <BucketRow label="Clocked" min={clockedMin} />
          {deductions.map((row) => (
            <BucketRow key={row.label} label={row.label} min={row.min} hint={row.hint} sign="minus" />
          ))}
          <div className="flex items-baseline justify-between gap-3 border-t border-border/50 pt-1.5">
            <span className="text-xs font-semibold text-foreground">Counted</span>
            <span className="whitespace-nowrap text-xs font-bold tabular-nums text-foreground">
              {formatHoursValue(hours.normalMin)}h
            </span>
          </div>
        </>
      ) : null}

      {aside.length > 0 ? (
        <div className="space-y-1.5 pt-1.5">
          {aside.map((row) => (
            <BucketRow key={row.label} label={row.label} min={row.min} hint={row.hint} />
          ))}
        </div>
      ) : null}
    </div>
  );
}

function BucketRow({
  label,
  min,
  hint,
  sign,
}: {
  label: string;
  min: number;
  hint?: string;
  sign?: "minus";
}) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <span className="text-xs text-muted-foreground">
        {label}
        {hint ? <span className="text-[10px] opacity-70"> &middot; {hint}</span> : null}
      </span>
      <span className="whitespace-nowrap text-xs font-semibold tabular-nums text-muted-foreground">
        {sign === "minus" ? "\u2212 " : ""}
        {formatHoursValue(min)}h
      </span>
    </div>
  );
}

// "09:00"/"18:00" as stored -> "09:00 AM - 06:00 PM". Falls back to a dash
// rather than inventing hours the org hasn't set.
function formatClockRange(start?: string | null, end?: string | null) {
  const one = (hm?: string | null) => {
    if (!hm) return null;
    const [h, m] = hm.split(":").map(Number);
    if (Number.isNaN(h) || Number.isNaN(m)) return null;
    const suffix = h < 12 ? "AM" : "PM";
    const hour12 = h % 12 === 0 ? 12 : h % 12;
    return `${String(hour12).padStart(2, "0")}:${String(m).padStart(2, "0")} ${suffix}`;
  };

  const from = one(start);
  const to = one(end);
  return from && to ? `${from} - ${to}` : "—";
}

// "Late 93m" makes the reader do the division; "Late 1h 33m" doesn't.
function formatHours(totalMin: number) {
  const h = Math.floor(totalMin / 60);
  const m = totalMin % 60;
  return m === 0 ? `${h}h` : `${h}h ${m}m`;
}

function formatLateness(minutes: number) {
  if (minutes < 60) return `${minutes}m`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m === 0 ? `${h}h` : `${h}h ${m}m`;
}

function monthLabel(now: Date) {
  return `${new Intl.DateTimeFormat("en-US", { month: "long" }).format(now)} so far`;
}

function HoursProgress({
  weekly,
  monthly,
}: {
  weekly: { hours: HoursBuckets | null; range: string; days: WeekBreakdownDay[] };
  monthly: { hours: HoursBuckets | null; range: string; weeks: MonthBreakdownWeek[] };
}) {
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      <WeeklyProgressCard entry={weekly} />
      <MonthlyProgressCard entry={monthly} />
    </div>
  );
}

type WeekBreakdownDay = {
  key: string;
  label: string;
  shortLabel: string;
  actualMin: number;
  status: AttendanceRecord["status"] | null;
};

type MonthBreakdownWeek = {
  key: string;
  label: string;
  range: string;
  actualMin: number;
};

function WeeklyProgressCard({
  entry,
}: {
  entry: { hours: HoursBuckets | null; range: string; days: WeekBreakdownDay[] };
}) {
  const [open, setOpen] = useState(false);
  const hours = entry.hours;
  const pct =
    !hours || hours.expectedMin <= 0
      ? 0
      : Math.min(100, Math.round((hours.normalMin / hours.expectedMin) * 100));

  return (
    <section className={`${CARD} p-4 sm:p-5`}>
      <button
        type="button"
        onClick={() => setOpen((current) => !current)}
        aria-expanded={open}
        className="flex w-full items-start justify-between gap-3 text-left"
      >
        <div>
          <p className="text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
            This week &middot; {entry.range}
          </p>
          <div className="mt-2 flex flex-wrap items-baseline gap-x-1.5 gap-y-0.5">
            <span className="text-3xl font-bold tabular-nums text-foreground">
              {hours ? `${formatHoursValue(hours.normalMin)}h` : "—"}
            </span>
            <span className="text-sm font-semibold text-muted-foreground">
              {hours ? `of ${formatHoursValue(hours.expectedMin)}h expected` : "hours unavailable"}
            </span>
          </div>
        </div>
        <span className="grid h-8 w-8 shrink-0 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground">
          <ChevronDown className={`h-4 w-4 transition-transform ${open ? "rotate-180" : ""}`} />
        </span>
      </button>

      <div className="mt-3 h-1.5 w-full overflow-hidden rounded-full bg-secondary/40">
        <div className="h-full bg-primary transition-all" style={{ width: `${pct}%` }} />
      </div>

      {open ? (
        <div className="mt-4 space-y-2 border-t border-border/50 pt-3">
          <HoursBucketList hours={hours} />
          <p className="pt-1 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
            Clocked each day
          </p>
          {entry.days.map((day) => (
            <div key={day.key} className="flex items-baseline justify-between gap-3">
              <span className="text-xs text-muted-foreground">
                <span className="font-bold text-foreground">{day.shortLabel}</span> {day.label}
              </span>
              <span className="whitespace-nowrap text-xs font-semibold tabular-nums text-muted-foreground">
                {day.actualMin > 0 ? `${formatHoursValue(day.actualMin)}h` : "—"}
              </span>
            </div>
          ))}
        </div>
      ) : null}
    </section>
  );
}

function MonthlyProgressCard({
  entry,
}: {
  entry: { hours: HoursBuckets | null; range: string; weeks: MonthBreakdownWeek[] };
}) {
  const [open, setOpen] = useState(false);
  const hours = entry.hours;
  const pct =
    !hours || hours.expectedMin <= 0
      ? 0
      : Math.min(100, Math.round((hours.normalMin / hours.expectedMin) * 100));
  const tone = pct >= 100 ? "bg-success" : pct >= 75 ? "bg-primary" : pct >= 40 ? "bg-amber-500" : "bg-destructive";

  return (
    <section className={`${CARD} p-4 sm:p-5`}>
      <button
        type="button"
        onClick={() => setOpen((current) => !current)}
        aria-expanded={open}
        className="flex w-full items-start justify-between gap-3 text-left"
      >
        <div>
          <p className="text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
            {entry.range}
          </p>
          <div className="mt-2 flex flex-wrap items-baseline gap-x-1.5 gap-y-0.5">
            <span className="text-3xl font-bold tabular-nums text-foreground">
              {hours ? `${formatHoursValue(hours.normalMin)}h` : "—"}
            </span>
            <span className="text-sm font-semibold text-muted-foreground">
              {hours ? `of ${formatHoursValue(hours.expectedMin)}h expected` : "hours unavailable"}
            </span>
          </div>
        </div>
        <span className="grid h-8 w-8 shrink-0 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground">
          <ChevronDown className={`h-4 w-4 transition-transform ${open ? "rotate-180" : ""}`} />
        </span>
      </button>

      <div className="mt-3 h-1.5 w-full overflow-hidden rounded-full bg-secondary/40">
        <div className={`h-full transition-all ${tone}`} style={{ width: `${pct}%` }} />
      </div>

      {open ? (
        <div className="mt-4 space-y-2 border-t border-border/50 pt-3">
          <HoursBucketList hours={hours} />
          <p className="pt-1 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
            Clocked each week
          </p>
          {entry.weeks.map((week) => (
            <div key={week.key} className="flex items-baseline justify-between gap-3">
              <span className="text-xs text-muted-foreground">
                <span className="font-bold text-foreground">{week.label}</span> {week.range}
              </span>
              <span className="whitespace-nowrap text-xs font-semibold tabular-nums text-muted-foreground">
                {formatHoursValue(week.actualMin)}h
              </span>
            </div>
          ))}
        </div>
      ) : null}
    </section>
  );
}

// Renders nothing at all on an ordinary day, so the row collapses to one line
// and the days that need attention are the only ones carrying a second.
function ShiftRowChips({ record, radius }: { record: AttendanceRecord; radius: number }) {
  const offSite = offSiteDistance(record, radius);
  const hasStatus =
    record.status === "MISSING"
    || record.status === "ON_LEAVE"
    || record.status === "LATE"
    || record.lateByMin != null
    || (record.timeIn != null && record.timeOut == null);

  // No wrapper at all when there's nothing to say, so the row keeps its single
  // line rather than carrying an empty spacer.
  if (!hasStatus && offSite == null) return null;

  return (
    <div className="mt-1 flex flex-wrap gap-1.5">
      <StatusChip record={record} />
      <GeoChip
        clockIn={record.clockInDistanceMeters}
        clockOut={record.clockOutDistanceMeters}
        radius={radius}
      />
    </div>
  );
}

// The worse of the two clock ends, or null when both were within the geofence.
function offSiteDistance(record: AttendanceRecord, radius: number) {
  const distances = [record.clockInDistanceMeters, record.clockOutDistanceMeters]
    .filter((d): d is number => d != null);
  if (distances.length === 0) return null;
  const worst = Math.max(...distances);
  return worst > radius ? worst : null;
}

function ShiftRow({
  record,
  projects,
  radius,
  showBadges = true,
}: {
  record: AttendanceRecord;
  projects: Project[];
  radius: number;
  showBadges?: boolean;
}) {
  const proj = projectName(projects, record.projectId);
  // A split day is summarised by its shift count, not by a span. "09:00 AM -
  // 06:00 PM" next to "8h clocked" reads as an error when an hour of it was
  // spent off the clock; the shifts below say where the hour went.
  const shifts = record.sessions ?? [];
  const split = shifts.length > 1;
  const timeLabel = split
    ? `${shifts.length} shifts`
    : record.timeIn && record.timeOut
      ? `${fmtTime(record.timeIn)} - ${fmtTime(record.timeOut)}`
      : record.timeIn
        ? fmtTime(record.timeIn)
        : "-";
  const placeLabel = proj ?? record.location;

  return (
    // No border, radius or shadow: these sit INSIDE the month card, so giving
    // each row its own box nested a card in a card and turned ten days into a
    // stack of ten outlines. The container rules them apart instead.
    <article className="flex items-center gap-3 border-t border-border/50 px-4 py-3 first:border-t-0">
      {statusIcon(record)}
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-semibold text-foreground">{shortDate(record.date)}</p>
        <p className="text-xs text-muted-foreground">
          {timeLabel}
          {placeLabel ? ` - ${placeLabel}` : ""}
        </p>
        {split ? (
          <ul className="mt-1 space-y-0.5">
            {shifts.map((shift, index) => (
              <li key={shift.id} className="text-[11px] text-muted-foreground">
                <span className="font-semibold text-foreground">{index + 1}.</span>{" "}
                {fmtTime(shift.startedAt)} &ndash;{" "}
                {shift.endedAt ? fmtTime(shift.endedAt) : "now"}
                {shift.durationMin != null ? ` · ${fmtDuration(shift.durationMin)}` : ""}
              </li>
            ))}
          </ul>
        ) : null}
        {showBadges ? (
          <ShiftRowChips record={record} radius={radius} />
        ) : null}
      </div>
      {/* Time on the clock, not counted hours — the endpoint returns totals for a
          range, not per day, so a per-day counted figure would mean
          re-implementing the cap and break rules here and letting the two drift.
          The month card above carries the counted number and its derivation. */}
      <span className="shrink-0 text-right text-xs font-bold tabular-nums text-muted-foreground">
        {fmtDuration(record.durationMin)}
        {record.durationMin != null ? (
          <span className="block text-[9px] font-semibold uppercase tracking-wider opacity-60">
            clocked
          </span>
        ) : null}
      </span>
    </article>
  );
}

function HistoryView({
  loading,
  error,
  history,
  now,
  projects,
  radius,
}: {
  loading: boolean;
  error: string | null;
  history: AttendanceRecord[];
  now: Date;
  projects: Project[];
  radius: number;
}) {
  const [period, setPeriod] = useState<HistoryPeriod>("THIS_MONTH");
  const [problemsOnly, setProblemsOnly] = useState(false);
  const [page, setPage] = useState(0);

  // Changing a filter with a stale page number would land on an empty page.
  useEffect(() => setPage(0), [period, problemsOnly]);

  const filtered = useMemo(
    () =>
      history.filter(
        (r) =>
          inPeriod(r.date, period, now)
          && (!problemsOnly || isProblemDay(r, radius)),
      ),
    [history, period, problemsOnly, now, radius],
  );

  const pageCount = Math.max(1, Math.ceil(filtered.length / HISTORY_PAGE_SIZE));
  const pageStart = page * HISTORY_PAGE_SIZE;
  const pageRecords = filtered.slice(pageStart, pageStart + HISTORY_PAGE_SIZE);

  // Grouped from the PAGE, but each month's summary is computed from the whole
  // filtered set below — a page holding three days of September must not report
  // September as three days.
  const historyByMonth = useMemo(() => {
    const grouped = new Map<string, AttendanceRecord[]>();
    for (const record of pageRecords) {
      const key = monthKey(record.date);
      grouped.set(key, [...(grouped.get(key) ?? []), record]);
    }
    return Array.from(grouped.entries());
  }, [pageRecords]);

  // Counted hours per visible month, straight from the server so the shift cap,
  // break deduction and overtime split are the same ones payroll would read.
  const [monthHours, setMonthHours] = useState<Record<string, HoursBuckets>>({});

  const monthTotals = useMemo(() => {
    const grouped = new Map<string, AttendanceRecord[]>();
    for (const record of filtered) {
      const key = monthKey(record.date);
      grouped.set(key, [...(grouped.get(key) ?? []), record]);
    }
    return grouped;
  }, [filtered]);

  const visibleMonths = historyByMonth.map(([month]) => month).join("|");
  useEffect(() => {
    const wanted = historyByMonth
      .map(([month, items]) => ({ month, sample: items[0]?.date }))
      .filter((m): m is { month: string; sample: string } => Boolean(m.sample));
    if (wanted.length === 0) return;

    let active = true;
    Promise.all(
      wanted.map(({ month, sample }) => {
        const { from, to } = monthRange(sample);
        return getMyHoursSummary(from, to)
          .then((hours) => [month, hours] as const)
          .catch(() => null);
      }),
    ).then((results) => {
      if (!active) return;
      setMonthHours(Object.fromEntries(results.filter(Boolean) as Array<readonly [string, HoursBuckets]>));
    });

    return () => {
      active = false;
    };
    // Keyed on the month labels rather than the array, which is a new
    // reference on every render.
  }, [visibleMonths]);

  const problemCount = useMemo(
    () => history.filter((r) => inPeriod(r.date, period, now) && isProblemDay(r, radius)).length,
    [history, period, now, radius],
  );
  if (loading) {
    return <SkeletonCards />;
  }

  if (error) {
    return (
      <section className="rounded-2xl border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
        Error: {error}
      </section>
    );
  }

  if (historyByMonth.length === 0) {
    return (
      <section className={`${CARD} p-8 text-center`}>
        <CalendarClock className="mx-auto h-6 w-6 text-muted-foreground" />
        <p className="mt-2 text-sm font-medium text-foreground">
          {problemsOnly ? "Nothing needs attention in this period." : "No attendance records in this period."}
        </p>
      </section>
    );
  }

  return (
    <div className="space-y-6">
      <div className="space-y-3">
        <StatusFilterTabs<HistoryPeriod>
          value={period}
          onChange={setPeriod}
          statuses={historyPeriods}
          labels={historyPeriodLabels}
          allValue="THIS_MONTH"
          allLabel="This month"
          ariaLabel="History period"
        />

        {/* Only offered when there's something to filter down to — a toggle that
            always yields an empty list is worse than no toggle. */}
        {problemCount > 0 ? (
          <button
            type="button"
            onClick={() => setProblemsOnly((current) => !current)}
            aria-pressed={problemsOnly}
            className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-bold transition ${
              problemsOnly
                ? "bg-amber-100 text-amber-800 dark:bg-amber-500/15 dark:text-amber-300"
                : "border border-border bg-card text-muted-foreground hover:text-foreground"
            }`}
          >
            <AlertTriangle className="h-3.5 w-3.5" />
            Needs attention
            <span className="tabular-nums opacity-70">{problemCount}</span>
          </button>
        ) : null}
      </div>

      {historyByMonth.map(([month, items]) => {
        const all = monthTotals.get(month) ?? items;
        const sum = getMonthSummary(all);
        const hours = monthHours[month];
        return (
          <section key={month} className="space-y-3">
            <div className="flex items-baseline justify-between">
              <h3 className="text-lg font-bold text-foreground">{month}</h3>
              <span className="text-xs text-muted-foreground">{all.length} days</span>
            </div>

            {/* Summary and rows share one card. Two stacked cards read as two
                unrelated things, when the totals are a header for the list
                directly under them. */}
            <div className={`${CARD} overflow-hidden`}>
              {/* No tint of its own: the summary sits on the card background,
                  same as the rows under it. The rule between them is enough of a
                  separation, and a tinted panel read as a different surface. */}
              <div className="p-4">
                <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
                  <SummaryMetric
                    label="Counted"
                    value={hours ? formatHours(hours.normalMin) : "—"}
                  />
                  <SummaryMetric label="On time" value={String(sum.onTime)} tone="text-success" />
                  <SummaryMetric label="Late" value={String(sum.late)} tone="text-tertiary" />
                  <SummaryMetric label="Missing" value={String(sum.missing)} tone="text-destructive" />
                </div>

                {/* The derivation, so "Counted" being lower than the day figures
                    below is explained rather than surprising. The rows show time on
                    the clock; this is what it became after the break came off and
                    the shift cap applied. */}
                {hours ? (
                  <p className="mt-3 border-t border-border/50 pt-2.5 text-[11px] text-muted-foreground">
                    Clocked {formatHours(hours.totalMin + hours.breakMin)}
                    {hours.breakMin > 0 ? ` · break −${formatHours(hours.breakMin)}` : ""}
                    {hours.beyondShiftMin > 0
                      ? ` · beyond shift −${formatHours(hours.beyondShiftMin)}`
                      : ""}
                    {hours.otApprovedMin > 0 ? ` · OT approved ${formatHours(hours.otApprovedMin)}` : ""}
                    {hours.otPendingMin > 0 ? ` · OT pending ${formatHours(hours.otPendingMin)}` : ""}
                    {hours.restDayMin > 0 ? ` · rest day ${formatHours(hours.restDayMin)}` : ""}
                  </p>
                ) : null}
              </div>

              <div className="border-t border-border/60">
                {items.map((record) => (
                  <ShiftRow key={record.id} record={record} projects={projects} radius={radius} />
                ))}
              </div>
            </div>
          </section>
        );
      })}

      {pageCount > 1 ? (
        <div className="flex items-center justify-between gap-3 pt-1">
          <span className="text-xs tabular-nums text-muted-foreground">
            {pageStart + 1}&ndash;{Math.min(pageStart + HISTORY_PAGE_SIZE, filtered.length)} of{" "}
            {filtered.length}
          </span>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => setPage((current) => Math.max(0, current - 1))}
              disabled={page === 0}
              className="rounded-full border border-border bg-card px-3 py-1.5 text-xs font-bold text-foreground transition hover:bg-secondary/50 disabled:opacity-40"
            >
              Previous
            </button>
            <span className="text-xs tabular-nums text-muted-foreground">
              {page + 1} / {pageCount}
            </span>
            <button
              type="button"
              onClick={() => setPage((current) => Math.min(pageCount - 1, current + 1))}
              disabled={page >= pageCount - 1}
              className="rounded-full border border-border bg-card px-3 py-1.5 text-xs font-bold text-foreground transition hover:bg-secondary/50 disabled:opacity-40"
            >
              Next
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}

function SummaryMetric({
  label,
  value,
  tone = "text-foreground",
}: {
  label: string;
  value: string;
  tone?: string;
}) {
  return (
    <div>
      <p className={`text-2xl font-extrabold tabular-nums ${tone}`}>{value}</p>
      <p className="text-[11px] text-muted-foreground">{label}</p>
    </div>
  );
}
