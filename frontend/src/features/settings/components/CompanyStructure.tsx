import { useEffect, useMemo, useState } from "react";
import { FolderKanban, LoaderCircle, Plus, Trash2, UserPlus, Users } from "lucide-react";
import {
  addTeamMember,
  deleteTeam,
  getTeams,
  layerLabel,
  removeTeamMember,
  type Team,
} from "@/features/teams/api";
import { getProjects, type Project } from "@/features/settings/api";
import { getEmployees, type Employee } from "@/features/employees/api";
import { TeamEditor } from "./TeamEditor";
import { SearchInput } from "@/shared/components/SearchInput";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

const CARD = "rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";

// Seven rows visible before a list scrolls, and the same seven is when its
// search box appears. Tying the two together means a list that fits never
// offers a search you don't need, and one that doesn't always gives you a way
// to jump — rather than a threshold picked independently of what's on screen.
const VISIBLE_ROWS = 7;

// All three panes are this tall, always: exactly seven rows.
//
// 36.5rem = 584px, measured rather than estimated — pane padding, the header,
// and seven 66px team rows with the 6px space-y-1.5 gaps between them. Teams
// set the number because their rows are the tallest (name plus the
// layer/member line); a project row is 46px, so seven of those fit easily.
//
// Fixed, not content-sized. The height is the layout, so it doesn't move when
// a team is added, when a project with no teams is picked, or when the editor's
// form grows a layer. Each pane scrolls its own overflow instead.
const PANE_H = "lg:h-[36.5rem]";

// Same seven-row rule for a layer section in the members card, and the same
// arithmetic: a member row is 36px (email, layer select, Remove) with the 6px
// space-y-1.5 gaps, so 7×36 + 6×6 = 288px.
//
// Measured because the consequence of leaving it out is severe: fifty people
// in one layer rendered a 2,144px section, and three such layers a 6,432px
// card — about seven screens, with no search in it.
const MEMBER_ROWS = 7;
const MEMBER_LIST_MAX = "max-h-[18rem]";   // 7×36 + 6×6 = 288px

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

