import { useState } from "react";
import { createPortal } from "react-dom";
import { ArrowLeftRight, Building2, Check, LoaderCircle, X } from "lucide-react";
import { createOrganization } from "../api";
import { switchOrg } from "@/features/auth/api";

// Create a new company. The caller becomes its Owner (server-side), so it's a
// company they can run and switch straight into. Kept deliberately minimal: only
// the name is required.
export function CreateCompanyDialog({ onClose }: { onClose: () => void }) {
  const [name, setName] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [created, setCreated] = useState<{ id: string; name: string } | null>(null);
  const [switching, setSwitching] = useState(false);

  async function submit() {
    const trimmed = name.trim();
    if (!trimmed) return;
    setSaving(true);
    setError(null);
    try {
      const org = await createOrganization(trimmed);
      setCreated({ id: org.id, name: org.name });
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not create the company.");
    } finally {
      setSaving(false);
    }
  }

  async function switchNow() {
    if (!created) return;
    setSwitching(true);
    setError(null);
    try {
      await switchOrg(created.id);
      window.location.reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not switch to the new company.");
      setSwitching(false);
    }
  }

  return createPortal(
    <div
      className="fixed inset-0 z-[70] flex items-center justify-center bg-background/80 p-4 backdrop-blur-sm"
      onClick={onClose}
    >
      <div
        className="w-full max-w-md rounded-[28px] border border-border/70 bg-card p-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-center gap-2">
            <span className="flex size-9 items-center justify-center rounded-full bg-primary/10 text-primary">
              <Building2 className="h-4 w-4" />
            </span>
            <h3 className="text-base font-black text-foreground">New company</h3>
          </div>
          <button
            type="button"
            aria-label="Close"
            onClick={onClose}
            className="rounded-full p-2 text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        {created ? (
          <div className="mt-4 space-y-4">
            <div className="flex items-start gap-2 rounded-2xl border border-emerald-500/30 bg-emerald-500/10 p-4 text-sm text-emerald-800 dark:text-emerald-300">
              <Check className="mt-0.5 h-4 w-4 shrink-0" />
              <p>
                <strong>{created.name}</strong> was created and you're its Owner. Switch to it now to
                start setting it up, or stay in your current company.
              </p>
            </div>
            {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}
            <div className="flex justify-end gap-2">
              <button
                type="button"
                onClick={onClose}
                disabled={switching}
                className="rounded-2xl bg-muted px-4 py-2 text-sm font-semibold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
              >
                Not now
              </button>
              <button
                type="button"
                onClick={() => void switchNow()}
                disabled={switching}
                className="inline-flex items-center gap-2 rounded-2xl bg-primary px-4 py-2 text-sm font-semibold text-primary-foreground transition hover:bg-primary/90 disabled:opacity-50"
              >
                {switching ? (
                  <LoaderCircle className="h-4 w-4 animate-spin" />
                ) : (
                  <ArrowLeftRight className="h-4 w-4" />
                )}
                Switch now
              </button>
            </div>
          </div>
        ) : (
          <div className="mt-4 space-y-4">
            <label className="block space-y-1.5">
              <span className="text-sm font-semibold text-foreground">Company name</span>
              <input
                autoFocus
                value={name}
                onChange={(e) => setName(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") void submit();
                }}
                placeholder="Acme Sdn Bhd"
                className="h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              />
              <span className="text-xs text-muted-foreground">
                Defaults (MYR, KM, 200m geofence) are applied and can be changed later.
              </span>
            </label>

            {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

            <div className="flex justify-end gap-2">
              <button
                type="button"
                onClick={onClose}
                disabled={saving}
                className="rounded-2xl bg-muted px-4 py-2 text-sm font-semibold text-muted-foreground transition hover:text-foreground disabled:opacity-50"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={() => void submit()}
                disabled={saving || !name.trim()}
                className="inline-flex items-center gap-2 rounded-2xl bg-primary px-4 py-2 text-sm font-semibold text-primary-foreground transition hover:bg-primary/90 disabled:opacity-50"
              >
                {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                Create company
              </button>
            </div>
          </div>
        )}
      </div>
    </div>,
    document.body,
  );
}
