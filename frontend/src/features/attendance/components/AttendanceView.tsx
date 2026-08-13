import { useEffect, useMemo, useState } from "react";
import { Camera, LoaderCircle, LogIn, LogOut, MapPin, TriangleAlert } from "lucide-react";
import {
  clockIn,
  clockOut,
  getAttendanceHistory,
  getTodayAttendance,
  OFF_SITE_CODE,
  uploadAttendancePhoto,
  type AttendanceRecord,
} from "../api";
import { AttendanceStatusBadge } from "./AttendanceStatusBadge";
import { getOrganization, getProjects, type Project } from "@/features/settings/api";
import { ApiError } from "@/shared/lib/api-client";
import { formatDistance, requestGeolocation, type Coords } from "@/shared/lib/geolocation";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

const TZ = "Asia/Kuala_Lumpur";
const NO_SELECTION = "__none__";
const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm";

function fmtClock(d: Date) {
  return new Intl.DateTimeFormat("en-GB", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false,
    timeZone: TZ,
  }).format(d);
}

function fmtTime(iso: string | null) {
  if (!iso) return "—";
  return new Intl.DateTimeFormat("en-GB", {
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
    timeZone: TZ,
  }).format(new Date(iso));
}

function fmtDayLabel(d: Date) {
  return new Intl.DateTimeFormat("en-MY", {
    weekday: "long",
    day: "2-digit",
    month: "long",
    year: "numeric",
    timeZone: TZ,
  }).format(d);
}

function fmtHistoryDate(ymd: string) {
  const [y, m, day] = ymd.split("-").map(Number);
  return new Intl.DateTimeFormat("en-MY", {
    weekday: "short",
    day: "2-digit",
    month: "short",
  }).format(new Date(y, (m ?? 1) - 1, day ?? 1));
}

function fmtDuration(min: number | null) {
  if (min == null) return "—";
  const h = Math.floor(min / 60);
  const m = min % 60;
  return h > 0 ? `${h}h ${m}m` : `${m}m`;
}

function GeoChip({ distance, radius }: { distance: number | null; radius: number }) {
  if (distance == null) return null;
  const onSite = distance <= radius;
  return (
    <span
      className={`inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-[11px] font-bold uppercase tracking-[0.12em] ${
        onSite ? "bg-secondary text-secondary-foreground" : "bg-amber-100 text-amber-800"
      }`}
    >
      <MapPin className="h-3 w-3" />
      {onSite ? "On-site" : "Off-site"} · {formatDistance(distance)}
    </span>
  );
}

