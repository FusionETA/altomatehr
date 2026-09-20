import { useCallback, useEffect, useMemo, useState } from "react";
import { CalendarClock, CheckSquare, Coffee, ChevronDown, FileImage, LoaderCircle, MapPin, Pencil, PencilLine, ShieldAlert, TriangleAlert, X } from "lucide-react";
import {
  bulkApproveAttendance,
  getTeamAttendanceApprovals,
  getTeamBreakApprovals,
  openAttendancePhoto,
  bulkRejectAttendance,
  pendingApprovalIds,
  type AttendanceApprovalRequest,
  type AttendanceBulkResult,
  type AttendanceRecord,
  type AttendanceSession,
} from "../api";
import { dayOffsetLabel } from "../lib/attendance-time";
import {
  approveOvertime,
  bulkApproveOvertime,
  getTeamOvertime,
  openOvertimePhoto,
  rejectOvertime,
  type OvertimeBulkResult,
  type OvertimeRequest,
} from "@/features/overtime/api";
import {
  overtimeMatchesStatus,
  overtimeStatusLabels,
  visibleOvertimeStatuses,
  type OvertimeStatusFilter,
} from "@/features/overtime/lib/overtime-status";
import { getOrganization, getProjects } from "@/features/settings/api";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { SearchInput } from "@/shared/components/SearchInput";
import { StatusFilterTabs } from "@/shared/components/StatusFilterTabs";
import {
  BulkActionBar,
  BulkResultPanel,
  SelectAllPill,
  SelectHint,
  SelectModeButton,
} from "@/shared/components/BulkApprove";
import { useBulkSelection } from "@/shared/lib/use-bulk-selection";
import { useRealtimeEvent } from "@/shared/lib/use-realtime";
import { formatDistance } from "@/shared/lib/geolocation";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonCards } from "@/shared/components/Skeleton";

const CARD = "rounded-2xl border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";
const TZ = "Asia/Kuala_Lumpur";

type DateFilter = "ALL" | "TODAY" | "LAST_7_DAYS";
type ApprovalType = "ATTENDANCE" | "OVERTIME";

function dateValue(ymd: string) {
  const [year, month, day] = ymd.split("-").map(Number);
  const date = new Date(year, (month ?? 1) - 1, day ?? 1);
  date.setHours(0, 0, 0, 0);
  return date;
}

function todayKey() {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
}

function fmtTime(value: string | null) {
  if (!value) return "-";
  return new Intl.DateTimeFormat("en-US", {
    hour: "2-digit",
    minute: "2-digit",
    hour12: true,
    timeZone: TZ,
  }).format(new Date(value));
}

function fmtDuration(minutes: number) {
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  if (h <= 0) return `${m}m`;
  return m > 0 ? `${h}h ${m}m` : `${h}h`;
}

function offSite(distance: number | null, radius: number) {
  return distance != null && distance > radius;
}

function coordsText(lat: number | null | undefined, lng: number | null | undefined) {
  if (lat == null || lng == null) return "-";
  return `${lat.toFixed(6)},${lng.toFixed(6)}`;
}

function groupKey(record: AttendanceRecord) {
  return `${record.date}:${record.employeeId}`;
}

type ApprovalGroup = {
  key: string;
  date: string;
  employeeName: string;
  employeeEmail: string | null;
  records: AttendanceRecord[];
  // Break requests for the same employee-day, so the card shows one timeline
  // rather than clock events here and breaks somewhere else.
  breaks: AttendanceApprovalRequest[];
};

// Bulk calls report per-id outcomes; show the first real reason rather than a
// bare count, since "already decided by someone else" reads very differently
// from "not yours to approve".
function firstBulkError(result: AttendanceBulkResult): string | null {
  return result.items.find((item) => !item.ok && item.error)?.error ?? null;
}

function groupApprovals(records: AttendanceRecord[], breaks: AttendanceApprovalRequest[]) {
  const groups = new Map<string, ApprovalGroup>();

  for (const record of records) {
    const key = groupKey(record);
    const employeeEmail = record.employeeEmail ?? null;
    const group =
      groups.get(key) ??
      ({
        key,
        date: record.date,
        employeeName: employeeEmail ? buildName(employeeEmail) : "Employee",
        employeeEmail,
        records: [],
        breaks: [],
      } satisfies ApprovalGroup);

    group.records.push(record);
    groups.set(key, group);
  }

  // Breaks arrive as bare approval requests; they belong to whichever day's
  // record they hang off.
  const groupByRecordId = new Map<string, ApprovalGroup>();
  for (const group of groups.values())
    for (const record of group.records) groupByRecordId.set(record.id, group);

  for (const brk of breaks) {
    const group = brk.attendanceRecordId ? groupByRecordId.get(brk.attendanceRecordId) : undefined;
    if (group) group.breaks.push(brk);
  }

  return Array.from(groups.values()).sort((a, b) => {
    const dateCompare = b.date.localeCompare(a.date);
    return dateCompare || a.employeeName.localeCompare(b.employeeName);
  });
}

// The decision endpoints take approval-REQUEST ids, never record ids: the two
// are separate GUIDs and the server resolves only the former, so passing a
// record id silently finds nothing. Every caller goes through here.
// What the reject dialog is about: a whole day, or one event on it. Both end up
// at the same endpoint with a different number of ids, so the dialog only needs
// to know which ids and what to call them.
type RejectTarget = { key: string; ids: string[]; title: string; subtitle: string };

function groupRequestIds(group: ApprovalGroup) {
  return [...group.records.flatMap(pendingApprovalIds), ...group.breaks.map((b) => b.id)];
}

// One row per pending clock event, built from the approval requests themselves
// rather than from the record's timeIn/timeOut.
//
// A record summarises a day as first-in / last-out, so rendering from it showed
// exactly two rows however many sessions the day held. A day with two shifts
// has four pending events: the header counted all four and "Approve all"
// decided all four, while only the first clock-in and the last clock-out were
// ever on screen. Worse, the clock-out row displayed the LAST time but carried
// the FIRST pending clock-out's id — so approving what read as 04:26 PM
// actually decided the 03:42 PM event.
//
// Adjustments are excluded: those carry originalEventAt and render as their own
// card inside the shift they correct (AdjustmentNotice), and counting them here
// would double them up.
type PendingEvent = {
  approvalId: string;
  sessionId: string | null;
  // Only meaningful on a clock-in: the record carries one IP verdict for the
  // day, captured at the first one.
  ipAllowed: boolean | null;
  kind: "CLOCK_IN" | "CLOCK_OUT";
  time: string;
  lateByMin: number | null;
  distance: number | null;
  photoUrl: string | null;
  location: string;
};

function pendingEventsFor(record: AttendanceRecord): PendingEvent[] {
  const sessions = [...(record.sessions ?? [])].sort((a, b) =>
    a.startedAt.localeCompare(b.startedAt),
  );

  return (record.approvals ?? [])
    .filter(
      (a) =>
        a.approvalStatus === "PENDING" &&
        !a.originalEventAt &&
        (a.kind === "CLOCK_IN" || a.kind === "CLOCK_OUT"),
    )
    .map((a) => {
      // Per-session where the event belongs to one; the record's own figures
      // are the fallback for rows filed before sessions existed.
      const sessionId = sessionIdFor(a, sessions);
      const session = sessions.find((x) => x.id === sessionId);
      const isIn = a.kind === "CLOCK_IN";
      return {
        approvalId: a.id,
        sessionId,
        ipAllowed: isIn ? record.clockInIpAllowed ?? null : null,
        kind: a.kind as "CLOCK_IN" | "CLOCK_OUT",
        time: a.eventAt,
        // Lateness on the first shift only, same rule as SessionList: every
        // clock-in is measured against the one scheduled start, so returning at
        // 14:30 from an afternoon off is stamped "338 minutes late" — a number
        // with no second shift start behind it. Two clock-ins both flagged
        // LATE 338M is that bug on screen.
        lateByMin:
          isIn && (session ? sessions[0]?.id === session.id : true)
            ? session?.lateByMin ?? record.lateByMin ?? null
            : null,
        distance: isIn
          ? session?.clockInDistanceMeters ?? record.clockInDistanceMeters ?? null
          : session?.clockOutDistanceMeters ?? record.clockOutDistanceMeters ?? null,
        photoUrl: isIn
          ? session?.clockInPhotoUrl ?? record.clockInPhotoUrl ?? null
          : session?.clockOutPhotoUrl ?? record.clockOutPhotoUrl ?? null,
        location: isIn
          ? coordsText(session?.clockInLat ?? record.clockInLat, session?.clockInLng ?? record.clockInLng)
          : coordsText(session?.clockOutLat ?? record.clockOutLat, session?.clockOutLng ?? record.clockOutLng),
      };
    })
    .sort((a, b) => a.time.localeCompare(b.time));
}

