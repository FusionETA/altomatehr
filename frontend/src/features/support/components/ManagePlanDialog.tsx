import { useState } from "react";
import { createPortal } from "react-dom";
import { LoaderCircle, X } from "lucide-react";
import { Switch } from "@/shared/components/ui/switch";
import { ADDONS, updateOrganizationPlan, type Plan, type SupportOrganization, type Tier } from "../api";

const choice = (active: boolean) =>
  `flex-1 rounded-2xl border px-3 py-2 text-sm font-semibold transition ${
    active
      ? "border-primary bg-primary/10 text-primary"
      : "border-border bg-card text-muted-foreground hover:text-foreground"
  }`;

// A company's package: plan, tier and the paid add-ons. Takes effect on the
// company's next request — module access is read fresh every time.
export function ManagePlanDialog({
  org,
  onClose,
  onSaved,
}: {
  org: SupportOrganization;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [plan, setPlan] = useState<Plan>(org.plan === "EXPERT" ? "EXPERT" : "DIY");
  const [tier, setTier] = useState<Tier>(org.tier === "FREE" ? "FREE" : "PAID");
  const [addons, setAddons] = useState<string[]>(org.addons);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // DIY Free never unlocks an add-on (OrgModules.DeriveOrgEnabledModules), so
  // the switches would only mislead there.
  const addonsApply = !(plan === "DIY" && tier === "FREE");

  async function save() {
    setSaving(true);
    setError(null);
    try {
      await updateOrganizationPlan(org.id, {
        plan,
        tier: plan === "DIY" ? tier : null,
        addons,
      });
      onSaved();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not update the plan.");
      setSaving(false);
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
          <div className="min-w-0">
            <h3 className="text-base font-black text-foreground">Manage plan</h3>
            <p className="truncate text-sm text-muted-foreground">{org.name}</p>
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

        <div className="mt-5 space-y-5">
          <div className="space-y-1.5">
            <p className="text-sm font-semibold text-foreground">Plan</p>
            <div className="flex gap-2">
              {(["DIY", "EXPERT"] as const).map((p) => (
                <button key={p} type="button" onClick={() => setPlan(p)} className={choice(plan === p)}>
                  {p}
                </button>
              ))}
            </div>
          </div>

          {plan === "DIY" ? (
            <div className="space-y-1.5">
              <p className="text-sm font-semibold text-foreground">Tier</p>
              <div className="flex gap-2">
                {(["FREE", "PAID"] as const).map((t) => (
                  <button key={t} type="button" onClick={() => setTier(t)} className={choice(tier === t)}>
                    {t}
                  </button>
                ))}
              </div>
            </div>
          ) : null}

          <div className="space-y-2">
            <p className="text-sm font-semibold text-foreground">Add-on modules</p>
            {ADDONS.map((a) => (
              <label key={a.key} className="flex items-center justify-between gap-3 rounded-2xl border border-border/60 px-4 py-2.5">
                <span className="text-sm font-medium text-foreground">{a.label}</span>
                <Switch
                  checked={addons.includes(a.key)}
                  disabled={!addonsApply}
                  onCheckedChange={(on) =>
                    setAddons((cur) => (on ? [...cur, a.key] : cur.filter((k) => k !== a.key)))
                  }
                />
              </label>
            ))}
            {!addonsApply ? (
              <p className="text-xs text-muted-foreground">DIY Free includes the core modules only.</p>
            ) : null}
          </div>

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
              onClick={() => void save()}
              disabled={saving}
              className="inline-flex items-center gap-2 rounded-2xl bg-primary px-4 py-2 text-sm font-semibold text-primary-foreground transition hover:bg-primary/90 disabled:opacity-50"
            >
              {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
              Save plan
            </button>
          </div>
        </div>
      </div>
    </div>,
    document.body,
  );
}
