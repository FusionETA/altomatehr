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
import { TeamEditorModal } from "./TeamEditorModal";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

const CARD = "rounded-[28px] border border-border/70 bg-card/90 shadow-ambient backdrop-blur-sm";

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
  const [editing, setEditing] = useState<Team | null>(null);

  useEffect(() => {
    Promise.all([getTeams(), getProjects(), getEmployees()])
      .then(([t, p, e]) => {
        setTeams(t);
        const active = p.filter((x) => !x.isArchived);
        setProjects(active);
        setSelectedProjectId((cur) => cur ?? active[0]?.id ?? null);
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

  // Projects with teams sort to the top of the list.
  const sortedProjects = useMemo(
    () =>
      [...projects].sort((a, b) => {
        const ha = (teamCountByProject.get(a.id) ?? 0) > 0;
        const hb = (teamCountByProject.get(b.id) ?? 0) > 0;
        if (ha !== hb) return ha ? -1 : 1;
        return a.name.localeCompare(b.name);
      }),
    [projects, teamCountByProject],
  );

  const teamsInProject = useMemo(
    () => teams.filter((t) => t.projectId === selectedProjectId),
    [teams, selectedProjectId],
  );
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
      <div>
        <h2 className="text-2xl font-black text-foreground">Company Structure</h2>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
          Define teams and approval layers per project. Members' approval chains are derived from
          the team config plus their direct supervisor — no per-employee overrides.
        </p>
      </div>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {loading ? (
        <p className="text-sm text-muted-foreground">Loading company structure…</p>
      ) : (
        <div className="grid gap-4 lg:grid-cols-[260px_minmax(220px,1fr)_minmax(0,2fr)]">
          {/* Pane 1 — Projects */}
          <section className={`${CARD} flex flex-col p-4`}>
            <div className="mb-1 flex items-center gap-2">
              <FolderKanban className="h-4 w-4 text-primary" />
              <h3 className="text-base font-black text-foreground">Projects</h3>
            </div>
            <p className="mb-3 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
              {projects.length} total
            </p>
            <div className="space-y-1.5">
              {sortedProjects.length === 0 ? (
                <p className="text-sm text-muted-foreground">No projects yet.</p>
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
          <section className={`${CARD} flex flex-col p-4`}>
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
                onClick={() => setCreating(true)}
                disabled={!selectedProjectId}
                className="inline-flex shrink-0 items-center gap-1 rounded-full bg-primary px-3 py-1.5 text-xs font-semibold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
              >
                <Plus className="h-3.5 w-3.5" /> New
              </button>
            </div>
            {teamsInProject.length === 0 ? (
              <p className="text-sm text-muted-foreground">No teams yet. Click “New” to create one.</p>
            ) : (
              <div className="space-y-1.5">
                {teamsInProject.map((t) => {
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

          {/* Pane 3 — Team editor / detail */}
          <section className={`${CARD} p-5 sm:p-6`}>
            {selectedTeam ? (
              <TeamDetail
                team={selectedTeam}
                employees={employees}
                projectName={projectName(selectedTeam.projectId)}
                onEdit={() => setEditing(selectedTeam)}
                onDelete={() => onDelete(selectedTeam)}
                onUpdated={upsert}
                onError={setError}
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

      {creating ? (
        <TeamEditorModal
          team={null}
          projects={projects}
          onClose={() => setCreating(false)}
          onSaved={upsert}
        />
      ) : null}
      {editing ? (
        <TeamEditorModal
          team={editing}
          projects={projects}
          onClose={() => setEditing(null)}
          onSaved={upsert}
        />
      ) : null}
    </div>
  );
}

// The right-hand pane: a team's roster (grouped by layer) + member management + edit/delete.
function TeamDetail({
  team,
  employees,
  projectName,
  onEdit,
  onDelete,
  onUpdated,
  onError,
}: {
  team: Team;
  employees: Employee[];
  projectName: string;
  onEdit: () => void;
  onDelete: () => void;
  onUpdated: (t: Team) => void;
  onError: (msg: string) => void;
}) {
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
          <p className="text-lg font-black text-foreground">{team.name}</p>
          <p className="text-xs text-muted-foreground">
            {projectName} · {team.layerCount} layer{team.layerCount === 1 ? "" : "s"}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={onEdit}
            className="rounded-full border border-border/60 bg-card px-3 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground"
          >
            Edit
          </button>
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

      {/* Roster grouped by layer, top layer first */}
      <div className="mt-4 space-y-3">
        {[...layers].reverse().map((layer) => {
          const members = team.members.filter((m) => m.layer === layer);
          return (
            <div key={layer} className="rounded-2xl border border-border/60 bg-background/50 p-3">
              <p className="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">
                {layerLabel(team, layer)}
              </p>
              {members.length === 0 ? (
                <p className="mt-1 text-xs text-muted-foreground">No one at this layer.</p>
              ) : (
                <ul className="mt-2 space-y-1.5">
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
