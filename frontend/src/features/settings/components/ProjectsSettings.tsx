import { useEffect, useState } from "react";
import { Crosshair, LoaderCircle, MapPin, Plus } from "lucide-react";
import {
  archiveProject,
  createProject,
  getProjects,
  restoreProject,
  updateProject,
  type Project,
} from "../api";
import { requestGeolocation } from "@/shared/lib/geolocation";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm sm:p-6";
const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50";

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

export function ProjectsSettings() {
  const [projects, setProjects] = useState<Project[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [adding, setAdding] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);

  // Per-row geofence-location editor.
  const [editingId, setEditingId] = useState<string | null>(null);
  const [lat, setLat] = useState("");
  const [lng, setLng] = useState("");
  const [savingLoc, setSavingLoc] = useState(false);
  const [locating, setLocating] = useState(false);
  const [locError, setLocError] = useState<string | null>(null);

  useEffect(() => {
    getProjects()
      .then(setProjects)
      .catch((e: unknown) => setError(message(e, "Could not load projects.")))
      .finally(() => setLoading(false));
  }, []);

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
      });
      setProjects((current) => current.map((p) => (p.id === updated.id ? updated : p)));
      setEditingId(null);
    } catch (err) {
      setLocError(message(err, "Could not save the location."));
    } finally {
      setSavingLoc(false);
    }
  }

  return (
    <div className={`${CARD} space-y-5`}>
      <div>
        <h2 className="text-lg font-black text-foreground">Projects</h2>
        <p className="text-sm text-muted-foreground">
          Projects that claims (and later attendance/leave) are filed against. Give a project a
          location to geofence attendance clock-ins against it.
        </p>
      </div>

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
        <p className="text-sm text-muted-foreground">Loading projects…</p>
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
                    <span
                      className={`inline-flex items-center gap-1 text-xs ${
                        geofenced ? "text-primary" : "text-muted-foreground"
                      }`}
                    >
                      <MapPin className="h-3 w-3" />
                      {geofenced
                        ? `${project.latitude!.toFixed(5)}, ${project.longitude!.toFixed(5)}`
                        : "No geofence"}
                      {project.isArchived ? " · Archived" : ""}
                    </span>
                  </div>
                  <div className="flex shrink-0 items-center gap-2">
                    <button
                      type="button"
                      onClick={() => (editingId === project.id ? setEditingId(null) : openEditor(project))}
                      className="rounded-full border border-border/60 bg-card px-4 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground"
                    >
                      {editingId === project.id ? "Close" : "Location"}
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
                        Save location
                      </button>
                      <span className="text-xs text-muted-foreground">Clear both to remove the geofence.</span>
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
