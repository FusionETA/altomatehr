import { useEffect, useState } from "react";
import { Crosshair, LoaderCircle, MapPin, Plus, RefreshCw, ShieldCheck } from "lucide-react";
import {
  archiveProject,
  createProject,
  getMyIp,
  getProjects,
  getXeroStatus,
  restoreProject,
  syncXeroProjects,
  updateProject,
  type Project,
} from "../api";
import { requestGeolocation } from "@/shared/lib/geolocation";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonPanel } from "@/shared/components/Skeleton";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50";

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

// ISO weekday numbers, 1 = Monday … 7 = Sunday — the format the schedule is
// stored in (a CSV like "1,2,3,4,5").
const DAYS = [
  { n: 1, label: "Mon" },
  { n: 2, label: "Tue" },
  { n: 3, label: "Wed" },
  { n: 4, label: "Thu" },
  { n: 5, label: "Fri" },
  { n: 6, label: "Sat" },
  { n: 7, label: "Sun" },
] as const;

function parseDays(csv: string | null | undefined): Set<number> {
  const out = new Set<number>();
  for (const part of (csv ?? "").split(",")) {
    const n = Number(part.trim());
    if (n >= 1 && n <= 7) out.add(n);
  }
  return out;
}

export function ProjectsSettings() {
  const [projects, setProjects] = useState<Project[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [adding, setAdding] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);

  // Per-row editor: geofence location + IP allowlist.
  const [editingId, setEditingId] = useState<string | null>(null);
  const [lat, setLat] = useState("");
  const [lng, setLng] = useState("");
  const [ips, setIps] = useState("");
  const [ipLoading, setIpLoading] = useState(false);
  const [whStart, setWhStart] = useState("");
  const [whEnd, setWhEnd] = useState("");
  const [workDays, setWorkDays] = useState<Set<number>>(new Set());
  const [lunch, setLunch] = useState("60");
  const [savingLoc, setSavingLoc] = useState(false);
  const [locating, setLocating] = useState(false);
  const [locError, setLocError] = useState<string | null>(null);

  // Xero sync — the button only shows when a connection exists, so we never
  // offer a sync that can only fail.
  const [xeroConnected, setXeroConnected] = useState(false);
  const [syncing, setSyncing] = useState(false);
  const [syncMsg, setSyncMsg] = useState<string | null>(null);

  const query = useCachedQuery("/projects", getProjects);
  const loading = query.loading;
  useEffect(() => {
    if (query.data) setProjects(query.data);
  }, [query.data]);
  useEffect(() => {
    if (query.error) setError(query.error);
  }, [query.error]);
  useEffect(() => {
    getXeroStatus()
      .then((s) => setXeroConnected(s.connected))
      .catch(() => setXeroConnected(false));
  }, []);

  async function handleSync() {
    setSyncing(true);
    setSyncMsg(null);
    setError(null);
    try {
      const r = await syncXeroProjects();
      setSyncMsg(
        `Synced from Xero — ${r.imported} added, ${r.updated} updated` +
          (r.skipped ? `, ${r.skipped} unchanged.` : "."),
      );
      await query.refresh();
    } catch (err) {
      setError(message(err, "Could not sync projects from Xero."));
    } finally {
      setSyncing(false);
    }
  }

  async function handleAdd(e: React.FormEvent) {
    e.preventDefault();
    const trimmed = name.trim();
    if (!trimmed) return;
    setAdding(true);
    setError(null);
    try {
      const created = await createProject({ name: trimmed });
      setProjects((current) => [...current, created]);
      setName("");
    } catch (err) {
      setError(message(err, "Could not add the project."));
    } finally {
      setAdding(false);
    }
  }

  async function toggleArchive(project: Project) {
    setBusyId(project.id);
    setError(null);
    try {
      const updated = project.isArchived
        ? await restoreProject(project.id)
        : await archiveProject(project.id);
      setProjects((current) => current.map((p) => (p.id === updated.id ? updated : p)));
    } catch (err) {
      setError(message(err, "Could not update the project."));
    } finally {
      setBusyId(null);
    }
  }

  function openEditor(project: Project) {
    setEditingId(project.id);
    setLat(project.latitude != null ? String(project.latitude) : "");
    setLng(project.longitude != null ? String(project.longitude) : "");
    setIps(project.allowedIps ?? "");
    setWhStart(project.workingHoursStart ?? "");
    setWhEnd(project.workingHoursEnd ?? "");
    setWorkDays(parseDays(project.workingDays));
    setLunch(String(project.lunchBreakMinutes ?? 60));
    setLocError(null);
  }

  async function useMyLocation() {
    setLocating(true);
    setLocError(null);
    try {
      const coords = await requestGeolocation();
      setLat(coords.lat.toFixed(6));
      setLng(coords.lng.toFixed(6));
    } catch (err) {
      setLocError(message(err, "Couldn't get your location."));
    } finally {
      setLocating(false);
    }
  }

  async function useMyIp() {
    setIpLoading(true);
    setLocError(null);
    try {
      const { ip } = await getMyIp();
      if (!ip) {
        setLocError("Couldn't determine your IP address.");
        return;
      }
      setIps((current) => {
        const parts = current.split(",").map((p) => p.trim()).filter(Boolean);
        return parts.includes(ip) ? current : [...parts, ip].join(", ");
      });
    } catch (err) {
      setLocError(message(err, "Couldn't determine your IP address."));
    } finally {
      setIpLoading(false);
    }
  }

  async function saveLocation(project: Project) {
    const latEmpty = lat.trim() === "";
    const lngEmpty = lng.trim() === "";
    if (latEmpty !== lngEmpty) {
      setLocError("Enter both latitude and longitude, or clear both to remove the geofence.");
      return;
    }
    const latNum = latEmpty ? null : Number(lat);
    const lngNum = lngEmpty ? null : Number(lng);
    if (
      latNum !== null &&
      (Number.isNaN(latNum) || Number.isNaN(lngNum!) ||
        latNum < -90 || latNum > 90 || lngNum! < -180 || lngNum! > 180)
    ) {
      setLocError("Latitude must be −90…90 and longitude −180…180.");
      return;
    }

    setSavingLoc(true);
    setLocError(null);
    try {
      const updated = await updateProject(project.id, {
        name: project.name,
        latitude: latNum,
        longitude: lngNum,
        allowedIps: ips.trim() === "" ? null : ips.trim(),
        workingHoursStart: whStart.trim() === "" ? null : whStart.trim(),
        workingHoursEnd: whEnd.trim() === "" ? null : whEnd.trim(),
        workingDays: workDays.size > 0 ? [...workDays].sort((a, b) => a - b).join(",") : null,
        lunchBreakMinutes: Number(lunch) || 0,
      });
      setProjects((current) => current.map((p) => (p.id === updated.id ? updated : p)));
      setEditingId(null);
    } catch (err) {
      setLocError(message(err, "Could not save the project."));
    } finally {
      setSavingLoc(false);
    }
  }

  return (
    <div className={`${CARD} space-y-5`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-lg font-black text-foreground">Projects</h2>
          <p className="text-sm text-muted-foreground">
            Projects that claims (and later attendance/leave) are filed against. Give a project a
            location to geofence clock-ins, or an IP allowlist to restrict where staff clock in
            from.
          </p>
        </div>
        {xeroConnected ? (
          <button
            type="button"
            onClick={handleSync}
            disabled={syncing}
            className="inline-flex shrink-0 items-center gap-2 rounded-2xl border border-border/60 bg-card px-4 py-2 text-sm font-semibold text-foreground transition hover:bg-muted disabled:opacity-50"
          >
            {syncing ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
            Sync from Xero
          </button>
        ) : null}
      </div>
      {syncMsg ? <p className="text-sm font-medium text-primary">{syncMsg}</p> : null}

      <form onSubmit={handleAdd} className="flex gap-2">
        <input
          className={INPUT}
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="New project name"
        />
        <button
          type="submit"
          disabled={adding || !name.trim()}
          className="inline-flex shrink-0 items-center gap-2 rounded-2xl bg-primary px-4 text-sm font-semibold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
        >
          {adding ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
          Add
        </button>
      </form>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {loading ? (
        <SkeletonPanel />
      ) : projects.length === 0 ? (
        <p className="text-sm text-muted-foreground">No projects yet.</p>
      ) : (
        <ul className="divide-y divide-border/60 overflow-hidden rounded-2xl border border-border/60">
          {projects.map((project) => {
            const geofenced = project.latitude != null && project.longitude != null;
            return (
              <li key={project.id} className="px-4 py-3">
                <div className="flex items-center justify-between gap-3">
                  <div className="min-w-0">
                    <p
                      className={`truncate font-semibold ${
                        project.isArchived ? "text-muted-foreground line-through" : "text-foreground"
                      }`}
                    >
                      {project.name}
                    </p>
                    <div className="mt-0.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
                      <span
                        className={`inline-flex items-center gap-1 ${
                          geofenced ? "text-primary" : "text-muted-foreground"
                        }`}
                      >
                        <MapPin className="h-3 w-3" />
                        {geofenced
                          ? `${project.latitude!.toFixed(5)}, ${project.longitude!.toFixed(5)}`
                          : "No geofence"}
                      </span>
                      {project.allowedIps ? (
                        <span className="inline-flex items-center gap-1 text-primary">
                          <ShieldCheck className="h-3 w-3" /> IP allowlist
                        </span>
                      ) : null}
                      {project.workingHoursStart && project.workingHoursEnd ? (
                        <span className="text-muted-foreground">
                          {project.workingHoursStart}–{project.workingHoursEnd}
                        </span>
                      ) : null}
                      {project.xeroProjectId ? (
                        <span className="text-muted-foreground">From Xero</span>
                      ) : null}
                      {project.isArchived ? <span className="text-muted-foreground">Archived</span> : null}
                    </div>
                  </div>
                  <div className="flex shrink-0 items-center gap-2">
                    <button
                      type="button"
                      onClick={() => (editingId === project.id ? setEditingId(null) : openEditor(project))}
                      className="rounded-full border border-border/60 bg-card px-4 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground"
                    >
                      {editingId === project.id ? "Close" : "Edit"}
                    </button>
                    <button
                      type="button"
                      disabled={busyId === project.id}
                      onClick={() => toggleArchive(project)}
                      className="rounded-full border border-border/60 bg-card px-4 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground disabled:opacity-50"
                    >
                      {project.isArchived ? "Restore" : "Archive"}
                    </button>
                  </div>
                </div>

                {editingId === project.id ? (
                  <div className="mt-3 space-y-3 rounded-2xl border border-border/60 bg-background/60 p-3">
                    <div className="grid gap-2 sm:grid-cols-2">
                      <input
                        className={INPUT}
                        type="number"
                        step="any"
                        value={lat}
                        onChange={(e) => setLat(e.target.value)}
                        placeholder="Latitude (e.g. 3.1578)"
                      />
                      <input
                        className={INPUT}
                        type="number"
                        step="any"
                        value={lng}
                        onChange={(e) => setLng(e.target.value)}
                        placeholder="Longitude (e.g. 101.7123)"
                      />
                    </div>

                    <div>
                      <div className="flex items-center justify-between gap-2">
                        <label className="text-xs font-semibold text-muted-foreground">IP allowlist</label>
                        <button
                          type="button"
                          onClick={useMyIp}
                          disabled={ipLoading}
                          className="inline-flex items-center gap-1.5 rounded-full border border-border/60 bg-card px-3 py-1 text-xs font-semibold text-foreground transition hover:bg-muted disabled:opacity-50"
                        >
                          {ipLoading ? (
                            <LoaderCircle className="h-3 w-3 animate-spin" />
                          ) : (
                            <Crosshair className="h-3 w-3" />
                          )}
                          Use my IP
                        </button>
                      </div>
                      <input
                        className={`${INPUT} mt-1`}
                        value={ips}
                        onChange={(e) => setIps(e.target.value)}
                        placeholder="e.g. 203.106.51.10, 118.100.0.0/16"
                      />
                      <p className="mt-1 text-xs text-muted-foreground">
                        Comma-separated IPs or CIDR ranges. Only enforced for employees whose policy
                        requires it; leave blank for none.
                      </p>
                    </div>

                    <div className="space-y-2">
                      <label className="text-xs font-semibold text-muted-foreground">Work schedule</label>
                      <div className="grid gap-2 sm:grid-cols-3">
                        <label className="block">
                          <span className="text-xs text-muted-foreground">Start</span>
                          <input
                            type="time"
                            className={`${INPUT} mt-1`}
                            value={whStart}
                            onChange={(e) => setWhStart(e.target.value)}
                          />
                        </label>
                        <label className="block">
                          <span className="text-xs text-muted-foreground">End</span>
                          <input
                            type="time"
                            className={`${INPUT} mt-1`}
                            value={whEnd}
                            onChange={(e) => setWhEnd(e.target.value)}
                          />
                        </label>
                        <label className="block">
                          <span className="text-xs text-muted-foreground">Lunch (min)</span>
                          <input
                            type="number"
                            min="0"
                            max="480"
                            className={`${INPUT} mt-1`}
                            value={lunch}
                            onChange={(e) => setLunch(e.target.value)}
                          />
                        </label>
                      </div>
                      <div className="flex flex-wrap gap-1.5">
                        {DAYS.map((d) => {
                          const on = workDays.has(d.n);
                          return (
                            <button
                              key={d.n}
                              type="button"
                              onClick={() =>
                                setWorkDays((prev) => {
                                  const next = new Set(prev);
                                  if (next.has(d.n)) next.delete(d.n);
                                  else next.add(d.n);
                                  return next;
                                })
                              }
                              className={`rounded-full border px-3 py-1 text-xs font-semibold transition ${
                                on
                                  ? "border-primary bg-primary text-primary-foreground"
                                  : "border-border/60 bg-card text-muted-foreground hover:text-foreground"
                              }`}
                            >
                              {d.label}
                            </button>
                          );
                        })}
                      </div>
                      <p className="text-xs text-muted-foreground">
                        Overrides the organisation's default schedule for this site. Leave the
                        times blank to fall back to the org-wide schedule.
                      </p>
                    </div>

                    {locError ? <p className="text-xs font-medium text-destructive">{locError}</p> : null}
                    <div className="flex flex-wrap items-center gap-2">
                      <button
                        type="button"
                        onClick={useMyLocation}
                        disabled={locating}
                        className="inline-flex items-center gap-2 rounded-2xl border border-border/60 bg-card px-4 py-2 text-xs font-semibold text-foreground transition hover:bg-muted disabled:opacity-50"
                      >
                        {locating ? (
                          <LoaderCircle className="h-4 w-4 animate-spin" />
                        ) : (
                          <Crosshair className="h-4 w-4" />
                        )}
                        Use my location
                      </button>
                      <button
                        type="button"
                        onClick={() => saveLocation(project)}
                        disabled={savingLoc}
                        className="inline-flex items-center gap-2 rounded-2xl bg-primary px-4 py-2 text-xs font-semibold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
                      >
                        {savingLoc ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                        Save
                      </button>
                      <span className="text-xs text-muted-foreground">Clear both coordinates to remove the geofence.</span>
                    </div>
                  </div>
                ) : null}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
