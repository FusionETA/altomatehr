import { type FormEvent, useState } from "react";
import { Check, LoaderCircle, Pencil, X } from "lucide-react";
import { useMyAccess } from "@/features/settings/lib/module-access";
import { renamePayrollRun, type PayrollRun } from "../api";
import { BUTTON_GHOST_SM, BUTTON_SM, INPUT_SM } from "../lib/ui";

// The run's title — "October 2026 – Workers" — with a rename for admins.
// A name is only a label, so it can change in any status; it tells the runs
// of a split month (and their files) apart.
export function PayrollRunName({ run, onRenamed }: { run: PayrollRun; onRenamed: () => void }) {
  const access = useMyAccess();
  const canRename = access.allEmployees && access.canManage("payroll");
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(run.name ?? "");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save(e: FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    try {
      await renamePayrollRun(run.id, name.trim());
      setEditing(false);
      onRenamed();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not rename the run.");
    } finally {
      setSaving(false);
    }
  }

  if (!editing) {
    return (
      <div className="flex items-center gap-1.5">
        <h1 className="text-xl font-semibold text-foreground">{run.label}</h1>
        {canRename ? (
          <button
            type="button"
            className="rounded-lg p-1 text-muted-foreground transition hover:bg-muted/60 hover:text-foreground"
            aria-label={run.name ? "Rename this run" : "Name this run"}
            title={run.name ? "Rename this run" : "Name this run"}
            onClick={() => {
              setName(run.name ?? "");
              setError(null);
              setEditing(true);
            }}
          >
            <Pencil className="size-3.5" aria-hidden />
          </button>
        ) : null}
      </div>
    );
  }

  return (
    <form onSubmit={save} className="space-y-1">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-xl font-semibold text-foreground">{run.periodLabel} –</span>
        <div className="w-48">
          <input
            autoFocus
            maxLength={60}
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Workers"
            aria-label="Run name"
            className={INPUT_SM}
          />
        </div>
        <button type="submit" className={BUTTON_SM} disabled={saving}>
          {saving ? (
            <LoaderCircle className="size-4 animate-spin" aria-hidden />
          ) : (
            <Check className="size-4" aria-hidden />
          )}
          Save
        </button>
        <button
          type="button"
          className={BUTTON_GHOST_SM}
          disabled={saving}
          onClick={() => setEditing(false)}
        >
          <X className="size-4" aria-hidden />
          Cancel
        </button>
      </div>
      {error ? <p className="text-xs font-medium text-destructive">{error}</p> : null}
    </form>
  );
}
