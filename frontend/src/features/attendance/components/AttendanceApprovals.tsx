import { useEffect, useMemo, useState } from "react";
import { CalendarClock, CheckSquare, Coffee, ChevronDown, FileImage, LoaderCircle, MapPin, Pencil, PencilLine, X } from "lucide-react";
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
} from "../api";
import {
  approveOvertime,
  getTeamOvertime,
  openOvertimePhoto,
  rejectOvertime,
  type OvertimeRequest,
} from "@/features/overtime/api";
import {
  overtimeMatchesStatus,
  overtimeStatusLabels,
  visibleOvertimeStatuses,
  type OvertimeStatusFilter,
} from "@/features/overtime/lib/overtime-status";
import { getOrganization, getProjects, type Project } from "@/features/settings/api";
import { buildName } from "@/features/employee-portal/lib/employee-formatters";
import { SearchInput } from "@/shared/components/SearchInput";
import { StatusFilterTabs } from "@/shared/components/StatusFilterTabs";
import { formatDistance } from "@/shared/lib/geolocation";

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

function locationText(record: AttendanceRecord, type: "in" | "out") {
  const lat = type === "in" ? record.clockInLat : record.clockOutLat;
  const lng = type === "in" ? record.clockInLng : record.clockOutLng;
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

function eventCount(group: ApprovalGroup) {
  return group.records.reduce((sum, record) => sum + (record.timeIn ? 1 : 0) + (record.timeOut ? 1 : 0), 0);
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
  const [records, setRecords] = useState<AttendanceRecord[]>([]);
  const [projects, setProjects] = useState<Project[]>([]);
  const [radius, setRadius] = useState(200);
  const [filter, setFilter] = useState<DateFilter>("ALL");
  const [employeeSearch, setEmployeeSearch] = useState("");
  const [openKey, setOpenKey] = useState<string | null>(null);
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [rejectingGroup, setRejectingGroup] = useState<ApprovalGroup | null>(null);
  const [rejectNotes, setRejectNotes] = useState("");
  const [rejectError, setRejectError] = useState<string | null>(null);
  const [breaks, setBreaks] = useState<AttendanceApprovalRequest[]>([]);

  useEffect(() => {
    Promise.all([
      getTeamAttendanceApprovals(),
      getTeamBreakApprovals().catch(() => []),
      getProjects().catch(() => []),
      getOrganization().catch(() => null),
    ])
      .then(([nextRecords, nextBreaks, nextProjects, organization]) => {
        setRecords(nextRecords);
        setBreaks(nextBreaks);
        setProjects(nextProjects.filter((project) => !project.isArchived));
        if (organization) setRadius(organization.geofenceRadiusMeters);
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  const projectNames = useMemo(() => new Map(projects.map((project) => [project.id, project.name])), [projects]);
  const groups = useMemo(() => {
    const grouped = groupApprovals(filterRecords(records, filter), breaks);
    const query = employeeSearch.trim().toLowerCase();
    if (!query) return grouped;
    return grouped.filter((group) =>
      `${group.employeeName} ${group.employeeEmail ?? ""}`.toLowerCase().includes(query),
    );
  }, [records, breaks, filter, employeeSearch]);

  function toggleSelected(key: string) {
    setSelectedKeys((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  async function approveGroup(group: ApprovalGroup) {
    setBusyKey(group.key);
    setError(null);
    const recordIds = group.records.map((record) => record.id);
    // The decision endpoints take approval-request ids, not record ids.
    const requestIds = [...group.records.flatMap(pendingApprovalIds), ...group.breaks.map((b) => b.id)];
    if (requestIds.length === 0) {
      setError("Nothing pending on this day.");
      setBusyKey(null);
      return;
    }
    try {
      const result = await bulkApproveAttendance(requestIds);
      if (result.failed > 0) {
        // Partial success is normal here: another approver may have moved first.
        setError(firstBulkError(result) ?? `${result.failed} of ${requestIds.length} could not be approved.`);
      }
      setRecords((current) => current.filter((record) => !recordIds.includes(record.id)));
      setBreaks((current) => current.filter((b) => !requestIds.includes(b.id)));
      setSelectedKeys((current) => {
        const next = new Set(current);
        next.delete(group.key);
        return next;
      });
      if (openKey === group.key) setOpenKey(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not approve attendance.");
    } finally {
      setBusyKey(null);
    }
  }

  function openReject(group: ApprovalGroup) {
    setRejectingGroup(group);
    setRejectNotes("");
    setRejectError(null);
  }

  async function confirmReject() {
    if (!rejectingGroup) return;
    const notes = rejectNotes.trim();
    if (!notes) {
      setRejectError("Remark is required when rejecting attendance.");
      return;
    }

    setBusyKey(rejectingGroup.key);
    setError(null);
    const recordIds = rejectingGroup.records.map((record) => record.id);
    const requestIds = [
      ...rejectingGroup.records.flatMap(pendingApprovalIds),
      ...rejectingGroup.breaks.map((b) => b.id),
    ];
    if (requestIds.length === 0) {
      setRejectError("Nothing pending on this day.");
      setBusyKey(null);
      return;
    }
    try {
      const result = await bulkRejectAttendance(requestIds, notes);
      if (result.failed > 0) {
        setRejectError(firstBulkError(result) ?? `${result.failed} of ${requestIds.length} could not be rejected.`);
        setBusyKey(null);
        return;
      }
      setRecords((current) => current.filter((record) => !recordIds.includes(record.id)));
      setBreaks((current) => current.filter((b) => !requestIds.includes(b.id)));
      setRejectingGroup(null);
      setRejectNotes("");
      if (openKey === rejectingGroup.key) setOpenKey(null);
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
          </section>
        ) : null}

        {approvalType === "OVERTIME" ? <OvertimeApprovals projectNames={projectNames} /> : null}

        {approvalType === "ATTENDANCE" && loading ? (
          <section className={`${CARD} p-6 text-sm text-muted-foreground`}>Loading approvals...</section>
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
              return (
                <article
                  key={group.key}
                  className={`${CARD} overflow-hidden transition-colors ${open ? "border-primary/35" : ""}`}
                >
                  <GroupHeader
                    group={group}
                    open={open}
                    selected={selectedKeys.has(group.key)}
                    onToggleSelected={() => toggleSelected(group.key)}
                    onToggleOpen={() => setOpenKey(open ? null : group.key)}
                  />
                  {open ? (
                    <ExpandedGroup
                      group={group}
                      radius={radius}
                      projectNames={projectNames}
                      busy={busyKey === group.key}
                      selected={selectedKeys.has(group.key)}
                      onToggleSelected={() => toggleSelected(group.key)}
                      onApprove={() => approveGroup(group)}
                      onReject={() => openReject(group)}
                    />
                  ) : null}
                </article>
              );
            })}
          </section>
        ) : null}
      </div>

      {rejectingGroup ? (
        <RejectDialog
          group={rejectingGroup}
          busy={busyKey === rejectingGroup.key}
          notes={rejectNotes}
          error={rejectError}
          onNotesChange={setRejectNotes}
          onClose={() => setRejectingGroup(null)}
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
            className={`h-9 rounded-lg text-xs font-bold transition ${
              active
                ? "bg-primary text-primary-foreground shadow-sm"
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
  const [requests, setRequests] = useState<OvertimeRequest[]>([]);
  const [status, setStatus] = useState<OvertimeStatusFilter>("ALL");
  const [employeeSearch, setEmployeeSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [selectedRequest, setSelectedRequest] = useState<OvertimeRequest | null>(null);
  const [rejectingRequest, setRejectingRequest] = useState<OvertimeRequest | null>(null);
  const [rejectNotes, setRejectNotes] = useState("");
  const [rejectError, setRejectError] = useState<string | null>(null);

  useEffect(() => {
    getTeamOvertime()
      .then(setRequests)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  const filteredRequests = useMemo(() => {
    const query = employeeSearch.trim().toLowerCase();
    return requests.filter((request) => {
      if (!overtimeMatchesStatus(request, status)) return false;
      if (!query) return true;
      const employee = request.employeeEmail ? buildName(request.employeeEmail) : "Employee";
      return `${employee} ${request.employeeEmail ?? ""}`.toLowerCase().includes(query);
    });
  }, [requests, status, employeeSearch]);

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
        <p className="px-1 text-sm text-muted-foreground">
          Showing <span className="font-semibold text-foreground">{filteredRequests.length}</span> of{" "}
          <span className="font-semibold text-foreground">{requests.length}</span> overtime approvals
        </p>
      </section>

      {loading ? <section className={`${CARD} p-6 text-sm text-muted-foreground`}>Loading overtime approvals...</section> : null}

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
                onOpen={() => setSelectedRequest(request)}
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
  onOpen,
  onApprove,
  onReject,
}: {
  request: OvertimeRequest;
  employee: string;
  projectName: string | null | undefined;
  busy: boolean;
  onOpen: () => void;
  onApprove: () => void;
  onReject: () => void;
}) {
  const pending = request.status === "PENDING";

  return (
    <article className={`${CARD} overflow-hidden transition-colors hover:border-primary/35`}>
      <button type="button" onClick={onOpen} className="block w-full space-y-3 p-4 text-left">
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
        {pending ? (
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
  selected,
  onToggleSelected,
  onToggleOpen,
}: {
  group: ApprovalGroup;
  open: boolean;
  selected: boolean;
  onToggleSelected: () => void;
  onToggleOpen: () => void;
}) {
  const count = eventCount(group);
  const late = lateCount(group);
  const initials = group.employeeName
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0])
    .join("")
    .toUpperCase();

  return (
    <div className={`flex items-center gap-2.5 px-4 py-3.5 ${open ? "bg-primary/5" : ""}`}>
      <input
        type="checkbox"
        checked={selected}
        onChange={onToggleSelected}
        className="h-4 w-4 shrink-0 rounded border-border text-primary focus:ring-primary"
        aria-label={`Select ${group.employeeName}`}
      />
      <button type="button" onClick={onToggleOpen} className="flex min-w-0 flex-1 items-center gap-3 text-left">
        <span className="grid h-10 w-10 shrink-0 place-items-center rounded-2xl bg-primary/10 text-xs font-black text-primary">
          {initials || "E"}
        </span>
        <div className="min-w-0 flex-1 space-y-1">
          <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
            <p className="truncate text-[14px] font-black text-foreground">{group.employeeName}</p>
            <span className="rounded-full bg-surface-low px-2 py-0.5 text-[10px] font-bold text-muted-foreground">
              {group.date}
            </span>
          </div>
          <p className="truncate text-xs font-medium text-muted-foreground">
            {count} {count === 1 ? "event" : "events"} pending
          </p>
        </div>
        {late > 0 ? (
          <span className="shrink-0 rounded-full bg-warning px-2.5 py-1 text-[9px] font-bold uppercase tracking-wider text-warning-foreground">
            {late} late
          </span>
        ) : null}
        <span
          className={`grid h-8 w-8 shrink-0 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground transition ${
            open ? "border-primary/45 text-primary" : ""
          }`}
        >
          <ChevronDown className={`h-4 w-4 transition ${open ? "rotate-180" : ""}`} />
        </span>
      </button>
    </div>
  );
}

function ExpandedGroup({
  group,
  radius,
  projectNames,
  busy,
  selected,
  onToggleSelected,
  onApprove,
  onReject,
}: {
  group: ApprovalGroup;
  radius: number;
  projectNames: Map<string, string>;
  busy: boolean;
  selected: boolean;
  onToggleSelected: () => void;
  onApprove: () => void;
  onReject: () => void;
}) {
  const count = eventCount(group);

  return (
    <div className="border-t border-border/60 bg-surface-low/45 px-3.5 pb-3.5 pt-3">
      <div className="mb-2 flex items-center gap-2 px-1 text-[11px] font-bold uppercase tracking-[0.16em] text-muted-foreground">
        <CheckSquare className="h-3.5 w-3.5" />
        Pending events
      </div>
      <div className="space-y-2">
        {group.records.map((record) => (
          <div key={record.id} className="space-y-2">
            <AdjustmentNotice record={record} />
            {breakRowsFor(group, record, "in")}
            {record.timeIn ? (
              <EventRow
                title="Clock in"
                time={record.timeIn}
                lateByMin={record.lateByMin}
                distance={record.clockInDistanceMeters}
                radius={radius}
                projectName={record.projectId ? projectNames.get(record.projectId) : null}
                location={locationText(record, "in")}
                photoUrl={record.clockInPhotoUrl}
                selected={selected}
                onToggleSelected={onToggleSelected}
              />
            ) : null}
            {breakRowsFor(group, record, "mid")}
            {record.timeOut ? (
              <EventRow
                title="Clock out"
                time={record.timeOut}
                lateByMin={null}
                distance={record.clockOutDistanceMeters}
                radius={radius}
                projectName={record.projectId ? projectNames.get(record.projectId) : null}
                location={locationText(record, "out")}
                photoUrl={record.clockOutPhotoUrl}
                selected={selected}
                onToggleSelected={onToggleSelected}
              />
            ) : null}
          </div>
        ))}
      </div>

      <div className="mt-3 grid grid-cols-2 gap-2">
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

// A time-adjustment request on this record, if the employee asked for one.
//
// The clock recorded one time and the employee is asking for another; approving
// the record applies the corrected time, rejecting keeps what the clock said.
// That decision is the point of this card, so it leads rather than sits under
// the event rows.
function AdjustmentNotice({ record }: { record: AttendanceRecord }) {
  const asks = (record.approvals ?? []).filter(
    (a) => a.approvalStatus === "PENDING" && a.originalEventAt,
  );
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

// A day reads clock in -> break start -> break end -> clock out, so the break
// rows are placed by time rather than listed after the clock events.
//
// "in" renders anything before the clock-in (shouldn't happen, but a break with
// no matching clock event would otherwise vanish); "mid" renders the rest.
function breakRowsFor(
  group: ApprovalGroup,
  record: AttendanceRecord,
  slot: "in" | "mid",
) {
  const mine = group.breaks
    .filter((b) => b.attendanceRecordId === record.id)
    .sort((a, b) => a.eventAt.localeCompare(b.eventAt));

  const rows = mine.filter((b) =>
    slot === "in"
      ? record.timeIn != null && b.eventAt < record.timeIn
      : record.timeIn == null || b.eventAt >= record.timeIn,
  );

  return rows.map((brk) => (
    <BreakEventRow key={brk.id} request={brk} />
  ));
}

// Deliberately quieter than a clock event: a break carries no geofence or
// photo, and the supervisor is mostly checking the time and the reason.
function BreakEventRow({ request }: { request: AttendanceApprovalRequest }) {
  return (
    <div className="flex items-start gap-2.5 rounded-2xl border border-border/60 bg-card px-3.5 py-2.5">
      <Coffee className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" />
      <div className="min-w-0 flex-1">
        <p className="text-sm font-bold text-foreground">
          {request.kind === "BREAK_START" ? "Break start" : "Break end"}
          <span className="ml-2 text-xs font-semibold tabular-nums text-muted-foreground">
            {fmtTime(request.eventAt)}
          </span>
        </p>
        {request.reason ? (
          <p className="mt-0.5 text-xs text-muted-foreground">&ldquo;{request.reason}&rdquo;</p>
        ) : null}
      </div>
    </div>
  );
}

function EventRow({
  title,
  time,
  lateByMin,
  distance,
  radius,
  projectName,
  location,
  photoUrl,
  selected,
  onToggleSelected,
}: {
  title: string;
  time: string;
  lateByMin: number | null;
  distance: number | null;
  radius: number;
  projectName: string | null | undefined;
  location: string;
  photoUrl: string | null;
  selected: boolean;
  onToggleSelected: () => void;
}) {
  const isOffSite = offSite(distance, radius);

  return (
    <div className="rounded-2xl border border-border/60 bg-card px-3.5 py-3 shadow-sm">
      <div className="flex items-start gap-2.5">
        <input
          type="checkbox"
          checked={selected}
          onChange={onToggleSelected}
          className="mt-1 h-4 w-4 shrink-0 rounded border-border text-primary focus:ring-primary"
          aria-label={`Select ${title}`}
        />
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
        </div>
      </div>
    </div>
  );
}

function RejectDialog({
  group,
  busy,
  notes,
  error,
  onNotesChange,
  onClose,
  onConfirm,
}: {
  group: ApprovalGroup;
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
            <h2 className="mt-1 text-lg font-black text-foreground">{group.employeeName}</h2>
            <p className="mt-1 text-xs text-muted-foreground">
              {group.date} · {eventCount(group)} events
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
