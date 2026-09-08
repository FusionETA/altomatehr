import { type FormEvent, useState } from "react";
import { LoaderCircle, Lock } from "lucide-react";
import {
  APPROVAL_MODULES,
  createTeam,
  updateTeam,
  type ModuleApprovalConfig,
  type Team,
} from "@/features/teams/api";
import type { Project } from "@/features/settings/api";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";

const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-white/80 px-4 text-sm shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary";

// The team editor, living in the third pane rather than a modal.
//
// Follows the previous system's flow: pick a project, pick a team, edit it in
// place. A modal made the pane a placeholder that said "Pick a team" and then
// covered the very list you picked from — you couldn't see the roster you were
// changing the layers for.
export function TeamEditor({
  team,
  projects,
  onCancel,
  onSaved,
}: {
  /** Null puts the panel in create mode. */
  team: Team | null;
  projects: Project[];
  onCancel: () => void;
  onSaved: (t: Team) => void;
}) {
  const [projectId, setProjectId] = useState(team?.projectId ?? projects[0]?.id ?? "");
  const [name, setName] = useState(team?.name ?? "");
  const [layerCount, setLayerCount] = useState(team?.layerCount ?? 1);
  const [labels, setLabels] = useState<string[]>(() =>
    Array.from({ length: team?.layerCount ?? 1 }, (_, i) => team?.layerLabels[i] ?? ""),
  );
  // Module → layer indices that approve. Defaults to every layer for every module.
  const [config, setConfig] = useState<ModuleApprovalConfig>(() => {
    const existing = team?.moduleApprovalConfig ?? {};
    const all = Array.from({ length: team?.layerCount ?? 1 }, (_, i) => i);
    return Object.fromEntries(APPROVAL_MODULES.map((m) => [m, existing[m] ?? all]));
  });
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function setCount(n: number) {
    const count = Math.max(1, Math.min(6, Math.round(n) || 1));
    const grew = count > layerCount;
    setLayerCount(count);
    setLabels((prev) => Array.from({ length: count }, (_, i) => prev[i] ?? ""));
    setConfig((prev) =>
      Object.fromEntries(
        APPROVAL_MODULES.map((m) => {
          const within = (prev[m] ?? []).filter((l) => l < count);

          // A newly-added layer approves by default — but only where the module
          // already has an approving layer. Without this, growing the team left
          // the new layer approving nothing, silently: an admin adds a Manager
          // tier and it reviews none of the four modules until they notice.
          //
          // An intentionally emptied column means "skip approvals for this
          // module", so it stays empty rather than being re-populated behind
          // the admin's back.
          if (grew && within.length > 0) {
            for (let l = layerCount; l < count; l += 1) within.push(l);
            within.sort((a, b) => a - b);
          }
          return [m, within];
        }),
      ),
    );
  }

  function toggle(module: string, layer: number) {
    setConfig((prev) => {
      const set = new Set(prev[module] ?? []);
      if (set.has(layer)) set.delete(layer);
      else set.add(layer);
      return { ...prev, [module]: [...set].sort((a, b) => a - b) };
    });
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!name.trim() || (!team && !projectId)) return;
    setSaving(true);
    setError(null);
    const layerLabels = labels.map((l) => l.trim());
    // Layer 0 is always present in what gets saved — the row is locked on, so
    // the payload has to agree with the screen.
    const moduleApprovalConfig = Object.fromEntries(
      APPROVAL_MODULES.map((m) => {
        const layers = new Set((config[m] ?? []).filter((l) => l < layerCount));
        layers.add(0);
        return [m, [...layers].sort((a, b) => a - b)];
      }),
    );
    const base = { name: name.trim(), layerCount, layerLabels, moduleApprovalConfig };
    try {
      const saved = team ? await updateTeam(team.id, base) : await createTeam({ projectId, ...base });
      // The parent selects the new team, so creating flips straight into
      // editing it rather than dropping back to an empty pane.
      onSaved(saved);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save the team.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <form onSubmit={handleSubmit}>
          <div className="flex items-start justify-between gap-3 border-b border-border/60 pb-3">
            <div>
              <h3 className="text-base font-black text-foreground">
                {team ? team.name : "New team"}
              </h3>
              <p className="text-xs text-muted-foreground">
                {team ? "Layers and approvals" : "Name it, then set its layers"}
              </p>
            </div>
          </div>

          <div className="mt-5 space-y-4">
            <div className="space-y-2">
              <span className="text-sm font-semibold text-foreground">Project</span>
              {team ? (
                <input className={`${INPUT} opacity-60`} value={projects.find((p) => p.id === team.projectId)?.name ?? "—"} disabled />
              ) : (
                <Select value={projectId} onValueChange={setProjectId}>
                  <SelectTrigger>
                    <SelectValue placeholder="Select a project" />
                  </SelectTrigger>
                  <SelectContent searchPlaceholder="Search projects…">
                    {projects.map((p) => (
                      <SelectItem key={p.id} value={p.id}>
                        {p.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </div>

            <div className="grid gap-4 sm:grid-cols-[1fr_140px]">
              <label className="block space-y-2">
                <span className="text-sm font-semibold text-foreground">Team name</span>
                <input required className={INPUT} value={name} onChange={(e) => setName(e.target.value)} placeholder="Field Crew" />
              </label>
              <label className="block space-y-2">
                <span className="text-sm font-semibold text-foreground">Layers</span>
                <input
                  type="number"
                  min="1"
                  max="6"
                  className={INPUT}
                  value={layerCount}
                  onChange={(e) => setCount(Number(e.target.value))}
                />
              </label>
            </div>

            <div className="space-y-2">
              <span className="text-sm font-semibold text-foreground">Layer labels</span>
              <p className="text-xs text-muted-foreground">Bottom to top — higher layers approve after lower ones.</p>
              <div className="space-y-2">
                {labels.map((label, i) => (
                  <div key={i} className="flex items-center gap-2">
                    <span className="w-16 shrink-0 text-xs font-semibold text-muted-foreground">
                      Layer {i + 1}
                    </span>
                    <input
                      className={INPUT}
                      value={label}
                      onChange={(e) => setLabels((prev) => prev.map((l, j) => (j === i ? e.target.value : l)))}
                      placeholder={i === 0 ? "Staff" : i === labels.length - 1 ? "Manager" : "Lead"}
                    />
                  </div>
                ))}
              </div>
            </div>

            <div className="space-y-2">
              <span className="text-sm font-semibold text-foreground">Module approval config</span>
              <p className="text-xs text-muted-foreground">
                Tick the layers that must approve each module. An empty column skips approvals.
              </p>
              <div className="overflow-x-auto rounded-2xl border border-border/60 bg-background/50 p-2">
                <table className="w-full min-w-[360px] text-sm">
                  <thead>
                    <tr className="border-b border-border/60">
                      <th className="py-2 pl-2 pr-2 text-left text-[11px] font-bold uppercase tracking-[0.12em] text-muted-foreground">
                        Layer
                      </th>
                      {APPROVAL_MODULES.map((m) => (
                        <th key={m} className="px-2 py-2 text-center text-[11px] font-bold uppercase tracking-[0.1em] text-muted-foreground">
                          {m}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {labels.map((label, i) => {
                      // The bottom layer is locked on. It's the base of the
                      // team — where requests come FROM — so it isn't a choice.
                      const locked = i === 0;
                      return (
                        <tr key={i} className="border-b border-border/60 last:border-b-0">
                          <td className="py-2 pl-2 pr-2 text-sm font-medium text-foreground">
                            {label.trim() || `Layer ${i + 1}`}
                            {locked ? (
                              <Lock className="ml-1.5 inline h-3 w-3 -translate-y-px text-muted-foreground" />
                            ) : null}
                          </td>
                          {APPROVAL_MODULES.map((m) => (
                            <td key={m} className="px-2 py-2 text-center">
                              <input
                                type="checkbox"
                                disabled={locked}
                                aria-label={
                                  locked
                                    ? `${m}: the base layer is always included`
                                    : `${m} approved at ${label.trim() || `layer ${i + 1}`}`
                                }
                                className="h-4 w-4 rounded border-border accent-primary disabled:opacity-60"
                                checked={locked || (config[m] ?? []).includes(i)}
                                onChange={locked ? undefined : () => toggle(m, i)}
                              />
                            </td>
                          ))}
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
              <p className="text-xs text-muted-foreground">
                {/* Said plainly because the locked row otherwise reads as "Staff
                    approves everything", which never happens: a chain starts at
                    the layer ABOVE the person filing, so nothing routes to the
                    bottom layer — there is nobody below it. */}
                The base layer is locked on: it's where requests are filed from, so it never
                receives one to approve.
              </p>
              <p className="text-xs text-muted-foreground">
                OT &amp; Attendance are saved but only take effect once those modules land.
              </p>
            </div>
          </div>

          {error ? (
            <p className="mt-4 rounded-2xl border border-destructive/20 bg-destructive/5 px-4 py-3 text-sm text-destructive">
              {error}
            </p>
          ) : null}

          <div className="mt-6 flex justify-end gap-3">
            {/* Nothing to cancel out of once a team exists — the panel just
                shows the selected team, so a Cancel would be a no-op button. */}
            {team ? null : (
              <button
                type="button"
                onClick={onCancel}
                className="rounded-2xl bg-muted px-4 py-3 text-sm font-semibold text-muted-foreground transition hover:text-foreground"
              >
                Cancel
              </button>
            )}
            <button
              type="submit"
              disabled={saving || !name.trim() || (!team && !projectId)}
              className="inline-flex items-center justify-center gap-2 rounded-2xl bg-primary px-5 py-3 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:bg-primary/90 disabled:pointer-events-none disabled:opacity-50"
            >
              {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
              {team ? "Save changes" : "Create team"}
            </button>
          </div>
    </form>
  );
}