// The Company/Employee → Company Structure tab. A 3-pane view (Projects → Teams → editor)
// mirroring the production admin. Members' approval chains derive from the team config
// plus their direct supervisor.
export function CompanyStructure() {
  const [teams, setTeams] = useState<Team[]>([]);
  const [projects, setProjects] = useState<Project[]>([]);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null);
  const [selectedTeamId, setSelectedTeamId] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  // Both lists get a search box once they're long enough to scan badly. The
  // threshold is low on purpose: it should appear as the org grows, not once
  // the list is already unmanageable.
  const [projectSearch, setProjectSearch] = useState("");
  const [teamSearch, setTeamSearch] = useState("");

  useEffect(() => {
    Promise.all([getTeams(), getProjects(), getEmployees()])
      .then(([t, p, e]) => {
        setTeams(t);
        const active = p.filter((x) => !x.isArchived);
        setProjects(active);
        // Land on a project with teams rather than whichever happens to sort
        // first. Opening onto an empty middle pane looks like the page failed
        // to load, when it just picked a project nobody has staffed.
        const withTeams = active.find((x) => t.some((team) => team.projectId === x.id));
        setSelectedProjectId((cur) => cur ?? withTeams?.id ?? active[0]?.id ?? null);
        setEmployees(e);
      })
      .catch((err: unknown) => setError(message(err, "Could not load company structure.")))
      .finally(() => setLoading(false));
  }, []);

  const teamCountByProject = useMemo(() => {
    const m = new Map<string, number>();
    for (const t of teams) m.set(t.projectId, (m.get(t.projectId) ?? 0) + 1);
    return m;
  }, [teams]);

  // Search first, then float projects that have teams to the top. Ties keep the
  // incoming order so the list doesn't reshuffle as teams are added elsewhere.
  const sortedProjects = useMemo(() => {
    const q = projectSearch.trim().toLowerCase();
    const base = q ? projects.filter((p) => p.name.toLowerCase().includes(q)) : projects;
    return base
      .map((project, index) => ({
        project,
        index,
        hasTeams: (teamCountByProject.get(project.id) ?? 0) > 0,
      }))
      .sort((a, b) => (a.hasTeams === b.hasTeams ? a.index - b.index : a.hasTeams ? -1 : 1))
      .map((x) => x.project);
  }, [projects, projectSearch, teamCountByProject]);

  const teamsInProject = useMemo(
    () => teams.filter((t) => t.projectId === selectedProjectId),
    [teams, selectedProjectId],
  );
  const filteredTeams = useMemo(() => {
    const q = teamSearch.trim().toLowerCase();
    return q ? teamsInProject.filter((t) => t.name.toLowerCase().includes(q)) : teamsInProject;
  }, [teamsInProject, teamSearch]);

  const selectedTeam = useMemo(
    () => teams.find((t) => t.id === selectedTeamId) ?? null,
    [teams, selectedTeamId],
  );

  const projectName = (id: string) => projects.find((p) => p.id === id)?.name ?? "Project";

  const upsert = (t: Team) => {
    setTeams((cur) =>
      cur.some((x) => x.id === t.id) ? cur.map((x) => (x.id === t.id ? t : x)) : [...cur, t],
    );
    setSelectedProjectId(t.projectId);
    setSelectedTeamId(t.id);
  };

  async function onDelete(team: Team) {
    if (!window.confirm(`Delete team "${team.name}"? Its members are unassigned.`)) return;
    try {
      await deleteTeam(team.id);
      setTeams((cur) => cur.filter((t) => t.id !== team.id));
      setSelectedTeamId(null);
    } catch (err) {
      setError(message(err, "Could not delete the team."));
    }
  }

  return (
    <div className="space-y-4">
      {/* Title only. The two lines of prose that used to sit here explained the
          approval model to someone reading it once and cost every visit after
          that the vertical space — but with nothing at all the panes floated
          under the portal header with no anchor. */}
      <h2 className="text-2xl font-black text-foreground">Company Structure</h2>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {loading ? (
        <p className="text-sm text-muted-foreground">Loading company structure…</p>
      ) : (
        <div className="grid gap-4 lg:grid-cols-[260px_minmax(220px,1fr)_minmax(0,2fr)]">
          {/* Pane 1 — Projects */}
          <section className={`${CARD} ${PANE_H} flex flex-col p-4`}>
            <div className="mb-1 flex items-center gap-2">
              <FolderKanban className="h-4 w-4 text-primary" />
              <h3 className="text-base font-black text-foreground">Projects</h3>
            </div>
            <p className="mb-3 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
              {projects.length} total
            </p>
            {projects.length > VISIBLE_ROWS ? (
              <SearchInput
                value={projectSearch}
                onChange={setProjectSearch}
                placeholder="Search projects…"
                className="mb-3"
                inputClassName="h-9 rounded-xl"
                clearLabel="Clear project search"
              />
            ) : null}
            {/* Capped height so a long list scrolls inside the pane instead of
                stretching the page past the other two panes. */}
            <div className="nice-scrollbar min-h-0 flex-1 space-y-1.5 overflow-y-auto">
              {projects.length === 0 ? (
                <p className="text-sm text-muted-foreground">No projects yet.</p>
              ) : sortedProjects.length === 0 ? (
                <p className="px-1 py-2 text-sm text-muted-foreground">
                  No project matches that search.
                </p>
              ) : (
                sortedProjects.map((p) => {
                  const count = teamCountByProject.get(p.id) ?? 0;
                  const active = p.id === selectedProjectId;
                  return (
                    <button
                      key={p.id}
                      type="button"
                      onClick={() => {
                        setSelectedProjectId(p.id);
                        setSelectedTeamId(null);
                      }}
                      className={`flex w-full items-center justify-between gap-2 rounded-2xl border px-3.5 py-3 text-left text-sm font-semibold transition ${
                        active
                          ? "border-primary/40 bg-primary/5 text-primary"
                          : "border-border/60 text-foreground hover:bg-muted"
                      }`}
                    >
                      <span className="truncate">{p.name}</span>
                      <span
                        className={`flex h-6 min-w-6 items-center justify-center rounded-full px-1.5 text-xs font-bold ${
                          count > 0 ? "bg-primary/10 text-primary" : "bg-muted text-muted-foreground"
                        }`}
                      >
                        {count}
                      </span>
                    </button>
                  );
                })
              )}
            </div>
          </section>

          {/* Pane 2 — Teams in the selected project */}
          <section className={`${CARD} ${PANE_H} flex flex-col p-4`}>
            <div className="mb-3 flex items-start justify-between gap-2">
              <div className="flex items-center gap-2">
                <Users className="h-4 w-4 text-primary" />
                <div>
                  <h3 className="text-base font-black text-foreground">Teams</h3>
                  <p className="text-xs text-muted-foreground">
                    {selectedProjectId ? projectName(selectedProjectId) : "—"}
                  </p>
                </div>
              </div>
              <button
                type="button"
                onClick={() => {
                  setSelectedTeamId(null);
                  setCreating(true);
                }}
                disabled={!selectedProjectId}
                className="inline-flex shrink-0 items-center gap-1 rounded-full bg-primary px-3 py-1.5 text-xs font-semibold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
              >
                <Plus className="h-3.5 w-3.5" /> New
              </button>
            </div>
            {teamsInProject.length > VISIBLE_ROWS ? (
              <SearchInput
                value={teamSearch}
                onChange={setTeamSearch}
                placeholder="Search teams…"
                className="mb-3"
                inputClassName="h-9 rounded-xl"
                clearLabel="Clear team search"
              />
            ) : null}
            {teamsInProject.length === 0 ? (
              <p className="text-sm text-muted-foreground">No teams yet. Click “New” to create one.</p>
            ) : filteredTeams.length === 0 ? (
              <p className="px-1 py-2 text-sm text-muted-foreground">No team matches that search.</p>
            ) : (
              <div className="nice-scrollbar min-h-0 flex-1 space-y-1.5 overflow-y-auto">
                {filteredTeams.map((t) => {
                  const active = t.id === selectedTeamId;
                  return (
                    <button
                      key={t.id}
                      type="button"
                      onClick={() => setSelectedTeamId(t.id)}
                      className={`w-full rounded-2xl border px-3.5 py-3 text-left transition ${
                        active ? "border-primary/40 bg-primary/5" : "border-border/60 hover:bg-muted"
                      }`}
                    >
                      <p className="text-sm font-bold text-foreground">{t.name}</p>
                      <p className="mt-1 flex items-center gap-3 text-xs text-muted-foreground">
                        <span>
                          {t.layerCount} layer{t.layerCount === 1 ? "" : "s"}
                        </span>
                        <span>
                          {t.members.length} member{t.members.length === 1 ? "" : "s"}
                        </span>
                      </p>
                    </button>
                  );
                })}
              </div>
            )}
          </section>

          {/* Pane 3 — the editor itself, as in the previous system. Editing
              happens beside the list you picked from, not on top of it. */}
          <section className={`${CARD} ${PANE_H} nice-scrollbar space-y-5 overflow-y-auto p-5 sm:p-6`}>
            {creating ? (
              <TeamEditor
                team={null}
                projects={projects}
                onCancel={() => setCreating(false)}
                onSaved={(t) => {
                  upsert(t);
                  // Flip straight into editing what was just created, rather
                  // than clearing the pane and making the admin find it.
                  setCreating(false);
                }}
              />
            ) : selectedTeam ? (
              <TeamEditor
                team={selectedTeam}
                projects={projects}
                onCancel={() => setSelectedTeamId(null)}
                onSaved={upsert}
              />
            ) : (
              <div className="flex min-h-[220px] flex-col items-center justify-center text-center">
                <h3 className="text-base font-black text-foreground">Pick a team</h3>
                <p className="mt-1 max-w-xs text-sm text-muted-foreground">
                  Select a team on the left to edit, or click “New” to create one.
                </p>
              </div>
            )}
          </section>
        </div>
      )}

      {/* Members, full width below the panes rather than inside the narrow
          third one — as the previous system does it. A roster with a layer
          dropdown and a remove action per row doesn't fit a third of a row. */}
      {!loading && selectedTeam && !creating ? (
        <section className={`${CARD} p-5 sm:p-6`}>
          <TeamDetail
            team={selectedTeam}
            employees={employees}
            projectName={projectName(selectedTeam.projectId)}
            onDelete={() => onDelete(selectedTeam)}
            onUpdated={upsert}
            onError={setError}
          />
        </section>
      ) : null}

    </div>
  );
}

