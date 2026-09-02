import { useEffect, useMemo, useState } from "react";
import {
  AlertTriangle,
  ArrowRight,
  CalendarClock,
  ChevronDown,
  CheckCircle2,
  ClipboardCheck,
  Clock3,
  MapPin,
  Plus,
  Users,
} from "lucide-react";
import {
  getAttendanceHistory,
  getMyHoursSummary,
  getTodayAttendance,
  type HoursBuckets,
  type AttendanceRecord,
} from "../api";
import { AttendanceApprovals } from "./AttendanceApprovals";
import { OvertimeView } from "@/features/overtime/components/OvertimeView";
import { getOrganization, getProjects, type Project } from "@/features/settings/api";
import { formatDistance } from "@/shared/lib/geolocation";

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

function getMonthSummary(records: AttendanceRecord[]) {
  const totalMin = records.reduce((sum, r) => sum + (r.durationMin ?? 0), 0);
  return {
    totalMin,
    onTime: records.filter((r) => r.status === "ON_TIME" || r.status === "CLOCKED_OUT").length,
    late: records.filter((r) => r.status === "LATE").length,
    missing: records.filter((r) => r.status === "MISSING").length,
  };
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

function GeoChip({ distance, radius }: { distance: number | null; radius: number }) {
  if (distance == null) return null;
  const onSite = distance <= radius;
  return (
    <span
      className={`inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider ${
        onSite ? "bg-secondary text-secondary-foreground" : "bg-amber-100 text-amber-800"
      }`}
    >
      <MapPin className="h-3 w-3" />
      {onSite ? "On-site" : "Off-site"} {formatDistance(distance)}
    </span>
  );
}

function StatusChip({ status }: { status: AttendanceRecord["status"] }) {
  if (status === "ON_TIME" || status === "CLOCKED_OUT") {
    return (
      <span className="inline-flex rounded-full bg-secondary px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-secondary-foreground">
        On time
      </span>
    );
  }
  if (status === "LATE") {
    return (
      <span className="inline-flex rounded-full bg-amber-100 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-amber-800">
        Late
      </span>
    );
  }
  if (status === "ON_LEAVE") {
    return (
      <span className="inline-flex rounded-full bg-primary/10 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-primary">
        On leave
      </span>
    );
  }
  if (status === "MISSING") {
    return (
      <span className="inline-flex rounded-full bg-destructive/10 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-destructive">
        Missing
      </span>
    );
  }
  return (
    <span className="inline-flex rounded-full bg-muted px-2.5 py-1 text-[10px] font-bold uppercase tracking-wider text-muted-foreground">
      In progress
    </span>
  );
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

  useEffect(() => {
    Promise.all([getTodayAttendance(), getAttendanceHistory(), getProjects(), getOrganization()])
      .then(([t, h, p, org]) => {
        setToday(t);
        setHistory(h);
        setProjects(p.filter((x) => !x.isArchived));
        setRadius(org.geofenceRadiusMeters);
        setOrgHours({ start: org.workingHoursStart, end: org.workingHoursEnd });
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

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

  const historyByMonth = useMemo(() => {
    const cutoff = new Date(now);
    cutoff.setDate(cutoff.getDate() - 30);
    const grouped = new Map<string, AttendanceRecord[]>();
    for (const record of history.filter((r) => new Date(`${r.date}T00:00:00`) >= cutoff)) {
      const key = monthKey(record.date);
      grouped.set(key, [...(grouped.get(key) ?? []), record]);
    }
    return Array.from(grouped.entries());
  }, [history, now]);

  if (sub === "att-history") {
    return (
      <HistoryView
        loading={loading}
        error={error}
        historyByMonth={historyByMonth}
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
    return <EmptyAttendanceSection kind="team" />;
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
          {today?.lateByMin != null ? (
            <span className="rounded-full bg-amber-100 px-3 py-1.5 text-[10px] font-bold uppercase tracking-wider text-amber-800">
              Late {today.lateByMin}m
            </span>
          ) : (
            <span className="rounded-full bg-secondary px-3 py-1.5 text-[10px] font-bold uppercase tracking-wider text-secondary-foreground">
              {clockedIn ? "On the clock" : clockedOut ? "Completed" : "Not started"}
            </span>
          )}
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
          <div className={`${CARD} p-4 text-sm text-muted-foreground`}>Loading attendance...</div>
        ) : weekRecords.length === 0 ? (
          <div className={`${CARD} p-5 text-center text-sm text-muted-foreground`}>
            No attendance records yet this week.
          </div>
        ) : (
          <div className="space-y-2">
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

function TodayEvents({ today, radius }: { today: AttendanceRecord | null; radius: number }) {
  const [expandedEvent, setExpandedEvent] = useState<string | null>(null);
  const events = [
    today?.timeIn
      ? {
          id: "in",
          label: "Clock in",
          at: today.timeIn,
          tone: "bg-secondary text-secondary-foreground",
          distance: today.clockInDistanceMeters,
        }
      : null,
    today?.timeOut
      ? {
          id: "out",
          label: "Clock out",
          at: today.timeOut,
          tone: "bg-muted text-muted-foreground",
          distance: today.clockOutDistanceMeters,
        }
      : null,
  ].filter(Boolean) as Array<{ id: string; label: string; at: string; tone: string; distance: number | null }>;

  return (
    <div className="mt-5">
      <p className="mb-3 text-sm font-bold text-foreground">Today's events</p>
      {events.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-border/70 bg-surface-low/50 p-5 text-center">
          <Clock3 className="mx-auto h-5 w-5 text-muted-foreground" />
          <p className="mt-2 text-sm font-medium text-foreground">No events yet today.</p>
          <p className="mt-1 text-xs text-muted-foreground">Your clock activity appears here.</p>
        </div>
      ) : (
        <div className="space-y-2">
          {events.map((event) => (
            <div
              key={event.id}
              className="border-b border-border/50 py-2 last:border-0"
            >
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
                  aria-label={`${expandedEvent === event.id ? "Hide" : "Show"} ${event.label} location`}
                  aria-expanded={expandedEvent === event.id}
                  onClick={() => setExpandedEvent((current) => (current === event.id ? null : event.id))}
                  className="ml-auto grid h-8 w-8 shrink-0 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground transition hover:text-foreground"
                >
                  <ChevronDown
                    className={`h-4 w-4 transition-transform ${expandedEvent === event.id ? "rotate-180" : ""}`}
                  />
                </button>
              </div>
              {expandedEvent === event.id ? <EventLocationDetails event={event} radius={radius} today={today} /> : null}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function EventLocationDetails({
  event,
  radius,
  today,
}: {
  event: { id: string; label: string; at: string; tone: string; distance: number | null };
  radius: number;
  today: AttendanceRecord | null;
}) {
  const distance = event.distance;
  const captured = distance != null;
  const offSite = captured && distance > radius;
  const location =
    event.id === "in"
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
  const timeLabel =
    record.timeIn && record.timeOut
      ? `${fmtTime(record.timeIn)} - ${fmtTime(record.timeOut)}`
      : record.timeIn
        ? fmtTime(record.timeIn)
        : "-";
  const placeLabel = proj ?? record.location;

  return (
    <article className="flex items-center gap-3 rounded-2xl border border-border/60 bg-card px-4 py-3 shadow-ambient">
      {statusIcon(record)}
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-semibold text-foreground">{shortDate(record.date)}</p>
        <p className="text-xs text-muted-foreground">
          {timeLabel}
          {placeLabel ? ` - ${placeLabel}` : ""}
        </p>
        {showBadges ? (
          <div className="mt-1 flex flex-wrap gap-1.5">
            <StatusChip status={record.status} />
            <GeoChip distance={record.clockInDistanceMeters} radius={radius} />
            <GeoChip distance={record.clockOutDistanceMeters} radius={radius} />
          </div>
        ) : null}
      </div>
      <span className="shrink-0 text-xs font-bold tabular-nums text-muted-foreground">
        {fmtDuration(record.durationMin)}
      </span>
    </article>
  );
}

function HistoryView({
  loading,
  error,
  historyByMonth,
  projects,
  radius,
}: {
  loading: boolean;
  error: string | null;
  historyByMonth: Array<[string, AttendanceRecord[]]>;
  projects: Project[];
  radius: number;
}) {
  if (loading) {
    return <section className={`${CARD} p-6 text-sm text-muted-foreground`}>Loading attendance history...</section>;
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
        <p className="mt-2 text-sm font-medium text-foreground">No attendance records in the last 30 days.</p>
      </section>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Last 30 days</p>
        <h2 className="mt-0.5 text-xl font-bold text-foreground">Attendance history</h2>
      </div>

      {historyByMonth.map(([month, items]) => {
        const sum = getMonthSummary(items);
        return (
          <section key={month} className="space-y-3">
            <div className="flex items-baseline justify-between">
              <h3 className="text-lg font-bold text-foreground">{month}</h3>
              <span className="text-xs text-muted-foreground">{items.length} days</span>
            </div>

            <div className={`${CARD} bg-secondary/40 p-4`}>
              <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
                <SummaryMetric label="Total worked" value={`${Math.floor(sum.totalMin / 60)}h ${sum.totalMin % 60}m`} />
                <SummaryMetric label="On time" value={String(sum.onTime)} tone="text-success" />
                <SummaryMetric label="Late" value={String(sum.late)} tone="text-tertiary" />
                <SummaryMetric label="Missing" value={String(sum.missing)} tone="text-destructive" />
              </div>
            </div>

            <div className={`${CARD} p-2`}>
              <div className="space-y-1">
                {items.map((record) => (
                  <ShiftRow key={record.id} record={record} projects={projects} radius={radius} />
                ))}
              </div>
            </div>
          </section>
        );
      })}
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

function EmptyAttendanceSection({ kind }: { kind: "overtime" | "approvals" | "team" }) {
  const config = {
    overtime: {
      title: "Overtime",
      kicker: "Submissions",
      body: "No overtime submissions yet.",
      icon: Plus,
      count: "0 submissions",
      button: "Submit overtime",
    },
    approvals: {
      title: "Approvals queue",
      kicker: "Approvals",
      body: "No attendance approvals waiting for review.",
      icon: ClipboardCheck,
      count: "0 pending",
      button: "Review all",
    },
    team: {
      title: "Your team",
      kicker: "Real-time",
      body: "No team attendance activity to show yet.",
      icon: Users,
      count: "0 present",
      button: "Refresh team",
    },
  }[kind];
  const Icon = config.icon;

  return (
    <div className="space-y-4">
      <div className="flex items-baseline justify-between gap-3">
        <div>
          <p className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">{config.kicker}</p>
          <h2 className="mt-0.5 text-xl font-bold text-foreground">{config.title}</h2>
        </div>
        <span className="text-xs text-muted-foreground">{config.count}</span>
      </div>

      <section className={`${CARD} border-dashed bg-surface-low p-8 text-center`}>
        <Icon className="mx-auto h-6 w-6 text-primary" />
        <p className="mt-3 text-sm font-medium text-foreground">{config.body}</p>
        <button
          type="button"
          disabled
          className="mt-4 inline-flex h-10 items-center justify-center rounded-2xl bg-primary px-4 text-xs font-bold text-primary-foreground opacity-50"
        >
          {config.button}
        </button>
      </section>
    </div>
  );
}
