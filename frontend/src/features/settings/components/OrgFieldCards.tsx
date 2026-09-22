import { useEffect, useState } from "react";
import { LoaderCircle, RefreshCw } from "lucide-react";
import {
  getOrganization,
  getXeroCurrencies,
  orgToUpdate,
  updateOrganization,
  type Organization,
  type XeroCurrency,
} from "../api";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonPanel } from "@/shared/components/Skeleton";

// These three cards each edit ONE slice of the organization, but live on the
// screen that slice belongs to — currency with Claims, mileage with Accounts,
// geofence with Projects — matching the previous system. They share the org's
// full-replace PUT via orgToUpdate(), so each save preserves the fields the
// other screens own.

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50";
const LABEL = "block text-sm font-semibold text-foreground";
const SAVE_BUTTON =
  "inline-flex items-center justify-center gap-2 rounded-2xl bg-primary px-5 py-2.5 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:opacity-90 disabled:opacity-50";

// Shared load-edit-save shell for an org slice. Renders a skeleton until the org
// is loaded, then hands the working copy to `children` and drives the save.
function OrgSliceCard({
  save,
  children,
}: {
  // Builds the update payload from the edited working copy.
  save: (org: Organization) => Promise<Organization>;
  children: (
    org: Organization,
    setOrg: (next: Organization) => void,
    disabled: boolean,
  ) => React.ReactNode;
}) {
  const orgQuery = useCachedQuery("/organizations/current", getOrganization);
  // A WORKING COPY, seeded from the cache so a revisit renders the form on
  // the first frame instead of a blank one. Deliberately not re-bound to the
  // query afterwards: a background refresh landing mid-edit would replace
  // what is being typed. The effect below only fills it on a cold load,
  // where there was nothing to type over.
  const [org, setOrg] = useState<Organization | null>(() => orgQuery.data ?? null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    if (orgQuery.data) setOrg((current) => current ?? orgQuery.data!);
  }, [orgQuery.data]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!org) return;
    setSaving(true);
    setError(null);
    setSaved(false);
    try {
      setOrg(await save(org));
      setSaved(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not save.");
    } finally {
      setSaving(false);
    }
  }

  if (orgQuery.loading) return <SkeletonPanel />;
  if (!org) {
    return (
      <div className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
        {orgQuery.error ?? "Could not load the organization."}
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit} className={`${CARD} space-y-4`}>
      {children(
        org,
        (next) => {
          setSaved(false);
          setOrg(next);
        },
        saving,
      )}
      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}
      {saved ? <p className="text-sm font-medium text-primary">Saved.</p> : null}
      <button type="submit" disabled={saving} className={SAVE_BUTTON}>
        {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
        Save
      </button>
    </form>
  );
}

