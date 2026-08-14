import { useEffect, useState } from "react";
import { Plus, Star } from "lucide-react";
import {
  archivePolicy,
  getPolicies,
  restorePolicy,
  setDefaultPolicy,
  type Policy,
} from "@/features/policies/api";
import { getLeaveTypes, type LeaveType } from "@/features/leave/api";
import { PolicyEditorModal } from "./PolicyEditorModal";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm sm:p-6";

function message(err: unknown, fallback: string) {
  return err instanceof Error ? err.message : fallback;
}

export function PoliciesSettings() {
  const [policies, setPolicies] = useState<Policy[]>([]);
  const [leaveTypes, setLeaveTypes] = useState<LeaveType[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [editing, setEditing] = useState<Policy | null>(null);
  const [creating, setCreating] = useState(false);

  useEffect(() => {
    Promise.all([getPolicies(), getLeaveTypes()])
      .then(([p, t]) => {
        setPolicies(p);
        setLeaveTypes(t.filter((x) => !x.isArchived));
      })
      .catch((e: unknown) => setError(message(e, "Could not load policies.")))
      .finally(() => setLoading(false));
  }, []);

  // Insert/replace a policy, keeping "exactly one default" consistent locally.
  function applyPolicy(p: Policy) {
    setPolicies((cur) => {
      const exists = cur.some((x) => x.id === p.id);
      const next = exists ? cur.map((x) => (x.id === p.id ? p : x)) : [...cur, p];
      return p.isDefault ? next.map((x) => (x.id === p.id ? x : { ...x, isDefault: false })) : next;
    });
  }

  async function act(id: string, fn: (id: string) => Promise<Policy>) {
    setBusyId(id);
    setError(null);
    try {
      applyPolicy(await fn(id));
    } catch (err) {
      setError(message(err, "Could not update the policy."));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className={`${CARD} space-y-5`}>
      <div className="flex items-start justify-between gap-4">
        <div>
          <h2 className="text-lg font-black text-foreground">Policies</h2>
          <p className="text-sm text-muted-foreground">
            Rule bundles assigned to employees — module access, attendance enforcement, OT, and leave
            entitlements.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setCreating(true)}
          className="inline-flex shrink-0 items-center gap-2 rounded-2xl bg-primary px-4 py-2.5 text-sm font-semibold text-primary-foreground transition hover:opacity-90"
        >
          <Plus className="h-4 w-4" />
          New policy
        </button>
      </div>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {loading ? (
        <p className="text-sm text-muted-foreground">Loading policies…</p>
      ) : policies.length === 0 ? (
        <p className="text-sm text-muted-foreground">No policies yet.</p>
      ) : (
        <ul className="divide-y divide-border/60 overflow-hidden rounded-2xl border border-border/60">
          {policies.map((p) => (
            <li key={p.id} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
              <div className="min-w-0">
                <p
                  className={`flex items-center gap-2 font-semibold ${
                    p.isArchived ? "text-muted-foreground line-through" : "text-foreground"
                  }`}
                >
                  {p.name}
                  {p.isDefault ? (
                    <span className="inline-flex items-center gap-1 rounded-full bg-primary/10 px-2 py-0.5 text-[10px] font-bold uppercase tracking-[0.12em] text-primary">
                      <Star className="h-3 w-3" /> Default
                    </span>
                  ) : null}
                </p>
                <p className="text-xs text-muted-foreground">
                  {p.salaryType === "MONTHLY" ? "Monthly" : "Hourly"} ·{" "}
                  {p.requireGeofence ? "geofenced" : "no geofence"} ·{" "}
                  {p.otEnabled ? "OT on" : "OT off"}
                  {p.leaveEntitlements.length > 0 ? ` · ${p.leaveEntitlements.length} leave override(s)` : ""}
                </p>
              </div>
              <div className="flex shrink-0 items-center gap-2">
                <button
                  type="button"
                  onClick={() => setEditing(p)}
                  className="rounded-full border border-border/60 bg-card px-3 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground"
                >
                  Edit
                </button>
                {!p.isDefault && !p.isArchived ? (
                  <button
                    type="button"
                    disabled={busyId === p.id}
                    onClick={() => act(p.id, setDefaultPolicy)}
                    className="rounded-full border border-border/60 bg-card px-3 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground disabled:opacity-50"
                  >
                    Make default
                  </button>
                ) : null}
                {!p.isDefault ? (
                  <button
                    type="button"
                    disabled={busyId === p.id}
                    onClick={() => act(p.id, p.isArchived ? restorePolicy : archivePolicy)}
                    className="rounded-full border border-border/60 bg-card px-3 py-1.5 text-xs font-semibold text-muted-foreground transition-colors hover:text-foreground disabled:opacity-50"
                  >
                    {p.isArchived ? "Restore" : "Archive"}
                  </button>
                ) : null}
              </div>
            </li>
          ))}
        </ul>
      )}

      {creating ? (
        <PolicyEditorModal
          policy={null}
          leaveTypes={leaveTypes}
          onClose={() => setCreating(false)}
          onSaved={applyPolicy}
        />
      ) : null}
      {editing ? (
        <PolicyEditorModal
          policy={editing}
          leaveTypes={leaveTypes}
          onClose={() => setEditing(null)}
          onSaved={applyPolicy}
        />
      ) : null}
    </div>
  );
}
