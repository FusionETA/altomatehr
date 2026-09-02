import { useEffect, useMemo, useRef, useState } from "react";
import { CalendarClock, Camera, FileImage, Plus, Upload, X } from "lucide-react";
import {
  attachOvertimeAfterPhoto,
  createOvertime,
  getMyOvertime,
  openOvertimePhoto,
  uploadOvertimePhoto,
  type OvertimeRequest,
} from "../api";
import { OtRatePreview } from "./OtRatePreview";
import {
  overtimeMatchesStatus,
  overtimeStatusLabels,
  visibleOvertimeStatuses,
  type OvertimeStatusFilter,
} from "../lib/overtime-status";
import { getProjects, type Project } from "@/features/settings/api";
import { SearchInput } from "@/shared/components/SearchInput";
import { StatusFilterTabs } from "@/shared/components/StatusFilterTabs";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

const CARD = "rounded-2xl border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";
const NO_PROJECT = "__none__";

function fmtDate(value: string) {
  if (!value) return "-";
  const [year, month, day] = value.split("-").map(Number);
  return new Intl.DateTimeFormat("en-MY", {
    day: "2-digit",
    month: "short",
    year: "numeric",
  }).format(new Date(year, (month ?? 1) - 1, day ?? 1));
}

function fmtTime(value: string) {
  if (!value) return "-";
  return new Intl.DateTimeFormat("en-US", {
    hour: "2-digit",
    minute: "2-digit",
    hour12: true,
    timeZone: "Asia/Kuala_Lumpur",
  }).format(new Date(value));
}

function fmtDuration(minutes: number) {
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  if (h <= 0) return `${m}m`;
  return m > 0 ? `${h}h ${m}m` : `${h}h`;
}

function statusClass(status: OvertimeRequest["status"]) {
  if (status === "APPROVED") return "bg-secondary text-secondary-foreground";
  if (status === "REJECTED") return "bg-destructive/10 text-destructive";
  if (status === "CANCELLED") return "bg-muted text-muted-foreground";
  return "bg-warning text-warning-foreground";
}

function OvertimeStatusBadge({ status }: { status: OvertimeRequest["status"] }) {
  return (
    <span
      className={`inline-flex rounded-full px-3.5 py-1.5 text-[11px] font-bold uppercase tracking-[0.16em] ${statusClass(status)}`}
    >
      {overtimeStatusLabels[status]}
    </span>
  );
}