export function AttendanceView() {
  const [today, setToday] = useState<AttendanceRecord | null>(null);
  const [history, setHistory] = useState<AttendanceRecord[]>([]);
  const [projects, setProjects] = useState<Project[]>([]);
  const [radius, setRadius] = useState(200);
  const [projectId, setProjectId] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [geoNote, setGeoNote] = useState<string | null>(null);
  const [now, setNow] = useState(() => new Date());

  // Off-site flow: set when the server refuses a clock for being outside the
  // geofence — the panel then collects a remark + photo and retries.
  const [offSite, setOffSite] = useState<{
    kind: "in" | "out";
    coords: Coords | null;
    projectId?: string;
    distanceMeters: number | null;
  } | null>(null);
  const [offRemark, setOffRemark] = useState("");
  const [offPhoto, setOffPhoto] = useState<File | null>(null);
  const [offBusy, setOffBusy] = useState(false);
  const [offError, setOffError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([getTodayAttendance(), getAttendanceHistory(), getProjects(), getOrganization()])
      .then(([t, h, p, org]) => {
        setToday(t);
        setHistory(h);
        setProjects(p.filter((x) => !x.isArchived));
        setRadius(org.geofenceRadiusMeters);
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  // Live clock — tick the displayed time + elapsed counter once a second.
  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 1000);
    return () => window.clearInterval(id);
  }, []);

  const clockedIn = today?.timeIn != null && today?.timeOut == null;
  const clockedOut = today?.timeOut != null;

  const elapsed = useMemo(() => {
    if (!clockedIn || !today?.timeIn) return null;
    const mins = Math.max(0, Math.floor((now.getTime() - new Date(today.timeIn).getTime()) / 60000));
    return fmtDuration(mins);
  }, [clockedIn, today, now]);

  const projectName = (id: string) => projects.find((p) => p.id === id)?.name ?? "Project";
  const selectedProject = projects.find((p) => p.id === projectId) ?? null;

  async function act(kind: "in" | "out") {
    setBusy(true);
    setError(null);
    setGeoNote(null);

    // Grab GPS (best-effort — denial still sends the request; the server decides
    // whether the chosen project's geofence makes location mandatory).
    let coords: Coords | null = null;
    try {
      coords = await requestGeolocation();
    } catch (e) {
      setGeoNote(e instanceof Error ? e.message : "Location not captured.");
    }

    const pid = kind === "in" ? projectId || undefined : undefined;
    try {
      const rec =
        kind === "in"
          ? await clockIn({ projectId: pid, lat: coords?.lat, lng: coords?.lng })
          : await clockOut({ lat: coords?.lat, lng: coords?.lng });
      setToday(rec);
      setHistory(await getAttendanceHistory());
    } catch (e) {
      if (e instanceof ApiError && e.code === OFF_SITE_CODE) {
        // Off-site: open the remark + photo panel and remember this attempt.
        const distanceMeters =
          e.body && typeof e.body === "object" && "distanceMeters" in e.body
            ? ((e.body as { distanceMeters?: number }).distanceMeters ?? null)
            : null;
        setOffRemark("");
        setOffPhoto(null);
        setOffError(null);
        setOffSite({ kind, coords, projectId: pid, distanceMeters });
      } else {
        setError(e instanceof Error ? e.message : "Something went wrong.");
      }
    } finally {
      setBusy(false);
    }
  }

  async function submitOffSite() {
    if (!offSite || !offPhoto || !offRemark.trim()) return;
    setOffBusy(true);
    setOffError(null);
    try {
      const { photoUrl } = await uploadAttendancePhoto(offPhoto);
      const { kind, coords, projectId: pid } = offSite;
      const rec =
        kind === "in"
          ? await clockIn({
              projectId: pid,
              lat: coords?.lat,
              lng: coords?.lng,
              remark: offRemark.trim(),
              photoUrl,
            })
          : await clockOut({
              lat: coords?.lat,
              lng: coords?.lng,
              remark: offRemark.trim(),
              photoUrl,
            });
      setToday(rec);
      setHistory(await getAttendanceHistory());
      setOffSite(null);
    } catch (e) {
      setOffError(e instanceof Error ? e.message : "Couldn't submit. Try again.");
    } finally {
      setOffBusy(false);
    }
  }

  return (
    <div className="space-y-5 sm:space-y-6">
      <section className={`${CARD} p-6 sm:p-8`}>
        <div className="flex flex-col items-center text-center">
          <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted-foreground">
            {fmtDayLabel(now)}
          </p>
          <p className="mt-2 text-5xl font-black tabular-nums text-foreground sm:text-6xl">
            {fmtClock(now)}
          </p>
          <p className="mt-1 text-xs text-muted-foreground">Kuala Lumpur time</p>

          <div className="mt-4">
            <AttendanceStatusBadge status={today?.status ?? "MISSING"} />
          </div>

          {!clockedIn && !clockedOut ? (
            <div className="mt-5 w-full max-w-sm space-y-1.5 text-left">
              <span className="text-sm font-semibold text-foreground">Project</span>
              <Select
                value={projectId || NO_SELECTION}
                onValueChange={(v) => setProjectId(v === NO_SELECTION ? "" : v)}
              >
                <SelectTrigger>
                  <SelectValue placeholder="No project" />
                </SelectTrigger>
                <SelectContent searchPlaceholder="Search projects…">
                  <SelectItem value={NO_SELECTION}>No project</SelectItem>
                  {projects.map((p) => (
                    <SelectItem key={p.id} value={p.id}>
                      {p.name}
                      {p.latitude != null && p.longitude != null ? " · geofenced" : ""}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {selectedProject?.latitude != null ? (
                <p className="text-xs text-muted-foreground">
                  Your location will be recorded against this project's geofence.
                </p>
              ) : null}
            </div>
          ) : null}

          {clockedIn ? (
            <div className="mt-4 flex flex-col items-center gap-2">
              <p className="text-sm text-muted-foreground">
                Clocked in at{" "}
                <span className="font-semibold text-foreground">{fmtTime(today?.timeIn ?? null)}</span>
                {elapsed ? (
                  <>
                    {" · "}
                    <span className="font-semibold text-foreground">{elapsed}</span> elapsed
                  </>
                ) : null}
                {today?.projectId ? ` · ${projectName(today.projectId)}` : ""}
              </p>
              <GeoChip distance={today?.clockInDistanceMeters ?? null} radius={radius} />
            </div>
          ) : null}

          {clockedOut ? (
            <>
              <div className="mt-4 grid w-full max-w-sm grid-cols-3 gap-3">
                <Stat label="In" value={fmtTime(today?.timeIn ?? null)} />
                <Stat label="Out" value={fmtTime(today?.timeOut ?? null)} />
                <Stat label="Total" value={fmtDuration(today?.durationMin ?? null)} />
              </div>
              <div className="mt-3 flex flex-wrap items-center justify-center gap-2">
                <GeoChip distance={today?.clockInDistanceMeters ?? null} radius={radius} />
                <GeoChip distance={today?.clockOutDistanceMeters ?? null} radius={radius} />
              </div>
            </>
          ) : null}

          {offSite ? (
            <div className="mt-5 w-full max-w-sm space-y-3 rounded-2xl border border-amber-300/70 bg-amber-50 p-4 text-left">
              <div className="flex items-start gap-2">
                <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0 text-amber-700" />
                <p className="text-sm text-amber-800">
                  You're off-site
                  {offSite.distanceMeters != null
                    ? ` (${formatDistance(offSite.distanceMeters)} away)`
                    : ""}
                  . Add a remark and a photo to clock {offSite.kind === "in" ? "in" : "out"}.
                </p>
              </div>
              <textarea
                value={offRemark}
                onChange={(e) => setOffRemark(e.target.value)}
                placeholder="Reason — site visit, WFH, client meeting…"
                className="min-h-[72px] w-full rounded-2xl border border-amber-300/70 bg-white/80 px-3 py-2 text-sm text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-amber-400"
              />
              <label
                htmlFor="offsite-photo"
                className="flex cursor-pointer items-center justify-center gap-2 rounded-2xl border border-amber-300/70 bg-white/80 px-3 py-3 text-sm font-semibold text-amber-800 transition hover:bg-white"
              >
                <Camera className="h-4 w-4" />
                <span className="max-w-full truncate">
                  {offPhoto ? offPhoto.name : "Take or upload a photo"}
                </span>
              </label>
              <input
                id="offsite-photo"
                type="file"
                accept="image/*"
                capture="environment"
                className="sr-only"
                onChange={(e) => setOffPhoto(e.target.files?.[0] ?? null)}
              />
              {offError ? <p className="text-xs font-medium text-destructive">{offError}</p> : null}
              <div className="flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setOffSite(null)}
                  className="rounded-2xl bg-white/70 px-4 py-2 text-xs font-semibold text-amber-800 transition hover:bg-white"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  disabled={offBusy || !offRemark.trim() || !offPhoto}
                  onClick={submitOffSite}
                  className="inline-flex items-center gap-2 rounded-2xl bg-amber-600 px-4 py-2 text-xs font-semibold text-white transition hover:bg-amber-700 disabled:opacity-50"
                >
                  {offBusy ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                  Clock {offSite.kind === "in" ? "in" : "out"} off-site
                </button>
              </div>
            </div>
          ) : null}

          <div className="mt-6">
            {clockedOut ? (
              <p className="text-sm font-medium text-muted-foreground">You're all done for today.</p>
            ) : !offSite ? (
              <button
                type="button"
                disabled={busy || loading}
                onClick={() => act(clockedIn ? "out" : "in")}
                className={`inline-flex items-center gap-2 rounded-2xl px-7 py-3.5 text-sm font-semibold shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition disabled:opacity-50 ${
                  clockedIn
                    ? "bg-foreground text-background hover:opacity-90"
                    : "bg-primary text-primary-foreground hover:opacity-90"
                }`}
              >
                {busy ? (
                  <LoaderCircle className="h-4 w-4 animate-spin" />
                ) : clockedIn ? (
                  <LogOut className="h-4 w-4" />
                ) : (
                  <LogIn className="h-4 w-4" />
                )}
                {clockedIn ? "Clock out" : "Clock in"}
              </button>
            ) : null}
          </div>

          {geoNote ? <p className="mt-3 text-xs text-muted-foreground">{geoNote}</p> : null}
          {error ? <p className="mt-4 text-sm font-medium text-destructive">{error}</p> : null}
        </div>
      </section>

      <section className={CARD}>
        <div className="border-b border-border/60 px-5 py-4 sm:px-6">
          <h2 className="text-lg font-black text-foreground">Recent attendance</h2>
        </div>
        {loading ? (
          <p className="px-5 py-6 text-sm text-muted-foreground sm:px-6">Loading…</p>
        ) : history.length === 0 ? (
          <p className="px-5 py-6 text-sm text-muted-foreground sm:px-6">No attendance yet.</p>
        ) : (
          <ul className="divide-y divide-border/60">
            {history.map((r) => (
              <li key={r.id} className="flex items-center justify-between gap-4 px-5 py-4 sm:px-6">
                <div className="min-w-0">
                  <p className="font-semibold text-foreground">{fmtHistoryDate(r.date)}</p>
                  <p className="text-xs text-muted-foreground">
                    {fmtTime(r.timeIn)} – {fmtTime(r.timeOut)} · {fmtDuration(r.durationMin)}
                    {r.clockInDistanceMeters != null
                      ? ` · ${r.clockInDistanceMeters <= radius ? "on-site" : "off-site"} ${formatDistance(r.clockInDistanceMeters)}`
                      : ""}
                  </p>
                </div>
                <AttendanceStatusBadge status={r.status} />
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-border/60 bg-card px-3 py-2.5 text-center">
      <p className="text-[11px] uppercase tracking-[0.14em] text-muted-foreground">{label}</p>
      <p className="mt-1 text-sm font-bold tabular-nums text-foreground">{value}</p>
    </div>
  );
}
