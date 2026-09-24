import { useEffect, useState } from "react";
import { LoaderCircle, MapPin, Plus, RefreshCw, ShieldCheck } from "lucide-react";
import {
  archiveProject,
  createProject,
  getProject,
  getProjects,
  getXeroProjectTracking,
  getXeroStatus,
  restoreProject,
  setXeroProjectTrackingCategory,
  syncXeroProjects,
  updateProject,
  type Project,
  type XeroTrackingCategory,
} from "../api";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonPanel } from "@/shared/components/Skeleton";
import { OrgGeofenceCard } from "./OrgFieldCards";
import {
  AllowedIpsEditor,
  GeofenceSitesEditor,
  type IpDraft,
  type SiteDraft,
} from "./ProjectAccessEditors";
import { isValidIpOrCidr } from "../lib/ip-allowlist";
import { SearchInput } from "@/shared/components/SearchInput";
import { TablePager } from "@/shared/components/TablePager";
import { usePaged } from "@/shared/lib/use-paged";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
// Below this the filter box is noise; above it, scanning the grid stops being
// quicker than typing. Matches CompanyStructure's VISIBLE_ROWS.
const SEARCHABLE_FROM = 7;
// Three across on a wide screen, so a page is four clean rows.
const PAGE_SIZE = 12;
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

// A project saved before sites existed carries a single lat/lng pair and a
// comma-separated allowlist. Both are shown as one-entry lists so the editor
// never looks empty for a project that IS configured, and so the first save
// moves it onto the new shape.
function legacySites(project: Project): SiteDraft[] {
  if (project.latitude == null || project.longitude == null) return [];
  return [{
    label: project.name,
    latitude: String(project.latitude),
    longitude: String(project.longitude),
  }];
}

function legacyIps(project: Project): IpDraft[] {
  return (project.allowedIps ?? "")
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean)
    .map((cidr) => ({ label: "", cidr }));
}

