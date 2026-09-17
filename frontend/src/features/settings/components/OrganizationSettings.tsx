import { useEffect, useState } from "react";
import { LoaderCircle } from "lucide-react";
import {
  getOrganization,
  orgToUpdate,
  updateOrganization,
  type Organization,
} from "../api";
import { XeroConnectionCard } from "./XeroConnectionCard";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonPanel } from "@/shared/components/Skeleton";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50";
const LABEL = "block text-sm font-semibold text-foreground";

export function OrganizationSettings() {
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const orgQuery = useCachedQuery("/organizations/current", getOrganization);
  // A WORKING COPY, seeded from the cache so a revisit renders the form on
  // the first frame instead of a blank one. Deliberately not re-bound to the
  // query afterwards: a background refresh landing mid-edit would replace
  // what is being typed. The effect below only fills it on a cold load,
  // where there was nothing to type over.
  const [org, setOrg] = useState<Organization | null>(() => orgQuery.data ?? null);

  const loading = orgQuery.loading;

  useEffect(() => {
    if (orgQuery.data) setOrg((current) => current ?? orgQuery.data!);
  }, [orgQuery.data]);
  useEffect(() => {
    if (orgQuery.error) setError(orgQuery.error);
  }, [orgQuery.error]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!org) return;
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      // Only the name is edited here; the rest of the org's fields are carried
      // through so this save never resets currency / mileage / geofence /
      // schedule (each edited on its own screen).
      const updated = await updateOrganization(orgToUpdate(org));
      setOrg(updated);
      setSaved(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save.");
    } finally {
      setSaving(false);
    }
  }

  if (loading) {
    return <SkeletonPanel />;
  }
  if (!org) {
    return (
      <div className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
        {error ?? "Could not load the organization."}
      </div>
    );
  }

  return (
    <div className="space-y-5">
      {/* Integrations sit with the company, not with the accounts they happen to
          own — and the claims screen sends admins to System Settings to connect. */}
      <XeroConnectionCard />

      <form onSubmit={handleSubmit} className={`${CARD} space-y-5`}>
        <div>
          <h2 className="text-lg font-black text-foreground">Organization</h2>
          <p className="text-sm text-muted-foreground">The company's name.</p>
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

        {/* The other org defaults each live with what they govern now. */}
        <div className="rounded-2xl border border-border/60 bg-background/50 p-4 text-xs leading-6 text-muted-foreground">
          <p className="font-semibold text-foreground">Looking for the other settings?</p>
          <ul className="mt-1 space-y-0.5">
            <li>Default currency → <span className="font-semibold text-foreground">Claims → Settings</span></li>
            <li>Mileage rate &amp; unit → <span className="font-semibold text-foreground">Settings → Accounts</span></li>
            <li>Geofence radius → <span className="font-semibold text-foreground">Settings → Projects</span></li>
            <li>Work schedule &amp; holidays → <span className="font-semibold text-foreground">Settings → Work Schedule</span></li>
          </ul>
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
    </div>
  );
}