// Which shift an approval belongs to.
//
// Normally the request carries the id. A CORRECTION usually doesn't: it is
// stamped with the still-OPEN session, and by the time anyone files "I forgot
// to clock out" that shift has already been closed, so the id is null. The
// original time recovers it — that time IS the session's recorded end (or
// start), so it names the shift exactly. Breaks fall back to the shift whose
// window contains them.
function sessionIdFor(
  approval: AttendanceApprovalRequest,
  sessions: AttendanceSession[],
): string | null {
  const stamped = approval.attendanceSessionId;
  if (stamped && sessions.some((s) => s.id === stamped)) return stamped;

  const at = new Date(approval.originalEventAt ?? approval.eventAt).getTime();
  if (Number.isNaN(at)) return null;

  if (approval.kind === "CLOCK_IN" || approval.kind === "CLOCK_OUT") {
    const edgeOf = (s: AttendanceSession) =>
      approval.kind === "CLOCK_IN" ? s.startedAt : s.endedAt;
    const matched = sessions.find((s) => {
      const edge = edgeOf(s);
      return edge != null && new Date(edge).getTime() === at;
    });
    return matched?.id ?? null;
  }

  const within = sessions.find(
    (s) =>
      new Date(s.startedAt).getTime() <= at &&
      (s.endedAt == null || at <= new Date(s.endedAt).getTime()),
  );
  return within?.id ?? null;
}

// One shift's worth of pending work: the correction asked for on it, its clock
// events, and the breaks taken during it.
//
// Two shifts used to arrive as four clock rows in one flat list, with any
// correction hoisted to a card above all of them — so the approver could see
// that 09:06 AM should have been 06:00 PM without being able to tell WHICH
// clock-out that was. Grouping puts each decision next to the shift it changes.
type ShiftGroup = {
  key: string;
  // 1-based position among the day's shifts. Null when the events couldn't be
  // tied to one — rows filed before sessions existed, which have no shift to
  // be numbered against.
  shiftNo: number | null;
  session: AttendanceSession | null;
  adjustments: AttendanceApprovalRequest[];
  events: PendingEvent[];
  breaks: AttendanceApprovalRequest[];
};

const UNASSIGNED_SHIFT = "__unassigned__";

function shiftGroupsFor(
  record: AttendanceRecord,
  recordBreaks: AttendanceApprovalRequest[],
): ShiftGroup[] {
  const sessions = [...(record.sessions ?? [])].sort((a, b) =>
    a.startedAt.localeCompare(b.startedAt),
  );
  const groups = new Map<string, ShiftGroup>();

  function bucket(sessionId: string | null) {
    const key = sessionId ?? UNASSIGNED_SHIFT;
    const existing = groups.get(key);
    if (existing) return existing;

    const index = sessions.findIndex((s) => s.id === sessionId);
    const created: ShiftGroup = {
      key,
      shiftNo: index >= 0 ? index + 1 : null,
      session: index >= 0 ? sessions[index] : null,
      adjustments: [],
      events: [],
      breaks: [],
    };
    groups.set(key, created);
    return created;
  }

  for (const ask of record.approvals ?? []) {
    if (ask.approvalStatus === "PENDING" && ask.originalEventAt)
      bucket(sessionIdFor(ask, sessions)).adjustments.push(ask);
  }
  for (const event of pendingEventsFor(record)) bucket(event.sessionId).events.push(event);
  for (const brk of recordBreaks) bucket(sessionIdFor(brk, sessions)).breaks.push(brk);

  return [...groups.values()].sort((a, b) => shiftSortKey(a).localeCompare(shiftSortKey(b)));
}

// Shifts read in the order they were worked. A group with no session sorts by
// its own earliest event rather than being pinned to the end — it is still part
// of the same day's timeline.
function shiftSortKey(shift: ShiftGroup) {
  if (shift.session) return shift.session.startedAt;
  return (
    [
      ...shift.adjustments.map((a) => a.originalEventAt ?? a.eventAt),
      ...shift.events.map((e) => e.time),
      ...shift.breaks.map((b) => b.eventAt),
    ].sort()[0] ?? ""
  );
}

// Clock events and breaks interleaved by time, so a shift reads
// in -> break start -> break end -> out rather than listing the breaks after
// the clock events.
type ShiftRow =
  | { kind: "event"; at: string; event: PendingEvent }
  | { kind: "break"; at: string; request: AttendanceApprovalRequest };

function shiftRows(shift: ShiftGroup): ShiftRow[] {
  return [
    ...shift.events.map((event) => ({ kind: "event" as const, at: event.time, event })),
    ...shift.breaks.map((request) => ({ kind: "break" as const, at: request.eventAt, request })),
  ].sort((a, b) => a.at.localeCompare(b.at));
}

// Events actually AWAITING a decision — the same ids the approve and reject
// calls send. It used to count a clock-in or clock-out whenever the time
// existed, pending or not, so a day with an approved clock-in and a pending
// clock-out read "2 events pending" and offered "Approve all (2)" while sending
// one. The per-event buttons made that visible: the clock-in row correctly
// showed no buttons while the header still claimed it was waiting.
function eventCount(group: ApprovalGroup) {
  return groupRequestIds(group).length;
}

function lateCount(group: ApprovalGroup) {
  return group.records.filter((record) => record.lateByMin != null).length;
}

function filterRecords(records: AttendanceRecord[], filter: DateFilter) {
  if (filter === "ALL") return records;
  const today = todayKey();
  if (filter === "TODAY") return records.filter((record) => record.date === today);

  const cutoff = new Date();
  cutoff.setHours(0, 0, 0, 0);
  cutoff.setDate(cutoff.getDate() - 6);
  return records.filter((record) => dateValue(record.date) >= cutoff);
}