// Default claim currency. Picks from Xero's currencies when connected, free text
// otherwise — every claim is denominated in this, so it lives with Claims.
export function OrgCurrencyCard() {
  const currenciesQuery = useCachedQuery("/xero/currencies", getXeroCurrencies);
  const currencies: XeroCurrency[] = currenciesQuery.data ?? [];

  // /xero/currencies asks Xero live, so "sync" is just this read again — it's
  // the client's 30s cache that would otherwise hide a currency added in Xero
  // a minute ago.
  const syncing = currenciesQuery.loading;

  return (
    <OrgSliceCard
      save={(org) => updateOrganization(orgToUpdate(org, { defaultCurrency: org.defaultCurrency }))}
    >
      {(org, setOrg, disabled) => (
        <>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <h2 className="text-lg font-black text-foreground">Default currency</h2>
              <p className="text-sm text-muted-foreground">
                The currency claims are submitted in. Xero refuses a bill in a currency the
                organisation isn't subscribed to.
              </p>
            </div>
            <button
              type="button"
              onClick={() => void currenciesQuery.refresh()}
              disabled={syncing}
              className="inline-flex shrink-0 items-center gap-2 rounded-2xl border border-border bg-card px-3.5 py-2 text-sm font-semibold text-foreground shadow-sm transition hover:border-primary/40 disabled:opacity-50"
            >
              <RefreshCw className={`h-4 w-4 ${syncing ? "animate-spin" : ""}`} />
              {syncing ? "Syncing…" : "Sync from Xero"}
            </button>
          </div>
          <div className="sm:max-w-sm">
            <label htmlFor="claim-currency" className={LABEL}>
              Currency
            </label>
            {currencies.length > 0 ? (
              <>
                <Select
                  value={org.defaultCurrency}
                  onValueChange={(code) => setOrg({ ...org, defaultCurrency: code })}
                  disabled={disabled}
                >
                  <SelectTrigger id="claim-currency" className="mt-1.5 bg-card">
                    <SelectValue placeholder="Pick a currency" />
                  </SelectTrigger>
                  <SelectContent>
                    {/* A currency saved before Xero was connected (or since removed
                        there) still has to show, or the field would silently read
                        as something the org isn't actually using. */}
                    {currencies.some((c) => c.code === org.defaultCurrency) ||
                    !org.defaultCurrency ? null : (
                      <SelectItem value={org.defaultCurrency}>
                        {org.defaultCurrency} — not in Xero
                      </SelectItem>
                    )}
                    {currencies.map((c) => (
                      <SelectItem key={c.code} value={c.code}>
                        {c.code} — {c.description}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <p className="mt-1 text-xs text-muted-foreground">
                  From your Xero organisation. Added one there? Sync to pull it in.
                </p>
              </>
            ) : (
              <>
                <input
                  id="claim-currency"
                  className={`${INPUT} mt-1.5`}
                  maxLength={3}
                  disabled={disabled}
                  value={org.defaultCurrency}
                  onChange={(e) => setOrg({ ...org, defaultCurrency: e.target.value.toUpperCase() })}
                  placeholder="MYR"
                />
                <p className="mt-1 text-xs text-muted-foreground">
                  Connect Xero to pick from the currencies it holds.
                </p>
              </>
            )}
            {currenciesQuery.error ? (
              <p className="mt-1 text-xs text-destructive">{currenciesQuery.error}</p>
            ) : null}
          </div>
        </>
      )}
    </OrgSliceCard>
  );
}

// Org-wide mileage rate + unit. Per-account overrides (below on the Accounts
// screen) take precedence when set.
export function OrgMileageDefaultsCard() {
  return (
    <OrgSliceCard
      save={(org) =>
        updateOrganization(
          orgToUpdate(org, {
            defaultMileageRate: org.defaultMileageRate,
            mileageUnit: org.mileageUnit,
          }),
        )
      }
    >
      {(org, setOrg, disabled) => (
        <>
          <div>
            <h2 className="text-lg font-black text-foreground">Mileage defaults</h2>
            <p className="text-sm text-muted-foreground">
              The organisation-wide mileage rate and unit. A per-account rate overrides this.
            </p>
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <div>
              <label htmlFor="mileage-rate" className={LABEL}>
                Default rate (per unit)
              </label>
              <input
                id="mileage-rate"
                type="number"
                step="0.01"
                min="0"
                disabled={disabled}
                className={`${INPUT} mt-1.5`}
                value={org.defaultMileageRate}
                onChange={(e) => setOrg({ ...org, defaultMileageRate: Number(e.target.value) })}
              />
            </div>
            <div>
              <label htmlFor="mileage-unit" className={LABEL}>
                Unit
              </label>
              <Select
                value={org.mileageUnit}
                disabled={disabled}
                onValueChange={(next) =>
                  setOrg({ ...org, mileageUnit: next as "KM" | "MILE" })
                }
              >
                <SelectTrigger id="mileage-unit" className="mt-1.5">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="KM">Kilometres</SelectItem>
                  <SelectItem value="MILE">Miles</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
        </>
      )}
    </OrgSliceCard>
  );
}

// How close to a project's pin still counts as on-site. Lives with Projects
// because that's what it governs.
export function OrgGeofenceCard() {
  return (
    <OrgSliceCard
      save={(org) =>
        updateOrganization(orgToUpdate(org, { geofenceRadiusMeters: org.geofenceRadiusMeters }))
      }
    >
      {(org, setOrg, disabled) => (
        <>
          <div>
            <h2 className="text-lg font-black text-foreground">Geofence radius</h2>
            <p className="text-sm text-muted-foreground">
              How close to a project's pin still counts as on-site, for clock-ins. Default 200m.
            </p>
          </div>
          <div className="sm:max-w-xs">
            <label htmlFor="geofence-radius" className={LABEL}>
              Radius (metres)
            </label>
            <input
              id="geofence-radius"
              type="number"
              step="10"
              min="10"
              disabled={disabled}
              className={`${INPUT} mt-1.5`}
              value={org.geofenceRadiusMeters}
              onChange={(e) => setOrg({ ...org, geofenceRadiusMeters: Number(e.target.value) })}
            />
          </div>
        </>
      )}
    </OrgSliceCard>
  );
}