export function ProjectsSettings() {
  const [projects, setProjects] = useState<Project[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState("");
  const [adding, setAdding] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [search, setSearch] = useState("");

  // Per-row editor: geofence location + IP allowlist.
  const [editingId, setEditingId] = useState<string | null>(null);
  const [sites, setSites] = useState<SiteDraft[]>([]);
  const [ipEntries, setIpEntries] = useState<IpDraft[]>([]);
  // The grid's projects come from the list endpoint, which omits both lists —
  // so opening the editor fetches the project on its own.
  const [loadingEditor, setLoadingEditor] = useState(false);
  const [whStart, setWhStart] = useState("");
  const [whEnd, setWhEnd] = useState("");
  const [workDays, setWorkDays] = useState<Set<number>>(new Set());
  const [lunch, setLunch] = useState("60");
  const [savingLoc, setSavingLoc] = useState(false);
  const [locError, setLocError] = useState<string | null>(null);

  // Xero sync — the button only shows when a connection exists, so we never
  // offer a sync that can only fail.
  const [xeroConnected, setXeroConnected] = useState(false);
  const [syncing, setSyncing] = useState(false);
  const [syncMsg, setSyncMsg] = useState<string | null>(null);

  // Which Xero tracking category holds the projects. Only worth showing when
  // Xero offers a choice — with one category the sync adopts it silently.
  const [categories, setCategories] = useState<XeroTrackingCategory[]>([]);
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [savingCategory, setSavingCategory] = useState(false);
  // A switch waiting for the admin to confirm what it does.
  const [pendingCategory, setPendingCategory] = useState<XeroTrackingCategory | null>(null);
  // Bumped when a switch is cancelled so the picker remounts: Radix treats
  // re-choosing the option you just backed out of as "no change" and would
  // never ask again.
  const [pickerKey, setPickerKey] = useState(0);

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
      .then((s) => {
        setXeroConnected(s.connected);
        if (!s.connected) return;
        // A failure here only costs the category picker, never the list.
        return getXeroProjectTracking()
          .then((t) => {
            setCategories(t.categories);
            setCategoryId(t.selectedCategoryId);
          })
          .catch(() => undefined);
      })
      .catch(() => setXeroConnected(false));
  }, []);

  // The first choice just takes effect — there is nothing yet to lose. A
  // SWITCH changes which projects every picker offers, so it asks first.
  function handleCategory(id: string) {
    if (id === categoryId) return;
    const chosen = categories.find((c) => c.id === id);
    if (!chosen) return;
    if (categoryId === null) void applyCategory(chosen);
    else setPendingCategory(chosen);
  }

  function cancelSwitch() {
    setPendingCategory(null);
    setPickerKey((k) => k + 1);
  }

  // Saving the category also syncs it on the server, so the list flips to the
  // new category's options in one step.
  async function applyCategory(chosen: XeroTrackingCategory) {
    setSavingCategory(true);
    setError(null);
    setSyncMsg(null);
    try {
      const r = await setXeroProjectTrackingCategory(chosen.id);
      setCategoryId(chosen.id);
      setPendingCategory(null);
      setSyncMsg(
        r
          ? `Projects now come from “${chosen.name}” — ${r.imported} added, ${r.updated} updated.`
          : `Projects now come from “${chosen.name}”.`,
      );
      await query.refresh();
    } catch (err) {
      setError(message(err, "Could not switch the tracking category."));
      setPickerKey((k) => k + 1);
    } finally {
      setSavingCategory(false);
    }
  }

  const currentCategory = categories.find((c) => c.id === categoryId) ?? null;

  async function handleSync() {
    setSyncing(true);
    setSyncMsg(null);
    setError(null);
    try {
      const r = await syncXeroProjects();
      setSyncMsg(
        r.needsTrackingCategoryChoice
          ? "Xero has more than one tracking category. Choose the one that holds your projects, then sync again."
          : `Synced from Xero — ${r.imported} added, ${r.updated} updated` +
            (r.skipped ? `, ${r.skipped} unchanged` : "") +
            (r.trackingCategoryName ? ` (from “${r.trackingCategoryName}”).` : "."),
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

  async function openEditor(project: Project) {
    setEditingId(project.id);
    setLocError(null);
    setLoadingEditor(true);
    // Seed from the legacy single values first, so the editor shows something
    // real while the fetch is in flight and a project that still only has the
    // old scalar pair is migrated to a site on its next save.
    setSites(legacySites(project));
    setIpEntries(legacyIps(project));
    setWhStart(project.workingHoursStart ?? "");
    setWhEnd(project.workingHoursEnd ?? "");
    setWorkDays(parseDays(project.workingDays));
    setLunch(String(project.lunchBreakMinutes ?? 60));
    try {
      const full = await getProject(project.id);
      if (full.geofencePoints.length > 0) {
        setSites(full.geofencePoints.map((g) => ({
          label: g.label,
          latitude: String(g.latitude),
          longitude: String(g.longitude),
        })));
      }
      if (full.allowedIpEntries.length > 0) {
        setIpEntries(full.allowedIpEntries.map((e) => ({ label: e.label, cidr: e.cidr })));
      }
    } catch (err) {
      setLocError(message(err, "Could not load this project's sites."));
    } finally {
      setLoadingEditor(false);
    }
  }

  async function saveLocation(project: Project) {
    // Every site needs a name and a usable pair of coordinates. Saving a
    // half-filled row would produce a site at 0,0 — in the Gulf of Guinea —
    // that quietly fails every geofence check.
    const cleanSites = sites.map((site) => ({
      label: site.label.trim(),
      latitude: Number(site.latitude),
      longitude: Number(site.longitude),
    }));
    const badSite = cleanSites.find(
      (site, i) =>
        site.label === "" ||
        sites[i].latitude.trim() === "" ||
        sites[i].longitude.trim() === "" ||
        Number.isNaN(site.latitude) ||
        Number.isNaN(site.longitude) ||
        site.latitude < -90 || site.latitude > 90 ||
        site.longitude < -180 || site.longitude > 180,
    );
    if (badSite) {
      setLocError(
        "Every site needs a name, a latitude between −90 and 90, and a longitude between −180 and 180.",
      );
      return;
    }

    const cleanIps = ipEntries.map((e) => ({ label: e.label.trim(), cidr: e.cidr.trim() }));
    const badIp = cleanIps.find((e) => e.label === "" || !isValidIpOrCidr(e.cidr));
    if (badIp) {
      setLocError("Every allowlist entry needs a name and a valid IPv4 address or range.");
      return;
    }

    setSavingLoc(true);
    setLocError(null);
    try {
      const updated = await updateProject(project.id, {
        name: project.name,
        // The single pair and the comma-separated string are superseded by the
        // lists; clearing them keeps one source of truth rather than leaving a
        // stale centre behind the sites.
        latitude: null,
        longitude: null,
        allowedIps: null,
        geofencePoints: cleanSites,
        allowedIpEntries: cleanIps,
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

  // When Xero drives projects, they're pulled from Xero and manual creation is
  // off — matching the monolith, which hides manual projects (not deletes them)
  // once Xero takes over. So the settings list shows only Xero-sourced projects
  // then, and the manual "add" box disappears.
  //
  // Projects from a tracking category the admin has switched away from are
  // left out too — kept on the server for past records, but no longer this
  // org's project list. They come back if the category is switched back.
  const visibleProjects = (
    xeroConnected
      ? projects.filter((p) => p.xeroProjectId != null || p.xeroTrackingOptionId != null)
      : projects
  ).filter((p) => !p.hiddenByTrackingCategory);
  const hiddenBySwitch = projects.filter((p) => p.hiddenByTrackingCategory).length;

  // Filter then page. The whole list is already in hand, so both happen here
  // rather than costing a round trip per keystroke or per page.
  const needle = search.trim().toLowerCase();
  const matching = needle
    ? visibleProjects.filter((p) => p.name.toLowerCase().includes(needle))
    : visibleProjects;
  const paged = usePaged(matching, PAGE_SIZE);

  // The editor renders below the grid rather than inside a card, so it needs
  // the project itself and not just the id.
  const editingProject = visibleProjects.find((p) => p.id === editingId) ?? null;

  return (
    <div className="space-y-5">
      {/* Projects lead: the radius is a default that only means anything once
          there are projects to apply it to. */}
      <div className={`${CARD} space-y-5`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <h2 className="text-lg font-black text-foreground">Projects</h2>
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
      {xeroConnected ? (
        <p className="text-sm text-muted-foreground">
          Projects are pulled from Xero — either from its Projects product or from the options on a
          tracking category. Use <span className="font-semibold">Sync from Xero</span> to refresh the
          list; add or rename projects in Xero.
        </p>
      ) : null}

      {/* One category is not a choice worth asking about; the sync takes it. */}
      {xeroConnected && categories.length > 1 ? (
        <div className="sm:max-w-sm">
          <label className="block text-sm font-semibold text-foreground">
            Tracking category holding your projects
          </label>
          <Select
            key={pickerKey}
            value={categoryId ?? ""}
            onValueChange={handleCategory}
            disabled={savingCategory || syncing || pendingCategory !== null}
          >
            <SelectTrigger className="mt-1.5 bg-card">
              <SelectValue placeholder="Choose a category" />
            </SelectTrigger>
            <SelectContent>
              {categories.map((c) => (
                <SelectItem key={c.id} value={c.id}>
                  {c.name} — {c.optionCount} option{c.optionCount === 1 ? "" : "s"}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <p className="mt-1 text-xs text-muted-foreground">
            Each option in this category becomes a project. Xero allows two active categories —
            pick the one that represents your projects.
          </p>
        </div>
      ) : null}

      {/* What a switch does, said before it happens — the same points the
          previous system made. Inline rather than a pop-up, like the other
          confirmations in this app. */}
      {pendingCategory ? (
        <div className="rounded-2xl border border-amber-500/30 bg-amber-500/10 p-4 text-sm text-amber-900 dark:text-amber-200">
          <p className="font-semibold">
            Switch from “{currentCategory?.name ?? "the current category"}” to “{pendingCategory.name}”?
          </p>
          <ul className="mt-2 list-disc space-y-1 pl-5">
            <li>
              The {pendingCategory.optionCount} option{pendingCategory.optionCount === 1 ? "" : "s"} in
              “{pendingCategory.name}” are imported as projects straight away.
            </li>
            <li>
              Projects from “{currentCategory?.name ?? "the current category"}” stop being offered in
              pickers. They are not deleted — past claims and attendance keep showing them.
            </li>
            <li>
              Teams linked to the old projects need linking to the new ones before their members can
              pick them.
            </li>
            <li>Claims sent to Xero from now on are tagged under “{pendingCategory.name}”.</li>
            <li>You can switch back at any time.</li>
          </ul>
          <div className="mt-3 flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => void applyCategory(pendingCategory)}
              disabled={savingCategory}
              className="inline-flex h-10 items-center gap-2 rounded-xl bg-primary px-4 text-sm font-semibold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
            >
              {savingCategory ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
              Switch and sync
            </button>
            <button
              type="button"
              onClick={cancelSwitch}
              disabled={savingCategory}
              className="inline-flex h-10 items-center rounded-xl border border-border bg-card px-4 text-sm font-semibold text-foreground transition hover:bg-muted disabled:opacity-50"
            >
              Cancel
            </button>
          </div>
        </div>
      ) : null}
      {syncMsg ? <p className="text-sm font-medium text-primary">{syncMsg}</p> : null}
      {hiddenBySwitch > 0 ? (
        <p className="text-xs text-muted-foreground">
          {hiddenBySwitch === 1
            ? "1 project from a previous tracking category is hidden."
            : `${hiddenBySwitch} projects from a previous tracking category are hidden.`}{" "}
          They still show on past claims and attendance, and come back if you switch back.
        </p>
      ) : null}

      {!xeroConnected ? (
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
      ) : null}

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {!loading && visibleProjects.length > SEARCHABLE_FROM ? (
        <SearchInput value={search} onChange={setSearch} placeholder="Search projects…" />
      ) : null}

      {loading ? (
        <SkeletonPanel />
      ) : visibleProjects.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          {xeroConnected
            ? "No projects synced yet. Click “Sync from Xero” to pull them in."
            : "No projects yet."}
        </p>
      ) : (
        <>
          {/* A grid, not a list: one project per full-width row spent most of
              the line on nothing, and the coordinates that filled it are
              editable in the panel below anyway. Cards use the width instead. */}
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {paged.pageItems.map((project) => {
              // Sites first, the legacy single pair as the fallback — reading
              // latitude alone would call a project with three sites
              // "no geofence", since saving sites clears that pair.
              const siteCount =
                project.geofenceSiteCount > 0
                  ? project.geofenceSiteCount
                  : project.latitude != null && project.longitude != null
                    ? 1
                    : 0;
              const ipCount =
                project.allowedIpCount > 0
                  ? project.allowedIpCount
                  : project.allowedIps?.trim()
                    ? project.allowedIps.split(",").filter((p) => p.trim()).length
                    : 0;
              const open = editingId === project.id;
              return (
                <div
                  key={project.id}
                  className={`flex flex-col gap-3 rounded-2xl border p-4 transition ${
                    open ? "border-primary/60 bg-primary/5" : "border-border/60 bg-card"
                  }`}
                >
                  <div className="min-w-0">
                    <p
                      className={`truncate font-semibold ${
                        project.isArchived ? "text-muted-foreground line-through" : "text-foreground"
                      }`}
                    >
                      {project.name}
                    </p>
                    <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
                      {/* The pin says yes-or-no; the numbers themselves live in
                          the editor, where they can actually be changed. */}
                      <span
                        className={`inline-flex items-center gap-1 ${
                          siteCount > 0 ? "text-primary" : "text-muted-foreground"
                        }`}
                      >
                        <MapPin className="h-3 w-3" />
                        {siteCount === 0
                          ? "No geofence"
                          : siteCount === 1
                            ? "Geofenced"
                            : `${siteCount} sites`}
                      </span>
                      {ipCount > 0 ? (
                        <span className="inline-flex items-center gap-1 text-primary">
                          <ShieldCheck className="h-3 w-3" />
                          {ipCount === 1 ? "IP allowlist" : `${ipCount} IP entries`}
                        </span>
                      ) : null}
                      {project.workingHoursStart && project.workingHoursEnd ? (
                        <span className="text-muted-foreground">
                          {project.workingHoursStart}&ndash;{project.workingHoursEnd}
                        </span>
                      ) : null}
                      {project.isArchived ? (
                        <span className="text-muted-foreground">Archived</span>
                      ) : null}
                    </div>
                  </div>

                  <div className="mt-auto flex items-center gap-2">
                    <button
                      type="button"
                      onClick={() => (open ? setEditingId(null) : openEditor(project))}
                      className="rounded-full border border-border/60 bg-card px-4 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground"
                    >
                      {open ? "Close" : "Edit"}
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
              );
            })}
          </div>

          {/* Below the grid, full width. Expanding a card in place would push
              the rest of its row down and leave a hole beside it. */}
          {editingProject ? (
            <div className="mt-3">
              <p className="mb-2 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                Editing {editingProject.name}
              </p>
            <div className="mt-3 space-y-3 rounded-2xl border border-border/60 bg-background/60 p-3">
              {loadingEditor ? (
                <p className="text-xs text-muted-foreground">Loading this project's sites…</p>
              ) : null}

              <GeofenceSitesEditor sites={sites} onChange={setSites} />
              <AllowedIpsEditor entries={ipEntries} onChange={setIpEntries} />

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
                  onClick={() => saveLocation(editingProject)}
                  disabled={savingLoc}
                  className="inline-flex items-center gap-2 rounded-2xl bg-primary px-4 py-2 text-xs font-semibold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
                >
                  {savingLoc ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                  Save
                </button>
                <span className="text-xs text-muted-foreground">Clear both coordinates to remove the geofence.</span>
              </div>
            </div>
            </div>
          ) : null}

          <TablePager paged={paged} noun="project" />
        </>
      )}
      </div>

      <OrgGeofenceCard />
    </div>
  );
}