export function AttendanceApprovals() {
  const [approvalType, setApprovalType] = useState<ApprovalType>("ATTENDANCE");
  const [filter, setFilter] = useState<DateFilter>("ALL");
  const [employeeSearch, setEmployeeSearch] = useState("");
  const [openKey, setOpenKey] = useState<string | null>(null);
  const [bulkBusy, setBulkBusy] = useState(false);
  const [bulkResult, setBulkResult] = useState<AttendanceBulkResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [rejecting, setRejecting] = useState<RejectTarget | null>(null);
  const [rejectNotes, setRejectNotes] = useState("");
  const [rejectError, setRejectError] = useState<string | null>(null);

  // The queues read through the cache like every other screen, so coming back
  // to this tab paints the rows it had and refreshes behind them. It used to
  // fetch from empty on every mount to keep a decided row from lingering —
  // which cost a full skeleton, and a blink of the whole card list, every
  // single visit. Nothing is actually served stale: any attendance write drops
  // these paths from the cache outright (invalidateFor), the realtime signal
  // below re-reads them, and the hook revalidates anything older than 30s.
  const recordsQuery = useCachedQuery("/attendance/team", getTeamAttendanceApprovals);
  const breaksQuery = useCachedQuery("/attendance/team/breaks", () =>
    getTeamBreakApprovals().catch(() => []),
  );
  const projectsQuery = useCachedQuery("/projects", getProjects);
  const orgQuery = useCachedQuery("/organizations/current", getOrganization);

  // All derived. Copying a query into state through an effect is what makes the
  // first frame of a revisit render from `[]` — the empty list is drawn, then
  // replaced a frame later, which is the blink.
  const records = useMemo(() => recordsQuery.data ?? [], [recordsQuery.data]);
  const breaks = useMemo(() => breaksQuery.data ?? [], [breaksQuery.data]);
  const projects = useMemo(
    () => (projectsQuery.data ?? []).filter((project) => !project.isArchived),
    [projectsQuery.data],
  );
  // The geofence radius decides whether each row reads as on-site, so it has to
  // be right on the first paint rather than a frame after it.
  const radius = orgQuery.data?.geofenceRadiusMeters ?? 200;
  // Only the records gate the skeleton: breaks are an extra on the same cards,
  // and a day's card is worth drawing without them.
  const loading = recordsQuery.loading;

  // Re-read rather than dropping rows by hand. /attendance/team returns only what
  // is still awaiting THIS approver, so it is the authority on what should remain
  // — and once a single event can be decided on its own, filtering out its whole
  // record would take the still-pending events on that day with it.
  const refreshRecords = recordsQuery.refresh;
  const refreshBreaks = breaksQuery.refresh;
  const refreshQueue = useCallback(async () => {
    await Promise.all([refreshRecords(), refreshBreaks()]);
  }, [refreshRecords, refreshBreaks]);

  useEffect(() => {
    const first = recordsQuery.error ?? breaksQuery.error;
    if (first) setError(first);
  }, [recordsQuery.error, breaksQuery.error]);

  // A clock-in/out or break decided elsewhere refreshes this tab live. There's
  // no realtime scope for overtime yet, so that tab (below) stays reload-only.
  useRealtimeEvent(["ATTENDANCE"], refreshQueue);

  const projectNames = useMemo(() => new Map(projects.map((project) => [project.id, project.name])), [projects]);
  const groups = useMemo(() => {
    const grouped = groupApprovals(filterRecords(records, filter), breaks);
    const query = employeeSearch.trim().toLowerCase();
    if (!query) return grouped;
    return grouped.filter((group) =>
      `${group.employeeName} ${group.employeeEmail ?? ""}`.toLowerCase().includes(query),
    );
  }, [records, breaks, filter, employeeSearch]);

  // A day-group is selectable when it still has something pending. The decision
  // endpoints take approval-REQUEST ids, not record ids, so a group whose events
  // have all been decided has nothing to send.
  const isBulkable = (group: ApprovalGroup) => groupRequestIds(group).length > 0;

  // Selection is per EVENT, not per day.
  //
  // A day bundles a clock-in, a clock-out and any breaks, and ticking the day
  // used to send all of them. But those are separate decisions: a supervisor
  // will happily wave through the clock-ins and still want to look at a
  // clock-out that came in off-site or three hours late. Keying the selection on
  // the approval-REQUEST id — which is what the endpoint takes anyway — makes
  // one event the unit, and a whole day just a convenient way to tick several.
  const selectableEvents = useMemo(
    () =>
      groups.flatMap((group) =>
        groupRequestIds(group).map((id) => ({ id, groupKey: group.key })),
      ),
    [groups],
  );

  const selection = useBulkSelection(selectableEvents, (event) => event.id, () => true);
  const selectedEvents = selection.selected.length;
  // The count is events now, so the days they span is the part that is no
  // longer obvious — three events could be one messy day or three tidy ones.
  const selectedDays = new Set(selection.selected.map((event) => event.groupKey)).size;

  // How much of one day is ticked, for its header checkbox: all, some, none.
  function groupSelectionState(group: ApprovalGroup) {
    const ids = groupRequestIds(group);
    const picked = ids.filter((id) => selection.has(id));
    return {
      ids,
      all: ids.length > 0 && picked.length === ids.length,
      some: picked.length > 0 && picked.length < ids.length,
    };
  }

  // One request for the whole batch. Each group already approves through the
  // bulk endpoint — a "row" here is a day, not an event — so this is the same
  // call with the ids of every ticked day concatenated.
  async function confirmBulkApprove() {
    if (selection.selected.length === 0) return;

    const requestIds = selection.selected.map((event) => event.id);
    setBulkBusy(true);
    setError(null);
    try {
      const result = await bulkApproveAttendance(requestIds);
      setBulkResult(result);
      selection.clear();
      await refreshQueue();
      setOpenKey(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not approve those days.");
    } finally {
      setBulkBusy(false);
    }
  }

  async function approveGroup(group: ApprovalGroup) {
    const requestIds = groupRequestIds(group);
    if (requestIds.length === 0) {
      setError("Nothing pending on this day.");
      return;
    }
    await approveRequestIds(group.key, requestIds, { closeIfOpen: group.key });
  }

  // Approves any set of approval-request ids: every pending event on a day, or a
  // single clock-in. The endpoint takes a list either way.
  async function approveRequestIds(
    busy: string,
    requestIds: string[],
    options: { closeIfOpen?: string } = {},
  ) {
    setBusyKey(busy);
    setError(null);
    try {
      const result = await bulkApproveAttendance(requestIds);
      if (result.failed > 0) {
        // Partial success is normal here: another approver may have moved first.
        setError(firstBulkError(result) ?? `${result.failed} of ${requestIds.length} could not be approved.`);
      }
      await refreshQueue();
      if (options.closeIfOpen && openKey === options.closeIfOpen) setOpenKey(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not approve attendance.");
    } finally {
      setBusyKey(null);
    }
  }

  function openReject(target: RejectTarget) {
    setRejecting(target);
    setRejectNotes("");
    setRejectError(null);
  }

  const dayRejectTarget = (group: ApprovalGroup): RejectTarget => ({
    key: group.key,
    ids: groupRequestIds(group),
    title: group.employeeName,
    subtitle: `${group.date} · ${eventCount(group)} events`,
  });

  const eventRejectTarget = (
    group: ApprovalGroup,
    requestId: string,
    label: string,
  ): RejectTarget => ({
    key: requestId,
    ids: [requestId],
    title: group.employeeName,
    subtitle: `${group.date} · ${label}`,
  });

  async function confirmReject() {
    if (!rejecting) return;
    const notes = rejectNotes.trim();
    if (!notes) {
      setRejectError("Remark is required when rejecting attendance.");
      return;
    }

    const { key, ids } = rejecting;
    if (ids.length === 0) {
      setRejectError("Nothing pending here.");
      return;
    }

    setBusyKey(key);
    setError(null);
    try {
      const result = await bulkRejectAttendance(ids, notes);
      if (result.failed > 0) {
        setRejectError(firstBulkError(result) ?? `${result.failed} of ${ids.length} could not be rejected.`);
        setBusyKey(null);
        return;
      }
      await refreshQueue();
      setRejecting(null);
      setRejectNotes("");
      // Only a whole-day rejection empties the card. Rejecting one event leaves
      // the others, so the panel stays open on them.
      if (ids.length > 1 && openKey === key) setOpenKey(null);
    } catch (e) {
      setRejectError(e instanceof Error ? e.message : "Could not reject attendance.");
    } finally {
      setBusyKey(null);
    }
  }

  return (
    <>
      <div className="space-y-4">
        <ApprovalTypeTabs value={approvalType} onChange={setApprovalType} />

        {approvalType === "ATTENDANCE" ? (
          <section className="space-y-3">
            <DateFilterTabs value={filter} onChange={setFilter} />
            <SearchInput
              value={employeeSearch}
              onChange={setEmployeeSearch}
              placeholder="Search employee"
              inputClassName="h-10 rounded-xl border-border/70 bg-card/90 font-semibold focus-visible:border-primary focus-visible:ring-primary/15 focus-visible:ring-offset-0"
            />

            <div className="flex items-center justify-between gap-3 px-1 text-sm text-muted-foreground">
              <p>
                <span className="font-semibold text-foreground">{groups.length}</span>{" "}
                {groups.length === 1 ? "day" : "days"} pending
              </p>

              {/* Shown at every width, unlike claims and leave: this queue is
                  cards only, so there is no desktop table checkbox column. */}
              {selection.selectable.length > 0 ? (
                <div className="flex shrink-0 items-center gap-2">
                  {selection.mode ? (
                    <SelectAllPill
                      inputRef={selection.selectAllRef}
                      total={selection.selectable.length}
                      allSelected={selection.allSelected}
                      onToggleAll={selection.toggleAll}
                    />
                  ) : null}
                  <SelectModeButton
                    active={selection.mode}
                    onToggle={() => (selection.mode ? selection.exit() : selection.enter())}
                  />
                </div>
              ) : null}
            </div>

            {selection.mode && selection.selected.length === 0 ? (
              <SelectHint>
                Tick a day to take all of it, or open one and tick just the clock-in or clock-out
                you want.
              </SelectHint>
            ) : null}

            {bulkResult ? (
              <BulkResultPanel result={bulkResult} onDismiss={() => setBulkResult(null)} />
            ) : null}

            {selection.selected.length > 0 ? (
              <BulkActionBar
                count={selectedEvents}
                noun="event"
                summary={`Across ${selectedDays} day${selectedDays === 1 ? "" : "s"}`}
                busy={bulkBusy}
                onClear={selection.clear}
                onApprove={confirmBulkApprove}
              />
            ) : null}
          </section>
        ) : null}

        {approvalType === "OVERTIME" ? <OvertimeApprovals projectNames={projectNames} /> : null}

        {approvalType === "ATTENDANCE" && loading ? (
          <SkeletonCards />
        ) : null}

        {approvalType === "ATTENDANCE" && error ? (
          <section className="rounded-2xl border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
            Error: {error}
          </section>
        ) : null}

        {approvalType === "ATTENDANCE" && !loading && !error && groups.length === 0 ? (
          <section className={`${CARD} p-8 text-center`}>
            <CalendarClock className="mx-auto h-6 w-6 text-muted-foreground" />
            <p className="mt-3 text-sm font-bold text-foreground">No attendance approvals waiting.</p>
            <p className="mt-1 text-xs text-muted-foreground">Try another date filter or employee search.</p>
          </section>
        ) : null}

        {approvalType === "ATTENDANCE" && !loading && !error && groups.length > 0 ? (
          <section className="space-y-3">
            {groups.map((group) => {
              const open = openKey === group.key;
              // Expansion stays available while selecting — it is the ONLY way
              // to reach a single clock-in or clock-out, which is the whole
              // point of selecting at event level. The header splits the two
              // gestures instead: its checkbox ticks the day, the rest of the
              // row opens it.
              const expanded = open;
              const selectable = isBulkable(group);
              const state = groupSelectionState(group);
              return (
                <article
                  key={group.key}
                  className={`${CARD} overflow-hidden transition-colors ${
                    expanded ? "border-primary/35" : ""
                  } ${selection.mode && !selectable ? "opacity-45" : ""} ${
                    selection.mode && (state.all || state.some)
                      ? "border-primary/50 bg-primary/5 ring-2 ring-primary/25"
                      : ""
                  }`}
                >
                  <GroupHeader
                    group={group}
                    open={expanded}
                    selectMode={selection.mode}
                    selectable={selectable}
                    allSelected={state.all}
                    someSelected={state.some}
                    selectedCount={state.ids.filter((id) => selection.has(id)).length}
                    onToggleSelectDay={() => selection.setMany(state.ids, !state.all)}
                    onToggleOpen={() => setOpenKey(open ? null : group.key)}
                  />
                  {expanded ? (
                    <ExpandedGroup
                      group={group}
                      radius={radius}
                      projectNames={projectNames}
                      busy={busyKey === group.key}
                      busyKey={busyKey}
                      selectMode={selection.mode}
                      isSelected={(requestId) => selection.has(requestId)}
                      onToggleSelect={(requestId) => selection.toggle(requestId)}
                      onApprove={() => approveGroup(group)}
                      onReject={() => openReject(dayRejectTarget(group))}
                      onApproveEvent={(requestId) =>
                        approveRequestIds(requestId, [requestId])
                      }
                      onRejectEvent={(requestId, label) =>
                        openReject(eventRejectTarget(group, requestId, label))
                      }
                    />
                  ) : null}
                </article>
              );
            })}
          </section>
        ) : null}
      </div>

      {rejecting ? (
        <RejectDialog
          target={rejecting}
          busy={busyKey === rejecting.key}
          notes={rejectNotes}
          error={rejectError}
          onNotesChange={setRejectNotes}
          onClose={() => setRejecting(null)}
          onConfirm={confirmReject}
        />
      ) : null}
    </>
  );
}

function DateFilterTabs({ value, onChange }: { value: DateFilter; onChange: (value: DateFilter) => void }) {
  const tabs: { id: DateFilter; label: string }[] = [
    { id: "ALL", label: "All" },
    { id: "TODAY", label: "Today" },
    { id: "LAST_7_DAYS", label: "Last 7 days" },
  ];

  return (
    <div className="inline-flex max-w-full items-center gap-1 rounded-xl border border-border/60 bg-surface-low p-1">
      {tabs.map((tab) => {
        const active = value === tab.id;
        return (
          <button
            key={tab.id}
            type="button"
            onClick={() => onChange(tab.id)}
            className={`h-8 rounded-lg px-3 text-xs font-bold transition sm:px-4 ${
              active
                ? "bg-card text-primary shadow-sm"
                : "text-muted-foreground hover:bg-card/70 hover:text-foreground"
            }`}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}

function ApprovalTypeTabs({ value, onChange }: { value: ApprovalType; onChange: (value: ApprovalType) => void }) {
  const tabs: { id: ApprovalType; label: string }[] = [
    { id: "ATTENDANCE", label: "Attendance" },
    { id: "OVERTIME", label: "Overtime" },
  ];

  return (
    <div className="grid grid-cols-2 rounded-xl border border-border/60 bg-surface-low p-1">
      {tabs.map((tab) => {
        const active = value === tab.id;
        return (
          <button
            key={tab.id}
            type="button"
            onClick={() => onChange(tab.id)}
            // Same segmented treatment as StatusFilterTabs, so the two toggle
            // bars on this screen don't read as different kinds of control.
            // Solid primary looked like a button you press rather than the tab
            // you're currently on.
            className={`h-9 rounded-lg text-xs font-bold transition-colors ${
              active
                ? "bg-card text-primary shadow-sm"
                : "text-muted-foreground hover:bg-card/70 hover:text-foreground"
            }`}
          >
            {tab.label}
          </button>
        );
      })}
    </div>
  );
}

function OvertimeApprovals({ projectNames }: { projectNames: Map<string, string> }) {
  const requestsQuery = useCachedQuery("/overtime/team", getTeamOvertime);
  // Seeded from the cache in the initializer rather than left empty for an
  // effect to fill after the paint — that one frame, built from [], is the
  // blink. State and not a derivation because a decision patches its own row in
  // place; the effect below keeps it in step with the background refresh.
  const [requests, setRequests] = useState<OvertimeRequest[]>(() => requestsQuery.data ?? []);
  const [status, setStatus] = useState<OvertimeStatusFilter>("ALL");
  const [employeeSearch, setEmployeeSearch] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [selectedRequest, setSelectedRequest] = useState<OvertimeRequest | null>(null);
  const [rejectingRequest, setRejectingRequest] = useState<OvertimeRequest | null>(null);
  const [rejectNotes, setRejectNotes] = useState("");
  const [rejectError, setRejectError] = useState<string | null>(null);
  const [bulkBusy, setBulkBusy] = useState(false);
  const [bulkResult, setBulkResult] = useState<OvertimeBulkResult | null>(null);

  const loading = requestsQuery.loading;

  useEffect(() => {
    if (requestsQuery.data) setRequests(requestsQuery.data);
  }, [requestsQuery.data]);
  useEffect(() => {
    if (requestsQuery.error) setError(requestsQuery.error);
  }, [requestsQuery.error]);

  const filteredRequests = useMemo(() => {
    const query = employeeSearch.trim().toLowerCase();
    return requests.filter((request) => {
      if (!overtimeMatchesStatus(request, status)) return false;
      if (!query) return true;
      const employee = request.employeeEmail ? buildName(request.employeeEmail) : "Employee";
      return `${employee} ${request.employeeEmail ?? ""}`.toLowerCase().includes(query);
    });
  }, [requests, status, employeeSearch]);

  // Bulk approval is only offered on requests that are decidable AND have the
  // after-work photo. ApproveAsync gates on that photo, so a request without one
  // would come back as a per-row failure — excluding it up front is the same
  // rule, applied before the approver taps rather than after.
  const isBulkable = (request: OvertimeRequest) =>
    request.status === "PENDING" && !!request.afterPhotoUrl;

  const selection = useBulkSelection(filteredRequests, (request) => request.id, isBulkable);
  const selectedMinutes = selection.selected.reduce(
    (sum, request) => sum + request.requestedMinutes,
    0,
  );
  const awaitingPhoto = filteredRequests.filter(
    (request) => request.status === "PENDING" && !request.afterPhotoUrl,
  ).length;

  async function decide(id: string, fn: (id: string) => Promise<OvertimeRequest>) {
    setBusyId(id);
    setError(null);
    try {
      const updated = await fn(id);
      setRequests((current) => current.map((request) => (request.id === id ? updated : request)));
      return true;
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not update overtime.");
      return false;
    } finally {
      setBusyId(null);
    }
  }

  async function confirmBulkApprove() {
    if (selection.selected.length === 0) return;

    setBulkBusy(true);
    setError(null);
    try {
      const result = await bulkApproveOvertime(selection.selected.map((request) => request.id));
      setBulkResult(result);
      selection.clear();
      // Re-read rather than patching rows: a request on a multi-step chain stays
      // PENDING and moves to the next approver, so its row changes meaning.
      setRequests(await getTeamOvertime());
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not approve those requests.");
    } finally {
      setBulkBusy(false);
    }
  }

  function openReject(request: OvertimeRequest) {
    setRejectingRequest(request);
    setRejectNotes("");
    setRejectError(null);
  }

  async function confirmReject() {
    if (!rejectingRequest) return;
    const notes = rejectNotes.trim();
    if (!notes) {
      setRejectError("Remark is required when rejecting overtime.");
      return;
    }
    const ok = await decide(rejectingRequest.id, (id) => rejectOvertime(id, notes));
    if (ok) setRejectingRequest(null);
  }

  return (
    <>
      <section className="space-y-3">
        <StatusFilterTabs<OvertimeStatusFilter>
          statuses={visibleOvertimeStatuses}
          labels={overtimeStatusLabels}
          value={status}
          onChange={setStatus}
          ariaLabel="Overtime approval status filters"
        />
        <SearchInput
          value={employeeSearch}
          onChange={setEmployeeSearch}
          placeholder="Search employee"
          inputClassName="h-10 rounded-xl border-border/70 bg-card/90 font-semibold focus-visible:border-primary focus-visible:ring-primary/15 focus-visible:ring-offset-0"
        />
        <div className="flex items-center justify-between gap-3 px-1 text-sm text-muted-foreground">
          <p>
            Showing <span className="font-semibold text-foreground">{filteredRequests.length}</span> of{" "}
            <span className="font-semibold text-foreground">{requests.length}</span> overtime approvals
          </p>

          {/* Shown at every width, unlike claims and leave: this queue is cards
              only, so there is no desktop table with a checkbox column. */}
          {selection.selectable.length > 0 ? (
            <div className="flex shrink-0 items-center gap-2">
              {selection.mode ? (
                <SelectAllPill
                  inputRef={selection.selectAllRef}
                  total={selection.selectable.length}
                  allSelected={selection.allSelected}
                  onToggleAll={selection.toggleAll}
                />
              ) : null}
              <SelectModeButton
                active={selection.mode}
                onToggle={() => (selection.mode ? selection.exit() : selection.enter())}
              />
            </div>
          ) : null}
        </div>

        {selection.mode && selection.selected.length === 0 ? (
          <SelectHint>Or tap the requests you want to approve together.</SelectHint>
        ) : null}

        {bulkResult ? (
          <BulkResultPanel result={bulkResult} onDismiss={() => setBulkResult(null)} />
        ) : null}

        {selection.selected.length > 0 ? (
          <BulkActionBar
            count={selection.selected.length}
            noun="request"
            summary={`Approving ${fmtDuration(selectedMinutes)} of overtime in one go`}
            busy={bulkBusy}
            onClear={selection.clear}
            onApprove={confirmBulkApprove}
          />
        ) : null}

        {/* The one thing an approver cannot fix from this screen, so it says so
            rather than leaving them to wonder why a row won't tick. */}
        {awaitingPhoto > 0 ? (
          <p className="flex items-start gap-2 px-1 text-xs text-muted-foreground">
            <TriangleAlert className="mt-0.5 h-3.5 w-3.5 shrink-0 text-amber-600" />
            {awaitingPhoto} request{awaitingPhoto === 1 ? " is" : "s are"} still waiting on the
            after-work photo and cannot be approved yet.
          </p>
        ) : null}
      </section>

      {loading ? <SkeletonCards /> : null}

      {error ? (
        <section className="rounded-2xl border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
          Error: {error}
        </section>
      ) : null}

      {!loading && !error && filteredRequests.length === 0 ? (
        <section className={`${CARD} p-8 text-center`}>
          <CalendarClock className="mx-auto h-6 w-6 text-muted-foreground" />
          <p className="mt-3 text-sm font-bold text-foreground">No overtime approvals match this status.</p>
          <p className="mt-1 text-xs text-muted-foreground">Try another overtime status or employee search.</p>
        </section>
      ) : null}

      {!loading && !error && filteredRequests.length > 0 ? (
        <section className="grid gap-3">
          {filteredRequests.map((request) => {
            const employee = request.employeeEmail ? buildName(request.employeeEmail) : "Employee";
            const projectName = request.projectId ? projectNames.get(request.projectId) : null;
            return (
              <OvertimeApprovalCard
                key={request.id}
                request={request}
                employee={employee}
                projectName={projectName}
                busy={busyId === request.id}
                selectMode={selection.mode}
                selectable={isBulkable(request)}
                selected={selection.has(request.id)}
                onOpen={() => {
                  // In select mode the card IS the checkbox — a full-card target
                  // instead of a 16px one inside a card that is itself tappable.
                  if (selection.mode) {
                    if (isBulkable(request)) selection.toggle(request.id);
                    return;
                  }
                  setSelectedRequest(request);
                }}
                onApprove={() => decide(request.id, approveOvertime)}
                onReject={() => openReject(request)}
              />
            );
          })}
        </section>
      ) : null}

      {rejectingRequest ? (
        <OvertimeRejectDialog
          request={rejectingRequest}
          busy={busyId === rejectingRequest.id}
          notes={rejectNotes}
          error={rejectError}
          onNotesChange={setRejectNotes}
          onClose={() => setRejectingRequest(null)}
          onConfirm={confirmReject}
        />
      ) : null}

      {selectedRequest ? (
        <OvertimeApprovalDetailsModal
          request={selectedRequest}
          employee={selectedRequest.employeeEmail ? buildName(selectedRequest.employeeEmail) : "Employee"}
          projectName={selectedRequest.projectId ? projectNames.get(selectedRequest.projectId) : null}
          onClose={() => setSelectedRequest(null)}
        />
      ) : null}
    </>
  );
}

function OvertimeApprovalCard({
  request,
  employee,
  projectName,
  busy,
  selectMode,
  selectable,
  selected,
  onOpen,
  onApprove,
  onReject,
}: {
  request: OvertimeRequest;
  employee: string;
  projectName: string | null | undefined;
  busy: boolean;
  selectMode: boolean;
  selectable: boolean;
  selected: boolean;
  onOpen: () => void;
  onApprove: () => void;
  onReject: () => void;
}) {
  const pending = request.status === "PENDING";

  return (
    <article
      className={`${CARD} overflow-hidden transition-colors ${
        selectMode && !selectable
          ? // Dimmed and inert: decided requests and ones still missing their
            // after-work photo cannot be batch-approved, and offering them would
            // only produce a per-row failure in the result panel.
            "opacity-45"
          : "hover:border-primary/35"
      } ${selected ? "border-primary/50 bg-primary/5 ring-2 ring-primary/25" : ""}`}
    >
      <button
        type="button"
        disabled={selectMode && !selectable}
        onClick={onOpen}
        aria-label={selectMode ? `Select ${employee}'s overtime request` : undefined}
        aria-pressed={selectMode && selectable ? selected : undefined}
        className="block w-full space-y-3 p-4 text-left disabled:cursor-default"
      >
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">Overtime</p>
            <p className="mt-1 truncate text-base font-black text-foreground">{employee}</p>
            <p className="text-sm text-muted-foreground">{request.employeeEmail ?? "Employee"}</p>
          </div>
          <OvertimeApprovalStatusBadge status={request.status} />
        </div>

        <div className="grid grid-cols-2 gap-3">
          <div>
            <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">Project</p>
            <p className="mt-1 truncate text-sm font-semibold text-foreground">{projectName ?? "-"}</p>
          </div>
          <div>
            <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">Duration</p>
            <p className="mt-1 text-sm font-semibold text-foreground">{fmtDuration(request.requestedMinutes)}</p>
          </div>
        </div>
      </button>

      <div className="mx-4 mb-4 flex items-center justify-between gap-3 rounded-2xl bg-surface-low p-3">
        <div>
          <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">Work date</p>
          <p className="mt-1 text-sm font-semibold text-foreground">{request.workDate}</p>
        </div>
        {/* Two ways to approve the same row on one card, one of which also
            swallows the tap meant to tick it. */}
        {pending && !selectMode ? (
          <div className="flex shrink-0 items-center gap-2">
            <button
              type="button"
              disabled={busy}
              onClick={onApprove}
              className="inline-flex h-9 items-center justify-center gap-2 rounded-full bg-secondary px-4 text-xs font-bold text-secondary-foreground transition hover:opacity-90 disabled:opacity-50"
            >
              {busy ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : null}
              Approve
            </button>
            <button
              type="button"
              disabled={busy}
              onClick={onReject}
              className="inline-flex h-9 items-center justify-center rounded-full bg-destructive/10 px-4 text-xs font-bold text-destructive transition hover:bg-destructive/20 disabled:opacity-50"
            >
              Reject
            </button>
          </div>
        ) : null}
      </div>
    </article>
  );
}

function OvertimeApprovalDetailsModal({
  request,
  employee,
  projectName,
  onClose,
}: {
  request: OvertimeRequest;
  employee: string;
  projectName: string | null | undefined;
  onClose: () => void;
}) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm">
      <div className="nice-scrollbar max-h-[90vh] w-full max-w-[640px] overflow-y-auto rounded-[28px] border border-white/40 bg-card/95 p-6 shadow-[0_18px_48px_rgba(76,26,134,0.14)] backdrop-blur-xl sm:p-8">
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Overtime</p>
            <h2 className="mt-1 truncate text-2xl font-black text-foreground">{employee}</h2>
            <p className="mt-1 text-sm text-muted-foreground">{request.employeeEmail ?? "Employee"}</p>
          </div>
          <button
            type="button"
            aria-label="Close overtime details"
            onClick={onClose}
            className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <section className="mt-5 rounded-[22px] border border-border/70 bg-surface-low/60 p-5">
          <div className="flex flex-col gap-5 sm:flex-row sm:items-end sm:justify-between">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Duration</p>
              <p className="mt-2 text-3xl font-black leading-none text-foreground">
                {fmtDuration(request.requestedMinutes)}
              </p>
              <div className="mt-4">
                <OvertimeApprovalStatusBadge status={request.status} />
              </div>
            </div>
            <div className="grid grid-cols-2 gap-x-6 gap-y-3 sm:min-w-[260px]">
              <OvertimeFact label="Work date" value={request.workDate} />
              <OvertimeFact label="Project" value={projectName ?? "Not assigned"} />
              <OvertimeFact label="Start" value={fmtTime(request.startAt)} />
              <OvertimeFact label="End" value={fmtTime(request.endAt)} />
            </div>
          </div>
        </section>

        <section className="mt-4 rounded-[22px] border border-border/70 bg-card/70 p-5">
          <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Reason</p>
          <p className="mt-3 whitespace-pre-wrap text-sm leading-6 text-foreground">{request.reason}</p>
        </section>

        {request.reviewNotes ? (
          <section className="mt-4 rounded-[22px] border border-border/70 bg-card/70 p-5">
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Reviewer note</p>
            <p className="mt-3 whitespace-pre-wrap text-sm leading-6 text-foreground">{request.reviewNotes}</p>
          </section>
        ) : null}

        <div className="mt-5 flex flex-wrap items-center gap-2 border-t border-border/60 pt-5">
          <span className="text-xs font-semibold uppercase tracking-[0.16em] text-muted-foreground">Photos</span>
          <button
            type="button"
            onClick={() => openOvertimePhoto(request.beforePhotoUrl)}
            className="inline-flex rounded-full bg-muted px-4 py-2 text-sm font-semibold text-primary transition hover:bg-secondary"
          >
            Before photo
          </button>
          {request.afterPhotoUrl ? (
            <button
              type="button"
              onClick={() => openOvertimePhoto(request.afterPhotoUrl!)}
              className="inline-flex rounded-full bg-muted px-4 py-2 text-sm font-semibold text-primary transition hover:bg-secondary"
            >
              After photo
            </button>
          ) : (
            <span className="text-sm text-muted-foreground">No after photo attached</span>
          )}
        </div>
      </div>
    </div>
  );
}

function OvertimeFact({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted-foreground">{label}</p>
      <p className="mt-1 truncate text-sm font-bold text-foreground">{value}</p>
    </div>
  );
}

function OvertimeApprovalStatusBadge({ status }: { status: OvertimeRequest["status"] }) {
  const className =
    status === "APPROVED"
      ? "bg-secondary text-secondary-foreground"
      : status === "REJECTED"
        ? "bg-destructive/10 text-destructive"
        : status === "CANCELLED"
          ? "bg-muted text-muted-foreground"
          : "bg-warning text-warning-foreground";

  return (
    <span className={`rounded-full px-2.5 py-1 text-[9px] font-bold uppercase tracking-wider ${className}`}>
      {overtimeStatusLabels[status]}
    </span>
  );
}

function OvertimeRejectDialog({
  request,
  busy,
  notes,
  error,
  onNotesChange,
  onClose,
  onConfirm,
}: {
  request: OvertimeRequest;
  busy: boolean;
  notes: string;
  error: string | null;
  onNotesChange: (value: string) => void;
  onClose: () => void;
  onConfirm: () => void;
}) {
  const employee = request.employeeEmail ? buildName(request.employeeEmail) : "Employee";

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/35 px-4 py-5 backdrop-blur-sm sm:items-center">
      <section className="w-full max-w-md rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)]">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Reject overtime</p>
            <h2 className="mt-1 text-lg font-black text-foreground">{employee}</h2>
            <p className="mt-1 text-xs text-muted-foreground">
              {request.workDate} · {fmtDuration(request.requestedMinutes)}
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="grid h-9 w-9 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground"
            aria-label="Close reject dialog"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <textarea
          value={notes}
          onChange={(event) => onNotesChange(event.target.value)}
          rows={4}
          placeholder="Reason for rejection"
          className="mt-5 w-full resize-none rounded-2xl border border-border bg-white/80 px-4 py-3 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        />
        {error ? <p className="mt-2 text-xs font-medium text-destructive">{error}</p> : null}

        <div className="mt-5 grid grid-cols-2 gap-3">
          <button
            type="button"
            disabled={busy}
            onClick={onConfirm}
            className="inline-flex h-11 items-center justify-center rounded-2xl bg-destructive/10 px-4 text-sm font-bold text-destructive transition hover:bg-destructive/20 disabled:opacity-50"
          >
            Reject
          </button>
          <button
            type="button"
            onClick={onClose}
            className="inline-flex h-11 items-center justify-center rounded-2xl bg-muted px-4 text-sm font-bold text-muted-foreground transition hover:text-foreground"
          >
            Cancel
          </button>
        </div>
      </section>
    </div>
  );
}

function GroupHeader({
  group,
  open,
  selectMode,
  selectable,
  allSelected,
  someSelected,
  selectedCount,
  onToggleSelectDay,
  onToggleOpen,
}: {
  group: ApprovalGroup;
  open: boolean;
  selectMode: boolean;
  selectable: boolean;
  allSelected: boolean;
  someSelected: boolean;
  selectedCount: number;
  onToggleSelectDay: () => void;
  onToggleOpen: () => void;
}) {
  const count = eventCount(group);
  const late = lateCount(group);

  // No checkbox on the card at all — the toolbar's select-all is the only one
  // left. The header's two jobs are split by TARGET instead: in select mode the
  // body takes the whole day, the chevron opens it to pick a single event.
  const selectsDay = selectMode && selectable;

  return (
    <div className={`flex items-center gap-2.5 px-4 py-3.5 ${open ? "bg-primary/5" : ""}`}>
      <button
        type="button"
        disabled={selectMode && !selectable}
        onClick={selectsDay ? onToggleSelectDay : onToggleOpen}
        aria-expanded={selectMode ? undefined : open}
        aria-pressed={selectsDay ? allSelected || someSelected : undefined}
        aria-label={
          selectsDay
            ? `Select all ${count} pending event${count === 1 ? "" : "s"} for ${group.employeeName} on ${group.date}`
            : undefined
        }
        className="flex min-w-0 flex-1 items-center gap-3 text-left disabled:cursor-default"
      >
        <div className="min-w-0 flex-1 space-y-1">
          <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
            <p className="truncate text-[14px] font-black text-foreground">{group.employeeName}</p>
            <span className="rounded-full bg-surface-low px-2 py-0.5 text-[10px] font-bold text-muted-foreground">
              {group.date}
            </span>
          </div>
          <p className="truncate text-xs font-medium text-muted-foreground">
            {selectMode && selectedCount > 0
              ? `${selectedCount} of ${count} selected`
              : `${count} ${count === 1 ? "event" : "events"} pending`}
          </p>
        </div>
        {late > 0 ? (
          <span className="shrink-0 rounded-full bg-warning px-2.5 py-1 text-[9px] font-bold uppercase tracking-wider text-warning-foreground">
            {late} late
          </span>
        ) : null}
        {/* Outside select mode the whole header already expands, so the chevron
            is decoration and must not be a nested button. */}
        {selectMode ? null : (
          <span
            className={`grid h-8 w-8 shrink-0 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground transition ${
              open ? "border-primary/45 text-primary" : ""
            }`}
          >
            <ChevronDown className={`h-4 w-4 transition ${open ? "rotate-180" : ""}`} />
          </span>
        )}
      </button>

      {/* In select mode the header body selects, so opening a day needs its own
          target — otherwise a single clock-in could not be reached at all. */}
      {selectMode ? (
        <button
          type="button"
          onClick={onToggleOpen}
          aria-expanded={open}
          aria-label={open ? "Hide this day's events" : "Show this day's events to pick one"}
          className={`grid h-8 w-8 shrink-0 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground transition hover:text-foreground ${
            open ? "border-primary/45 text-primary" : ""
          }`}
        >
          <ChevronDown className={`h-4 w-4 transition ${open ? "rotate-180" : ""}`} />
        </button>
      ) : null}
    </div>
  );
}

function ExpandedGroup({
  group,
  radius,
  projectNames,
  busy,
  busyKey,
  onApprove,
  onReject,
  selectMode,
  isSelected,
  onToggleSelect,
  onApproveEvent,
  onRejectEvent,
}: {
  group: ApprovalGroup;
  radius: number;
  projectNames: Map<string, string>;
  busy: boolean;
  busyKey: string | null;
  onApprove: () => void;
  onReject: () => void;
  selectMode: boolean;
  isSelected: (requestId: string) => boolean;
  onToggleSelect: (requestId: string) => void;
  onApproveEvent: (requestId: string) => void;
  onRejectEvent: (requestId: string, label: string) => void;
}) {
  const count = eventCount(group);
  const eventActions = {
    busyKey,
    onApprove: onApproveEvent,
    onReject: onRejectEvent,
    selectMode,
    isSelected,
    onToggleSelect,
  };

  return (
    <div className="border-t border-border/60 bg-surface-low/45 px-3.5 pb-3.5 pt-3">
      <div className="mb-2 flex items-center gap-2 px-1 text-[11px] font-bold uppercase tracking-[0.16em] text-muted-foreground">
        <CheckSquare className="h-3.5 w-3.5" />
        Pending events
      </div>
      <div className="space-y-2">
        {group.records.map((record) => {
          const recordBreaks = group.breaks.filter((b) => b.attendanceRecordId === record.id);
          // Number and box the shifts only on a day that had more than one. On
          // a single-shift day "Shift 1" and a frame around the only thing
          // there is chrome that says nothing.
          const showShifts = (record.sessions?.length ?? 0) > 1;
          return (
            <div key={record.id} className="space-y-2">
              {shiftGroupsFor(record, recordBreaks).map((shift) => (
                <ShiftBlock
                  key={shift.key}
                  shift={shift}
                  showHeader={showShifts}
                  projectName={record.projectId ? projectNames.get(record.projectId) : null}
                  radius={radius}
                  actions={eventActions}
                />
              ))}
            </div>
          );
        })}
      </div>

      <div className={`mt-3 grid grid-cols-2 gap-2 ${selectMode ? "hidden" : ""}`}>
        <button
          type="button"
          disabled={busy}
          onClick={onApprove}
          className="inline-flex h-9 items-center justify-center gap-2 rounded-xl bg-primary px-3 text-xs font-bold text-primary-foreground shadow-sm transition hover:bg-primary/90 disabled:opacity-50"
        >
          {busy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
          Approve all ({count})
        </button>
        <button
          type="button"
          disabled={busy}
          onClick={onReject}
          className="inline-flex h-9 items-center justify-center rounded-xl border border-border bg-card px-3 text-xs font-bold text-foreground shadow-sm transition hover:bg-muted disabled:opacity-50"
        >
          Reject all ({count})
        </button>
      </div>
    </div>
  );
}

// One shift, headed by which shift it is and the window it covered.
//
// The header is what tells two otherwise identical stacks of clock rows apart:
// with four pending events on one day, "Clock out 09:06 AM" alone doesn't say
// whether it closed the morning or the evening.
function ShiftBlock({
  shift,
  showHeader,
  projectName,
  radius,
  actions,
}: {
  shift: ShiftGroup;
  showHeader: boolean;
  projectName: string | null | undefined;
  radius: number;
  actions: {
    busyKey: string | null;
    onApprove: (requestId: string) => void;
    onReject: (requestId: string, label: string) => void;
    selectMode: boolean;
    isSelected: (requestId: string) => boolean;
    onToggleSelect: (requestId: string) => void;
  };
}) {
  const body = (
    <div className="space-y-2">
      <AdjustmentNotice asks={shift.adjustments} />
      {shiftRows(shift).map((row) =>
        row.kind === "event" ? (
          <EventRow
            key={row.event.approvalId}
            title={row.event.kind === "CLOCK_IN" ? "Clock in" : "Clock out"}
            time={row.event.time}
            lateByMin={row.event.lateByMin}
            distance={row.event.distance}
            radius={radius}
            ipAllowed={row.event.ipAllowed}
            projectName={projectName}
            location={row.event.location}
            photoUrl={row.event.photoUrl}
            approvalId={row.event.approvalId}
            busyKey={actions.busyKey}
            selectMode={actions.selectMode}
            isSelected={actions.isSelected}
            onToggleSelect={actions.onToggleSelect}
            onApprove={actions.onApprove}
            onReject={actions.onReject}
          />
        ) : (
          <BreakEventRow
            key={row.request.id}
            request={row.request}
            busyKey={actions.busyKey}
            selectMode={actions.selectMode}
            selected={actions.isSelected(row.request.id)}
            onToggleSelect={actions.onToggleSelect}
            onApprove={actions.onApprove}
            onReject={actions.onReject}
          />
        ),
      )}
    </div>
  );

  if (!showHeader) return body;

  const session = shift.session;
  const offset = session ? dayOffsetLabel(session.startedAt, session.endedAt) : "";

  return (
    <section className="rounded-[20px] border border-border/70 bg-surface-lowest/60 p-2">
      <div className="mb-2 flex flex-wrap items-center gap-x-2 gap-y-1 px-1.5 pt-0.5">
        <span className="rounded-full bg-primary/10 px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.14em] text-primary">
          {shift.shiftNo ? `Shift ${shift.shiftNo}` : "Other events"}
        </span>
        {session ? (
          <span className="text-[11px] font-semibold tabular-nums text-muted-foreground">
            {fmtTime(session.startedAt)} –{" "}
            {session.endedAt ? fmtTime(session.endedAt) : "still in"}
            {offset ? <span className="ml-1 font-bold text-tertiary">{offset}</span> : null}
          </span>
        ) : null}
        {session?.durationMin != null ? (
          <span className="text-[11px] font-semibold tabular-nums text-muted-foreground">
            · {fmtDuration(session.durationMin)}
          </span>
        ) : null}
      </div>
      {body}
    </section>
  );
}

// A time-adjustment request on this shift, if the employee asked for one.
//
// The clock recorded one time and the employee is asking for another; approving
// the shift applies the corrected time, rejecting keeps what the clock said.
// It sits inside its own shift rather than above the whole day: on a two-shift
// day, a card at the top left the approver guessing which clock-out the
// crossed-out time belonged to.
function AdjustmentNotice({ asks }: { asks: AttendanceApprovalRequest[] }) {
  if (asks.length === 0) return null;

  return (
    <div className="space-y-2 rounded-2xl border border-primary/40 bg-primary/5 px-3.5 py-3">
      {asks.map((ask) => (
        <div key={ask.id} className="space-y-1">
          <div className="flex items-center gap-2">
            <PencilLine className="h-3.5 w-3.5 shrink-0 text-primary" />
            <p className="text-[11px] font-bold uppercase tracking-[0.16em] text-primary">
              {ask.kind === "CLOCK_IN" ? "Clock-in" : "Clock-out"} correction requested
            </p>
          </div>
          <p className="pl-5.5 text-sm font-semibold text-foreground">
            {fmtTime(ask.eventAt)}
            <span className="ml-2 text-xs font-medium text-muted-foreground line-through">
              {fmtTime(ask.originalEventAt!)}
            </span>
          </p>
          {ask.reason ? (
            <p className="pl-5.5 text-xs text-muted-foreground">&ldquo;{ask.reason}&rdquo;</p>
          ) : null}
        </div>
      ))}
    </div>
  );
}

// Deliberately quieter than a clock event: a break carries no geofence or
// photo, and the supervisor is mostly checking the time and the reason.
function BreakEventRow({
  request,
  busyKey,
  selectMode,
  selected,
  onToggleSelect,
  onApprove,
  onReject,
}: {
  request: AttendanceApprovalRequest;
  busyKey: string | null;
  selectMode: boolean;
  selected: boolean;
  onToggleSelect: (requestId: string) => void;
  onApprove: (requestId: string) => void;
  onReject: (requestId: string, label: string) => void;
}) {
  const label = request.kind === "BREAK_START" ? "Break start" : "Break end";

  // A break is an event like any other and is already inside the day's request
  // ids, so it has to be tickable too — otherwise "select day" would sweep in
  // breaks the approver could neither see nor untick.
  const row = (
    <div
      className={`flex items-start gap-2.5 rounded-2xl border bg-card px-3.5 py-2.5 transition ${
        selectMode && selected
          ? "border-primary/50 bg-primary/5 ring-2 ring-primary/25"
          : "border-border/60"
      }`}
    >
      <Coffee className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" />
      <div className="min-w-0 flex-1">
        <p className="text-sm font-bold text-foreground">
          {label}
          <span className="ml-2 text-xs font-semibold tabular-nums text-muted-foreground">
            {fmtTime(request.eventAt)}
          </span>
        </p>
        {request.reason ? (
          <p className="mt-0.5 text-xs text-muted-foreground">&ldquo;{request.reason}&rdquo;</p>
        ) : null}

        {/* group.breaks only ever holds requests awaiting this approver, so a
            break row is always decidable — no pending check needed. */}
        {selectMode ? null : (
          <EventDecisionButtons
            busy={busyKey === request.id}
            onApprove={() => onApprove(request.id)}
            onReject={() => onReject(request.id, label)}
          />
        )}
      </div>
    </div>
  );

  if (!selectMode) return row;

  return (
    <div
      role="button"
      tabIndex={0}
      aria-pressed={selected}
      onClick={() => onToggleSelect(request.id)}
      onKeyDown={(event) => {
        if (event.key !== "Enter" && event.key !== " ") return;
        event.preventDefault();
        onToggleSelect(request.id);
      }}
      className="cursor-pointer rounded-2xl focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 focus-visible:ring-offset-2"
    >
      {row}
    </div>
  );
}

function EventRow({
  title,
  time,
  lateByMin,
  distance,
  radius,
  ipAllowed,
  projectName,
  location,
  photoUrl,
  approvalId,
  busyKey,
  selectMode,
  isSelected,
  onToggleSelect,
  onApprove,
  onReject,
}: {
  title: string;
  time: string;
  lateByMin: number | null;
  distance: number | null;
  radius: number;
  // false = the IP check ran and failed; this clock-in came through the
  // remark-and-photo override. null = no check ran, so nothing to say.
  ipAllowed: boolean | null;
  projectName: string | null | undefined;
  location: string;
  photoUrl: string | null;
  // Null when this event has already been decided: the row still renders (it is
  // context for the ones that haven't), it just gets no buttons.
  approvalId: string | null;
  busyKey: string | null;
  selectMode: boolean;
  isSelected: (requestId: string) => boolean;
  onToggleSelect: (requestId: string) => void;
  onApprove: (requestId: string) => void;
  onReject: (requestId: string, label: string) => void;
}) {
  const isOffSite = offSite(distance, radius);
  // Only a PENDING event can be ticked. A decided one still renders as context
  // for the ones that haven't been, but it is not part of any batch.
  const selectable = selectMode && approvalId !== null;
  const ticked = selectable && isSelected(approvalId);

  const row = (
    <div
      className={`rounded-2xl border bg-card px-3.5 py-3 shadow-sm transition ${
        ticked ? "border-primary/50 bg-primary/5 ring-2 ring-primary/25" : "border-border/60"
      } ${selectMode && !selectable ? "opacity-45" : ""}`}
    >
      <div className="flex items-start gap-2.5">
        <div className="min-w-0 flex-1">
          <div className="flex items-start justify-between gap-2">
            <div className="min-w-0">
              <div className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
                <h3 className="text-[12px] font-black text-foreground">{title}</h3>
                <span className="text-[15px] font-black tabular-nums text-foreground">{fmtTime(time)}</span>
              </div>
              <div className="mt-1.5 flex flex-wrap items-center gap-1.5">
                {lateByMin != null ? (
                  <span className="rounded-full bg-warning px-2 py-0.5 text-[9px] font-black uppercase tracking-wider text-warning-foreground">
                    Late {lateByMin}m
                  </span>
                ) : null}
                <span
                  className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[9px] font-black uppercase tracking-wider ${
                    isOffSite ? "bg-amber-100 text-amber-800" : "bg-secondary text-secondary-foreground"
                  }`}
                >
                  <MapPin className="h-3 w-3" />
                  {isOffSite ? "Off-site" : "On-site"}
                  {distance != null ? ` ${formatDistance(distance)}` : ""}
                </span>
                {/* Only the failure is worth a badge. A clock-in that passed
                    the check looks like any other, and an org that never
                    turned the check on would otherwise get a badge on every
                    single row saying nothing. */}
                {ipAllowed === false ? (
                  <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-[9px] font-black uppercase tracking-wider text-amber-800">
                    <ShieldAlert className="h-3 w-3" />
                    Off-network
                  </span>
                ) : null}
              </div>
            </div>
            <button
              type="button"
              disabled
              className="grid h-7 w-7 shrink-0 place-items-center rounded-full text-muted-foreground opacity-80"
              aria-label={`Edit ${title}`}
            >
              <Pencil className="h-3.5 w-3.5" />
            </button>
          </div>

          <div className="mt-2 grid gap-1 text-[11px] leading-5">
            <div className="grid grid-cols-[3.75rem_1fr] gap-2">
              <span className="text-muted-foreground">Project</span>
              <span className="truncate font-semibold text-foreground">{projectName ?? "-"}</span>
            </div>
            <div className="grid grid-cols-[3.75rem_1fr] gap-2">
              <span className="text-muted-foreground">GPS</span>
              <span className="break-all font-semibold text-foreground">{location}</span>
            </div>
          </div>

          {photoUrl ? (
            <button
              type="button"
              onClick={() => openAttendancePhoto(photoUrl)}
              className="mt-3 inline-flex h-7 items-center gap-1.5 rounded-full bg-muted px-2.5 text-[11px] font-bold text-primary transition hover:bg-secondary"
            >
              <FileImage className="h-3 w-3" />
              View photo
            </button>
          ) : null}

          {/* This event on its own. Approving a clock-in here leaves the
              clock-out beside it pending, which the day-level "Approve all"
              cannot express. Hidden while selecting — the batch is the action
              then, and two approve paths on one row is one too many. */}
          {approvalId && !selectMode ? (
            <EventDecisionButtons
              busy={busyKey === approvalId}
              onApprove={() => onApprove(approvalId)}
              onReject={() => onReject(approvalId, title)}
            />
          ) : null}
        </div>
      </div>
    </div>
  );

  // In select mode the whole row is the target, matching the claim cards: a
  // 16px box inside a dense row is a poor thing to aim at on a phone.
  if (!selectable) return row;

  return (
    <div
      role="button"
      tabIndex={0}
      aria-pressed={ticked}
      onClick={() => onToggleSelect(approvalId)}
      onKeyDown={(event) => {
        if (event.key !== "Enter" && event.key !== " ") return;
        event.preventDefault();
        onToggleSelect(approvalId);
      }}
      className="cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 focus-visible:ring-offset-2 rounded-2xl"
    >
      {row}
    </div>
  );
}

// Shared by the clock and break rows, so one event decides the same way
// whichever kind it is.
function EventDecisionButtons({
  busy,
  onApprove,
  onReject,
}: {
  busy: boolean;
  onApprove: () => void;
  onReject: () => void;
}) {
  return (
    <div className="mt-3 flex items-center gap-2">
      <button
        type="button"
        disabled={busy}
        onClick={onApprove}
        className="inline-flex h-8 items-center justify-center gap-1.5 rounded-full bg-secondary px-3.5 text-[11px] font-bold text-secondary-foreground transition hover:opacity-90 disabled:opacity-50"
      >
        {busy ? <LoaderCircle className="h-3 w-3 animate-spin" /> : null}
        Approve
      </button>
      <button
        type="button"
        disabled={busy}
        onClick={onReject}
        className="inline-flex h-8 items-center justify-center rounded-full bg-destructive/10 px-3.5 text-[11px] font-bold text-destructive transition hover:bg-destructive/20 disabled:opacity-50"
      >
        Reject
      </button>
    </div>
  );
}

function RejectDialog({
  target,
  busy,
  notes,
  error,
  onNotesChange,
  onClose,
  onConfirm,
}: {
  target: RejectTarget;
  busy: boolean;
  notes: string;
  error: string | null;
  onNotesChange: (value: string) => void;
  onClose: () => void;
  onConfirm: () => void;
}) {
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/35 px-4 py-5 backdrop-blur-sm sm:items-center">
      <section className="w-full max-w-md rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)]">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Reject</p>
            <h2 className="mt-1 text-lg font-black text-foreground">{target.title}</h2>
            <p className="mt-1 text-xs text-muted-foreground">{target.subtitle}</p>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="grid h-9 w-9 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground"
            aria-label="Close reject dialog"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <textarea
          value={notes}
          onChange={(event) => onNotesChange(event.target.value)}
          rows={4}
          placeholder="Reason for rejection"
          className="mt-5 w-full resize-none rounded-2xl border border-border bg-white/80 px-4 py-3 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        />
        {error ? <p className="mt-2 text-xs font-medium text-destructive">{error}</p> : null}

        <div className="mt-5 grid grid-cols-2 gap-3">
          <button
            type="button"
            disabled={busy}
            onClick={onConfirm}
            className="inline-flex h-11 items-center justify-center rounded-2xl bg-destructive/10 px-4 text-sm font-bold text-destructive transition hover:bg-destructive/20 disabled:opacity-50"
          >
            Reject all
          </button>
          <button
            type="button"
            onClick={onClose}
            className="inline-flex h-11 items-center justify-center rounded-2xl bg-muted px-4 text-sm font-bold text-muted-foreground transition hover:text-foreground"
          >
            Cancel
          </button>
        </div>
      </section>
    </div>
  );
}
