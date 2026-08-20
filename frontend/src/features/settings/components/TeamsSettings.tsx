import { useEffect, useMemo, useState } from "react";
import { LoaderCircle, Plus, Trash2, UserPlus } from "lucide-react";
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
import { SearchInput } from "@/shared/components/SearchInput";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

export function TeamsSettings() {
  const [teams, setTeams] = useState<Team[]>([]);
  const [projects, setProjects] = useState<Project[]>([]);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<Team | null>(null);
  const [creating, setCreating] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [searchTerm, setSearchTerm] = useState("");

  useEffect(() => {
    Promise.all([getTeams(), getProjects(), getEmployees()])
      .then(([t, p, e]) => {
        setTeams(t);
        setProjects(p.filter((x) => !x.isArchived));
        setEmployees(e);
      })
      .catch((err: unknown) => setError(message(err, "Could not load teams.")))
      .finally(() => setLoading(false));
  }, []);

  const projectName = (id: string) => projects.find((p) => p.id === id)?.name ?? "Project";
  const upsert = (t: Team) =>
    setTeams((cur) => (cur.some((x) => x.id === t.id) ? cur.map((x) => (x.id === t.id ? t : x)) : [...cur, t]));
  const filteredTeams = useMemo(() => {
    const query = searchTerm.trim().toLowerCase();
    if (!query) return teams;
    return teams.filter((team) =>
      [
        team.name,
        projectName(team.projectId),
        ...team.members.map((member) => member.email ?? member.employeeId),
      ]
        .filter(Boolean)
        .join(" ")
        .toLowerCase()
        .includes(query),
    );
  }, [projects, searchTerm, teams]);

  async function onDelete(team: Team) {
    if (!window.confirm(`Delete team "${team.name}"? Its members are unassigned.`)) return;
    setBusyId(team.id);
    setError(null);
    try {
      await deleteTeam(team.id);
      setTeams((cur) => cur.filter((t) => t.id !== team.id));
    } catch (err) {
      setError(message(err, "Could not delete the team."));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className="space-y-5">
      <div className={`${CARD} flex flex-col gap-4`}>
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2 className="text-lg font-black text-foreground">Teams</h2>
            <p className="text-sm text-muted-foreground">
              Layered teams within a project. Members sit at a layer; approval chains escalate up the
              layers (next step).
            </p>
          </div>
          <button
            type="button"
            onClick={() => setCreating(true)}
            disabled={projects.length === 0}
            className="inline-flex shrink-0 items-center gap-2 rounded-2xl bg-primary px-4 py-2.5 text-sm font-semibold text-primary-foreground transition hover:opacity-90 disabled:opacity-50"
          >
            <Plus className="h-4 w-4" />
            New team
          </button>
        </div>
        <SearchInput
          value={searchTerm}
          onChange={setSearchTerm}
          placeholder="Search by team, project, or employee"
          inputClassName="h-10 rounded-xl border-border/70 bg-card/90 focus-visible:ring-primary focus-visible:ring-offset-0"
        />
      </div>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {loading ? (
        <p className="text-sm text-muted-foreground">Loading teams…</p>
      ) : teams.length === 0 ? (
        <p className="text-sm text-muted-foreground">No teams yet.</p>
      ) : filteredTeams.length === 0 ? (
        <p className="text-sm text-muted-foreground">No teams match this search.</p>
      ) : (
        filteredTeams.map((team) => (
          <TeamCard
            key={team.id}
            team={team}
            employees={employees}
            projectName={projectName(team.projectId)}
            busy={busyId === team.id}
            onEdit={() => setEditing(team)}
            onDelete={() => onDelete(team)}
            onUpdated={upsert}
            onError={setError}
          />
        ))
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

function TeamCard({
  team,
  employees,
  projectName,
  busy,
  onEdit,
  onDelete,
  onUpdated,
  onError,
}: {
  team: Team;
  employees: Employee[];
  projectName: string;
  busy: boolean;
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
    <div className={CARD}>
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
            disabled={busy}
            onClick={onDelete}
            className="inline-flex items-center gap-1 rounded-full border border-destructive/30 bg-card px-3 py-1.5 text-xs font-semibold text-destructive transition-colors hover:bg-destructive/5 disabled:opacity-50"
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
                    <li key={m.employeeId} className="flex flex-wrap items-center justify-between gap-2">
                      <span className="text-sm font-medium text-foreground">{m.email ?? m.employeeId}</span>
                      <div className="flex items-center gap-2">
                        <Select
                          value={String(m.layer)}
                          onValueChange={(v) =>
                            run(() => addTeamMember(team.id, { employeeId: m.employeeId, layer: Number(v) }))
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
          {working ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <UserPlus className="h-4 w-4" />}
          Add
        </button>
      </div>
    </div>
  );
}
