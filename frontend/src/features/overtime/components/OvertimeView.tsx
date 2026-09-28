import { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { FloatingActionButton } from "@/shared/components/FloatingActionButton";
import { SkeletonCards } from "@/shared/components/Skeleton";
import { CalendarClock, Camera, Plus, Upload, X } from "lucide-react";
import {
  attachOvertimeAfterFiles,
  createOvertime,
  getMyOvertime,
  OVERTIME_FILE_ACCEPT,
  OVERTIME_MAX_FILES,
  removeOvertimeAttachment,
  uploadOvertimeFiles,
  type OvertimeAttachment,
  type OvertimeRequest,
} from "../api";
import { OvertimeFileList } from "./OvertimeFileList";
import { useConfirm } from "@/shared/components/ConfirmDialog";
import { OtRatePreview } from "./OtRatePreview";
import { OvertimeStatusBadge } from "./OvertimeStatusBadge";
import {
  overtimeMatchesStatus,
  overtimeStatusLabels,
  visibleOvertimeStatuses,
  type OvertimeStatusFilter,
} from "../lib/overtime-status";
import { getMyProjects, getProjects, type Project } from "@/features/settings/api";
import { SearchInput } from "@/shared/components/SearchInput";
import { StatusFilterTabs } from "@/shared/components/StatusFilterTabs";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { businessToday } from "@/shared/lib/business-day";

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

export function OvertimeView() {
  const [status, setStatus] = useState<OvertimeStatusFilter>("ALL");
  const [searchTerm, setSearchTerm] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [modalOpen, setModalOpen] = useState(false);

  // Both cached. New requests are pushed onto the list locally (see the submit
  // handler below), so this only needs to load once per visit — and on a
  // revisit it doesn't need to load at all.
  const requestsQuery = useCachedQuery("/overtime", getMyOvertime);
  // The PICKER offers only the employee's own projects. The project decides
  // which team approves the request and which site the hours are costed to, so
  // offering one they aren't on produces overtime that routes through a
  // fallback team. The server refuses it too. Same rule as claims and
  // attendance.
  const projectsQuery = useCachedQuery("/projects/mine", getMyProjects);

  // Names for the HISTORY rows, which is a different question: a request filed
  // before the employee left that team still has to render its project name
  // rather than a dash. Lookup only — never rendered as a list of choices.
  const allProjectsQuery = useCachedQuery("/projects", getProjects);
  const loading = requestsQuery.loading || projectsQuery.loading;

  // Seeded from the query on the FIRST render, not copied in by an effect.
  //
  // useCachedQuery already has the cached answer during that render, but an
  // effect only runs after the paint — so a revisit rendered one frame of the
  // empty state ("No overtime requests") before the list appeared. That frame
  // is the blink.
  //
  // Still state rather than derived, because approving and submitting update
  // the list in place; the effect below keeps it in step with a background
  // refresh.
  const [requests, setRequests] = useState<OvertimeRequest[]>(() => requestsQuery.data ?? []);

  useEffect(() => {
    if (requestsQuery.data) setRequests(requestsQuery.data);
  }, [requestsQuery.data]);

  // Derived outright — nothing here mutates the project list, so there is no
  // reason for it to be a second copy that can lag.
  const projects = useMemo(
    () => (projectsQuery.data ?? []).filter((project) => !project.isArchived),
    [projectsQuery.data],
  );
  useEffect(() => {
    // A missing project list only costs labels, so it isn't worth an error
    // banner over the requests themselves.
    if (requestsQuery.error) setError(requestsQuery.error);
  }, [requestsQuery.error]);

  const projectNames = useMemo(
    () => new Map((allProjectsQuery.data ?? []).map((project) => [project.id, project.name])),
    [allProjectsQuery.data],
  );
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
          <SkeletonCards />
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

      <FloatingActionButton label="Submit overtime" onClick={() => setModalOpen(true)}>
        <Plus className="h-6 w-6" />
      </FloatingActionButton>

      {modalOpen ? (
        <NewOvertimeModal
          projects={projects}
          projectsLoading={projectsQuery.loading}
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
  const [removingId, setRemovingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const [confirm, confirmDialog] = useConfirm();

  const pending = request.status === "PENDING";
  const after = request.afterAttachments ?? [];
  const room = OVERTIME_MAX_FILES - after.length;

  // Several at once, added to what's already there — the evidence often
  // arrives as a handful of site photos plus a signed job sheet.
  async function attachAfter(list: FileList | null) {
    const files = Array.from(list ?? []);
    if (files.length === 0) return;
    if (files.length > room) {
      setError(`You can add ${room} more after-work ${room === 1 ? "file" : "files"} (up to ${OVERTIME_MAX_FILES}).`);
      if (fileRef.current) fileRef.current.value = "";
      return;
    }
    setBusy(true);
    setError(null);
    try {
      onUpdated(await attachOvertimeAfterFiles(request.id, await uploadOvertimeFiles(files)));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not attach those files.");
    } finally {
      setBusy(false);
      if (fileRef.current) fileRef.current.value = "";
    }
  }

  async function removeAfter(file: OvertimeAttachment) {
    const ok = await confirm({
      title: "Remove this file?",
      message: `"${file.fileName}" will be taken off this request and deleted.`,
      confirmLabel: "Remove",
      destructive: true,
    });
    if (!ok) return;
    setRemovingId(file.id);
    setError(null);
    try {
      onUpdated(await removeOvertimeAttachment(request.id, file.id));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not remove that file.");
    } finally {
      setRemovingId(null);
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

        <div className="mt-3 space-y-2.5">
          <div>
            <p className="mb-1 text-[10px] uppercase tracking-[0.16em] text-muted-foreground">
              Before · {request.beforeAttachments?.length ?? 0}
            </p>
            <OvertimeFileList files={request.beforeAttachments ?? []} />
          </div>

          <div>
            <p className="mb-1 text-[10px] uppercase tracking-[0.16em] text-muted-foreground">
              After · {after.length}
            </p>
            {after.length > 0 ? (
              <OvertimeFileList
                files={after}
                onRemove={pending ? (file) => void removeAfter(file) : undefined}
                removingId={removingId}
              />
            ) : pending ? (
              <p className="text-[11px] text-muted-foreground">
                None yet — at least one is needed before this can be approved.
              </p>
            ) : (
              <p className="text-[11px] text-muted-foreground">None attached.</p>
            )}

            {pending && room > 0 ? (
              <>
                <input
                  ref={fileRef}
                  type="file"
                  multiple
                  accept={OVERTIME_FILE_ACCEPT}
                  className="hidden"
                  onChange={(event) => void attachAfter(event.target.files)}
                />
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => fileRef.current?.click()}
                  className="mt-2 inline-flex h-7 items-center gap-1.5 rounded-full bg-primary/10 px-2.5 text-[11px] font-bold text-primary transition hover:bg-primary/15 disabled:opacity-50"
                >
                  <Camera className="h-3 w-3" />
                  {busy ? "Uploading..." : after.length > 0 ? "Add more" : "Attach after"}
                </button>
              </>
            ) : null}
          </div>
        </div>
      </div>

      {request.reviewNotes ? (
        <div className="rounded-2xl border border-border/60 bg-card/70 p-3">
          <p className="text-[11px] uppercase tracking-[0.16em] text-muted-foreground">Reviewer note</p>
          <p className="mt-1 text-xs leading-5 text-muted-foreground">{request.reviewNotes}</p>
        </div>
      ) : null}
      {error ? <p className="text-xs font-medium text-destructive">{error}</p> : null}
      {confirmDialog}
    </article>
  );
}

function NewOvertimeModal({
  projects,
  projectsLoading,
  onClose,
  onCreated,
}: {
  projects: Project[];
  /** Still arriving, so an empty list isn't yet "on no project". */
  projectsLoading: boolean;
  onClose: () => void;
  onCreated: (request: OvertimeRequest) => void;
}) {
  const [projectId, setProjectId] = useState(NO_PROJECT);
  const [workDate, setWorkDate] = useState(() => businessToday());
  const [startTime, setStartTime] = useState("");
  const [endTime, setEndTime] = useState("");
  const [reason, setReason] = useState("");
  // Picked but not yet uploaded; uploaded together on submit.
  const [beforeFiles, setBeforeFiles] = useState<File[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Hidden entirely when they're on no project — an empty dropdown asks a
  // question with no answers, and requiring one would lock an unassigned
  // employee out of claiming overtime. Required for everyone else.
  //
  // "Still loading" is not "on no project": treating them the same made the
  // field appear into the middle of an already-drawn form. The shell warms
  // /projects/mine so this is normally false on the first render.
  const projectRequired = projectsLoading || projects.length > 0;

  async function submit() {
    if (!workDate || !startTime || !endTime || !reason.trim() || beforeFiles.length === 0) return;
    if (projects.length > 0 && projectId === NO_PROJECT) {
      setError("Pick the project this overtime is for.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const beforeAttachments = await uploadOvertimeFiles(beforeFiles);
      const request = await createOvertime({
        projectId: projectId === NO_PROJECT ? undefined : projectId,
        workDate: `${workDate}T00:00:00`,
        startAt: `${workDate}T${startTime}:00`,
        endAt: `${workDate}T${endTime}:00`,
        reason: reason.trim(),
        beforeAttachments,
      });
      onCreated(request);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not submit overtime.");
    } finally {
      setBusy(false);
    }
  }

  const canSubmit = Boolean(
    workDate && startTime && endTime && reason.trim() && beforeFiles.length > 0 && !busy,
  );

  // Choosing again ADDS to the list (a second trip to the gallery shouldn't
  // drop the first pick); the same file twice is kept once.
  function addBeforeFiles(list: FileList | null) {
    const merged = [...beforeFiles];
    for (const file of Array.from(list ?? [])) {
      if (!merged.some((f) => f.name === file.name && f.size === file.size)) merged.push(file);
    }
    const over = merged.length > OVERTIME_MAX_FILES;
    setError(over ? `You can attach up to ${OVERTIME_MAX_FILES} before-work files.` : null);
    setBeforeFiles(over ? merged.slice(0, OVERTIME_MAX_FILES) : merged);
  }

  return createPortal(
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
          {projectRequired ? (
            <label className="grid gap-1.5">
              <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">Project</span>
              <Select value={projectId} onValueChange={setProjectId} disabled={projectsLoading}>
                <SelectTrigger>
                  <SelectValue placeholder={projectsLoading ? "Loading projects…" : "Select project"} />
                </SelectTrigger>
                <SelectContent searchPlaceholder="Search projects...">
                  {projects.map((project) => (
                    <SelectItem key={project.id} value={project.id}>
                      {project.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </label>
          ) : null}

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

          <div className="grid gap-1.5">
            <label className="grid gap-1.5">
              <span className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">
                Before-work photos or files
              </span>
              <input
                type="file"
                multiple
                accept={OVERTIME_FILE_ACCEPT}
                className="hidden"
                disabled={beforeFiles.length >= OVERTIME_MAX_FILES}
                onChange={(event) => {
                  addBeforeFiles(event.target.files);
                  event.target.value = "";
                }}
              />
              <span className="flex h-12 cursor-pointer items-center gap-3 rounded-2xl border border-border bg-white/80 px-4 text-sm text-muted-foreground shadow-sm transition hover:border-primary/40">
                <Upload className="h-4 w-4 shrink-0 text-primary" />
                <span className="min-w-0 flex-1 truncate">
                  {beforeFiles.length === 0
                    ? "Choose photos or PDFs"
                    : beforeFiles.length >= OVERTIME_MAX_FILES
                      ? `${OVERTIME_MAX_FILES} files — the most you can attach`
                      : "Add more"}
                </span>
              </span>
            </label>
            {beforeFiles.length > 0 ? (
              <ul className="flex flex-wrap gap-2">
                {beforeFiles.map((file, index) => (
                  <li
                    key={`${file.name}-${file.size}`}
                    className="inline-flex max-w-full items-center gap-1 rounded-full bg-muted py-1 pl-3 pr-1 text-xs font-semibold text-foreground"
                  >
                    <span className="max-w-[12rem] truncate">{file.name}</span>
                    <button
                      type="button"
                      aria-label={`Remove ${file.name}`}
                      onClick={() => setBeforeFiles((cur) => cur.filter((_, i) => i !== index))}
                      className="flex h-5 w-5 items-center justify-center rounded-full text-muted-foreground transition hover:bg-background hover:text-destructive"
                    >
                      <X className="h-3 w-3" aria-hidden />
                    </button>
                  </li>
                ))}
              </ul>
            ) : null}
            <p className="text-[11px] text-muted-foreground">
              Photos (JPG, PNG, HEIC) or PDFs, up to {OVERTIME_MAX_FILES}, 8 MB each. They can't be
              changed after you submit.
            </p>
          </div>
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
    </div>,
    document.body,
  );
}