export function OvertimeView() {
  const [requests, setRequests] = useState<OvertimeRequest[]>([]);
  const [projects, setProjects] = useState<Project[]>([]);
  const [status, setStatus] = useState<OvertimeStatusFilter>("ALL");
  const [searchTerm, setSearchTerm] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [modalOpen, setModalOpen] = useState(false);

  function refresh() {
    setLoading(true);
    setError(null);
    Promise.all([getMyOvertime(), getProjects().catch(() => [])])
      .then(([nextRequests, nextProjects]) => {
        setRequests(nextRequests);
        setProjects(nextProjects.filter((project) => !project.isArchived));
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }

  useEffect(() => {
    refresh();
  }, []);

  const projectNames = useMemo(() => new Map(projects.map((project) => [project.id, project.name])), [projects]);
  const filteredRequests = useMemo(() => {
    const query = searchTerm.trim().toLowerCase();
    return requests.filter((request) => {
      if (!overtimeMatchesStatus(request, status)) return false;
      if (!query) return true;
      const projectName = request.projectId ? projectNames.get(request.projectId) : "";
      return [
        request.workDate,
        fmtTime(request.startAt),
        fmtTime(request.endAt),
        fmtDuration(request.requestedMinutes),
        overtimeStatusLabels[request.status],
        projectName,
        request.reason,
      ]
        .filter(Boolean)
        .join(" ")
        .toLowerCase()
        .includes(query);
    });
  }, [projectNames, requests, searchTerm, status]);

  return (
    <>
      <div className="space-y-4 sm:space-y-6">
        <section className="space-y-4">
          <StatusFilterTabs<OvertimeStatusFilter>
            statuses={visibleOvertimeStatuses}
            labels={overtimeStatusLabels}
            value={status}
            onChange={setStatus}
            ariaLabel="Overtime status filters"
          />
          <SearchInput
            value={searchTerm}
            onChange={setSearchTerm}
            placeholder="Search by project, reason, date, or status"
            inputClassName="h-10 rounded-xl border-border/70 bg-card/90 focus-visible:ring-primary focus-visible:ring-offset-0"
          />
          <p className="px-1 text-sm text-muted-foreground">
            Showing <span className="font-semibold text-foreground">{filteredRequests.length}</span> of{" "}
            <span className="font-semibold text-foreground">{requests.length}</span> overtime requests
          </p>
        </section>

        {loading ? (
          <section className={`${CARD} p-6 text-sm text-muted-foreground`}>Loading overtime...</section>
        ) : null}

        {error ? (
          <section className="rounded-2xl border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
            Error: {error}
          </section>
        ) : null}

        {!loading && !error && filteredRequests.length === 0 ? (
          <section className={`${CARD} p-8 text-center`}>
            <CalendarClock className="mx-auto h-6 w-6 text-muted-foreground" />
            <p className="mt-3 text-sm font-bold text-foreground">No overtime requests match this status.</p>
            <p className="mt-1 text-xs text-muted-foreground">Try another filter or tap plus to submit overtime.</p>
          </section>
        ) : null}

        {!loading && !error && filteredRequests.length > 0 ? (
          <div className="grid gap-3 sm:gap-4 lg:grid-cols-2">
            {filteredRequests.map((request) => (
              <OvertimeCard
                key={request.id}
                request={request}
                projectName={request.projectId ? projectNames.get(request.projectId) : undefined}
                onUpdated={(updated) =>
                  setRequests((current) => current.map((item) => (item.id === updated.id ? updated : item)))
                }
              />
            ))}
          </div>
        ) : null}
      </div>

      <button
        type="button"
        aria-label="Submit overtime"
        onClick={() => setModalOpen(true)}
        className="fixed bottom-32 right-5 z-40 flex h-14 w-14 items-center justify-center rounded-full bg-primary text-primary-foreground shadow-panel transition-transform hover:scale-105 active:scale-95 lg:bottom-8 lg:right-8"
      >
        <Plus className="h-6 w-6" />
      </button>

      {modalOpen ? (
        <NewOvertimeModal
          projects={projects}
          onClose={() => setModalOpen(false)}
          onCreated={(request) => {
            setRequests((current) => [request, ...current]);
            setModalOpen(false);
          }}
        />
      ) : null}
    </>
  );
}

function OvertimeCard({
  request,
  projectName,
  onUpdated,
}: {
  request: OvertimeRequest;
  projectName?: string;
  onUpdated: (request: OvertimeRequest) => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  async function attachAfter(file: File | null) {
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      const upload = await uploadOvertimePhoto(file);
      const updated = await attachOvertimeAfterPhoto(request.id, upload.photoUrl);
      onUpdated(updated);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not attach after photo.");
    } finally {
      setBusy(false);
      if (fileRef.current) fileRef.current.value = "";
    }
  }

  return (
    <article className={`${CARD} space-y-3 p-3.5 sm:p-4`}>
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-[10px] uppercase tracking-[0.16em] text-muted-foreground">{fmtDate(request.workDate)}</p>
          <p className="mt-0.5 text-sm font-black text-foreground">{fmtDuration(request.requestedMinutes)}</p>
          <p className="mt-0.5 truncate text-[11px] text-muted-foreground">
            {fmtTime(request.startAt)} - {fmtTime(request.endAt)}
          </p>
        </div>
        <OvertimeStatusBadge status={request.status} />
      </div>

      <div className="grid grid-cols-2 gap-3 rounded-2xl bg-surface-low px-3 py-2.5">
        <div>
          <p className="text-[10px] uppercase tracking-[0.16em] text-muted-foreground">Project</p>
          <p className="mt-0.5 truncate text-sm font-bold text-foreground">{projectName ?? "-"}</p>
        </div>
        <div>
          <p className="text-[10px] uppercase tracking-[0.16em] text-muted-foreground">Status</p>
          <p className="mt-0.5 truncate text-sm font-bold text-foreground">{overtimeStatusLabels[request.status]}</p>
        </div>
      </div>

      <div>
        <p className="line-clamp-2 text-sm font-semibold leading-5 text-foreground">{request.reason}</p>

        <div className="mt-2 flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => openOvertimePhoto(request.beforePhotoUrl)}
            className="inline-flex h-7 items-center gap-1.5 rounded-full bg-muted px-2.5 text-[11px] font-bold text-primary transition hover:bg-secondary"
          >
            <FileImage className="h-3 w-3" />
            Before photo
          </button>
          {request.afterPhotoUrl ? (
            <button
              type="button"
              onClick={() => openOvertimePhoto(request.afterPhotoUrl!)}
              className="inline-flex h-7 items-center gap-1.5 rounded-full bg-muted px-2.5 text-[11px] font-bold text-primary transition hover:bg-secondary"
            >
              <FileImage className="h-3 w-3" />
              After photo
            </button>
          ) : request.status === "PENDING" ? (
            <>
              <input
                ref={fileRef}
                type="file"
                accept="image/*"
                className="hidden"
                onChange={(event) => attachAfter(event.target.files?.[0] ?? null)}
              />
              <button
                type="button"
                disabled={busy}
                onClick={() => fileRef.current?.click()}
                className="inline-flex h-7 items-center gap-1.5 rounded-full bg-primary/10 px-2.5 text-[11px] font-bold text-primary transition hover:bg-primary/15 disabled:opacity-50"
              >
                <Camera className="h-3 w-3" />
                {busy ? "Uploading..." : "Attach after"}
              </button>
            </>
          ) : null}
        </div>
      </div>

      {request.reviewNotes ? (
        <div className="rounded-2xl border border-border/60 bg-card/70 p-3">
          <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">Reviewer note</p>
          <p className="mt-1 text-xs leading-5 text-muted-foreground">{request.reviewNotes}</p>
        </div>
      ) : null}
      {error ? <p className="text-xs font-medium text-destructive">{error}</p> : null}
    </article>
  );
}

function NewOvertimeModal({
  projects,
  onClose,
  onCreated,
}: {
  projects: Project[];
  onClose: () => void;
  onCreated: (request: OvertimeRequest) => void;
}) {
  const [projectId, setProjectId] = useState(NO_PROJECT);
  const [workDate, setWorkDate] = useState(() => new Date().toISOString().slice(0, 10));
  const [startTime, setStartTime] = useState("");
  const [endTime, setEndTime] = useState("");
  const [reason, setReason] = useState("");
  const [beforePhoto, setBeforePhoto] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    if (!workDate || !startTime || !endTime || !reason.trim() || !beforePhoto) return;
    setBusy(true);
    setError(null);
    try {
      const upload = await uploadOvertimePhoto(beforePhoto);
      const request = await createOvertime({
        projectId: projectId === NO_PROJECT ? undefined : projectId,
        workDate: `${workDate}T00:00:00`,
        startAt: `${workDate}T${startTime}:00`,
        endAt: `${workDate}T${endTime}:00`,
        reason: reason.trim(),
        beforePhotoUrl: upload.photoUrl,
      });
      onCreated(request);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not submit overtime.");
    } finally {
      setBusy(false);
    }
  }

  const canSubmit = Boolean(workDate && startTime && endTime && reason.trim() && beforePhoto && !busy);

  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-black/35 px-4 py-5 backdrop-blur-sm sm:items-center">
      <section className="max-h-[calc(100vh-2.5rem)] w-full max-w-xl overflow-y-auto rounded-[28px] border border-border/70 bg-card p-5 shadow-[0_24px_70px_rgba(32,10,55,0.24)] sm:p-6">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">Overtime</p>
            <h2 className="mt-1 text-xl font-black text-foreground">Submit overtime</h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="grid h-10 w-10 shrink-0 place-items-center rounded-full border border-border/60 bg-card text-muted-foreground transition hover:text-foreground"
            aria-label="Close overtime form"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="mt-5 grid gap-4">
          <label className="grid gap-1.5">
            <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">Project</span>
            <Select value={projectId} onValueChange={setProjectId}>
              <SelectTrigger>
                <SelectValue placeholder="No project" />
              </SelectTrigger>
              <SelectContent searchPlaceholder="Search projects...">
                <SelectItem value={NO_PROJECT}>No project</SelectItem>
                {projects.map((project) => (
                  <SelectItem key={project.id} value={project.id}>
                    {project.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </label>

          <div className="grid gap-4 sm:grid-cols-3">
            <label className="grid gap-1.5">
              <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">Date</span>
              <input
                type="date"
                value={workDate}
                onChange={(event) => setWorkDate(event.target.value)}
                className="h-12 rounded-2xl border border-border bg-white/80 px-4 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              />
            </label>
            <label className="grid gap-1.5">
              <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">Start</span>
              <input
                type="time"
                value={startTime}
                onChange={(event) => setStartTime(event.target.value)}
                className="h-12 rounded-2xl border border-border bg-white/80 px-4 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              />
            </label>
            <label className="grid gap-1.5">
              <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">End</span>
              <input
                type="time"
                value={endTime}
                onChange={(event) => setEndTime(event.target.value)}
                className="h-12 rounded-2xl border border-border bg-white/80 px-4 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              />
            </label>
          </div>

          <OtRatePreview
            workDate={workDate}
            projectId={projectId === NO_PROJECT ? undefined : projectId}
          />

          <label className="grid gap-1.5">
            <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">Reason</span>
            <textarea
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              rows={4}
              placeholder="What work requires overtime?"
              className="resize-none rounded-2xl border border-border bg-white/80 px-4 py-3 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            />
          </label>

          <label className="grid gap-1.5">
            <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">Before photo</span>
            <input
              type="file"
              accept="image/*"
              className="hidden"
              onChange={(event) => setBeforePhoto(event.target.files?.[0] ?? null)}
            />
            <span className="flex h-12 cursor-pointer items-center gap-3 rounded-2xl border border-border bg-white/80 px-4 text-sm text-muted-foreground shadow-sm transition hover:border-primary/40">
              <Upload className="h-4 w-4 shrink-0 text-primary" />
              <span className="min-w-0 flex-1 truncate">
                {beforePhoto ? beforePhoto.name : "Choose before-work photo"}
              </span>
            </span>
          </label>
        </div>

        {error ? <p className="mt-4 text-sm font-medium text-destructive">{error}</p> : null}

        <div className="mt-6 grid gap-3 sm:grid-cols-2">
          <button
            type="button"
            disabled={!canSubmit}
            onClick={submit}
            className="inline-flex h-12 items-center justify-center rounded-2xl bg-primary px-5 text-sm font-bold text-primary-foreground shadow-sm transition hover:bg-primary/90 disabled:opacity-50"
          >
            {busy ? "Submitting..." : "Submit overtime"}
          </button>
          <button
            type="button"
            onClick={onClose}
            className="inline-flex h-12 items-center justify-center rounded-2xl bg-muted px-5 text-sm font-bold text-muted-foreground transition hover:text-foreground"
          >
            Cancel
          </button>
        </div>
      </section>
    </div>
  );
}
