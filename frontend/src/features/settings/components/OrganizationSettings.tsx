import { useEffect, useState } from "react";
import { LoaderCircle } from "lucide-react";
import { getOrganization, updateOrganization, type Organization } from "../api";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-[0_12px_30px_rgba(76,26,134,0.07)] backdrop-blur-sm sm:p-6";
const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50";
const LABEL = "block text-sm font-semibold text-foreground";

export function OrganizationSettings() {
  const [org, setOrg] = useState<Organization | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    getOrganization()
      .then(setOrg)
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, []);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!org) return;
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      const updated = await updateOrganization({
        name: org.name,
        defaultCurrency: org.defaultCurrency,
        defaultMileageRate: org.defaultMileageRate,
        mileageUnit: org.mileageUnit,
        geofenceRadiusMeters: org.geofenceRadiusMeters,
      });
      setOrg(updated);
      setSaved(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save.");
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return <div className={`${CARD} text-sm text-muted-foreground`}>Loading organization…</div>;
  }
  if (!org) {
    return (
      <div className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
        {error ?? "Could not load the organization."}
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit} className={`${CARD} space-y-5`}>
      <div>
        <h2 className="text-lg font-black text-foreground">Organization</h2>
        <p className="text-sm text-muted-foreground">Company details and defaults for this org.</p>
      </div>

      <div className="space-y-2">
        <label htmlFor="org-name" className={LABEL}>Company name</label>
        <input
          id="org-name"
          className={INPUT}
          value={org.name}
          onChange={(e) => setOrg({ ...org, name: e.target.value })}
        />
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-2">
          <label htmlFor="org-currency" className={LABEL}>Default currency</label>
          <input
            id="org-currency"
            className={INPUT}
            maxLength={3}
            value={org.defaultCurrency}
            onChange={(e) => setOrg({ ...org, defaultCurrency: e.target.value.toUpperCase() })}
            placeholder="MYR"
          />
        </div>
        <div className="space-y-2">
          <label htmlFor="org-mileage" className={LABEL}>Default mileage rate</label>
          <input
            id="org-mileage"
            type="number"
            step="0.01"
            min="0"
            className={INPUT}
            value={org.defaultMileageRate}
            onChange={(e) => setOrg({ ...org, defaultMileageRate: Number(e.target.value) })}
          />
        </div>
        <div className="space-y-2">
          <label htmlFor="org-mileage-unit" className={LABEL}>Mileage unit</label>
          <select
            id="org-mileage-unit"
            className={INPUT}
            value={org.mileageUnit}
            onChange={(e) => setOrg({ ...org, mileageUnit: e.target.value as "KM" | "MILE" })}
          >
            <option value="KM">Kilometres</option>
            <option value="MILE">Miles</option>
          </select>
        </div>
        <div className="space-y-2">
          <label htmlFor="org-geofence" className={LABEL}>Geofence radius (metres)</label>
          <input
            id="org-geofence"
            type="number"
            step="10"
            min="10"
            className={INPUT}
            value={org.geofenceRadiusMeters}
            onChange={(e) => setOrg({ ...org, geofenceRadiusMeters: Number(e.target.value) })}
          />
          <p className="text-xs text-muted-foreground">
            How close to a project's pin still counts as on-site. Default 200.
          </p>
        </div>
      </div>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}
      {saved ? <p className="text-sm font-medium text-primary">Saved.</p> : null}

      <button
        type="submit"
        disabled={saving}
        className="inline-flex items-center justify-center gap-2 rounded-2xl bg-primary px-5 py-2.5 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:opacity-90 disabled:opacity-50"
      >
        {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
        Save changes
      </button>
    </form>
  );
}