// The right-hand pane: a team's roster (grouped by layer) + member management + edit/delete.
function TeamDetail({
  team,
  employees,
  projectName,
  onDelete,
  onUpdated,
  onError,
}: {
  team: Team;
  employees: Employee[];
  projectName: string;
  onDelete: () => void;
  onUpdated: (t: Team) => void;
  onError: (msg: string) => void;
}) {
  const [memberSearch, setMemberSearch] = useState("");
  const memberQuery = memberSearch.trim().toLowerCase();
  const [addEmp, setAddEmp] = useState("");
  const [addLayer, setAddLayer] = useState("0");
  const [working, setWorking] = useState(false);

  const memberIds = useMemo(() => new Set(team.members.map((m) => m.employeeId)), [team.members]);
  const available = employees.filter((e) => !memberIds.has(e.id));
  const layers = Array.from({ length: team.layerCount }, (_, i) => i);

  async function run(action: () => Promise<Team>) {
    setWorking(true);
    try {
      onUpdated(await action());
    } catch (err) {
      onError(err instanceof Error ? err.message : "Could not update the team.");
    } finally {
      setWorking(false);
    }
  }

  async function add() {
    if (!addEmp) return;
    await run(() => addTeamMember(team.id, { employeeId: addEmp, layer: Number(addLayer) }));
    setAddEmp("");
  }

  return (
    <div>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-base font-black text-foreground">Members</p>
          <p className="text-xs text-muted-foreground">
            {projectName} · {team.members.length} assigned
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={onDelete}
            className="inline-flex items-center gap-1 rounded-full border border-destructive/30 bg-card px-3 py-1.5 text-xs font-semibold text-destructive transition-colors hover:bg-destructive/5"
          >
            <Trash2 className="h-3 w-3" />
            Delete
          </button>
        </div>
      </div>

      {/* Search across every layer at once. A crew of forty is three sections
          you'd otherwise scroll separately to find one person, and the grouping
          is the point of this view so it shouldn't be flattened to get a
          filter. Offered only once the roster is bigger than a section shows. */}
      {team.members.length > MEMBER_ROWS ? (
        <div className="mt-4">
          <SearchInput
            value={memberSearch}
            onChange={setMemberSearch}
            placeholder="Search members…"
            inputClassName="h-10 rounded-xl"
            clearLabel="Clear member search"
          />
        </div>
      ) : null}

      {/* Roster grouped by layer, top layer first */}
      <div className="mt-4 space-y-3">
        {[...layers].reverse().map((layer) => {
          const all = team.members.filter((m) => m.layer === layer);
          const members = memberQuery
            ? all.filter((m) => (m.email ?? m.employeeId).toLowerCase().includes(memberQuery))
            : all;
          return (
            <div key={layer} className="rounded-2xl border border-border/60 bg-background/50 p-3">
              <p className="flex items-baseline justify-between gap-3 text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">
                <span>{layerLabel(team, layer)}</span>
                {/* The count matters once the list scrolls: seven visible rows
                    out of thirty looks like the whole layer otherwise. */}
                {all.length > 0 ? (
                  <span className="font-semibold normal-case tracking-normal">
                    {memberQuery && members.length !== all.length
                      ? `${members.length} of ${all.length}`
                      : `${all.length}`}
                  </span>
                ) : null}
              </p>
              {all.length === 0 ? (
                <p className="mt-1 text-xs text-muted-foreground">No one at this layer.</p>
              ) : members.length === 0 ? (
                <p className="mt-1 text-xs text-muted-foreground">No one here matches that search.</p>
              ) : (
                // Seven rows, then scroll — the same rule as the panes above.
                // Unbounded, a fifty-person layer was 2,144px and three of them
                // ran to 6,432px: seven screens of card with no way to find
                // anyone in it.
                <ul className={`nice-scrollbar mt-2 ${MEMBER_LIST_MAX} space-y-1.5 overflow-y-auto`}>
                  {members.map((m) => (
                    <li
                      key={m.employeeId}
                      className="flex flex-wrap items-center justify-between gap-2"
                    >
                      <span className="text-sm font-medium text-foreground">
                        {m.email ?? m.employeeId}
                      </span>
                      <div className="flex items-center gap-2">
                        <Select
                          value={String(m.layer)}
                          onValueChange={(v) =>
                            run(() =>
                              addTeamMember(team.id, { employeeId: m.employeeId, layer: Number(v) }),
                            )
                          }
                        >
                          <SelectTrigger className="h-9 w-[150px] bg-card">
                            <SelectValue />
                          </SelectTrigger>
                          <SelectContent>
                            {layers.map((l) => (
                              <SelectItem key={l} value={String(l)}>
                                {layerLabel(team, l)}
                              </SelectItem>
                            ))}
                          </SelectContent>
                        </Select>
                        <button
                          type="button"
                          disabled={working}
                          onClick={() => run(() => removeTeamMember(team.id, m.employeeId))}
                          className="rounded-full border border-border/60 bg-card px-2.5 py-1 text-xs font-semibold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
                        >
                          Remove
                        </button>
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          );
        })}
      </div>

      {/* Add member */}
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <div className="min-w-[180px] flex-1">
          <Select value={addEmp} onValueChange={setAddEmp}>
            <SelectTrigger className="h-10 bg-card">
              <SelectValue placeholder="Add employee…" />
            </SelectTrigger>
            <SelectContent searchPlaceholder="Search people…">
              {available.length === 0 ? (
                <SelectItem value="__none__" disabled>
                  Everyone's already on this team
                </SelectItem>
              ) : (
                available.map((e) => (
                  <SelectItem key={e.id} value={e.id}>
                    {e.email}
                  </SelectItem>
                ))
              )}
            </SelectContent>
          </Select>
        </div>
        <Select value={addLayer} onValueChange={setAddLayer}>
          <SelectTrigger className="h-10 w-[150px] bg-card">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {layers.map((l) => (
              <SelectItem key={l} value={String(l)}>
                {layerLabel(team, l)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <button
          type="button"
          disabled={working || !addEmp}
          onClick={add}
          className="inline-flex items-center gap-2 rounded-2xl bg-primary px-4 py-2.5 text-sm font-semibold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
        >
          {working ? (
            <LoaderCircle className="h-4 w-4 animate-spin" />
          ) : (
            <UserPlus className="h-4 w-4" />
          )}
          Add
        </button>
      </div>
    </div>
  );
}
