import { useEffect, useState } from "react";
import { LoaderCircle } from "lucide-react";
import {
  getOrganization,
  getXeroCurrencies,
  orgToUpdate,
  updateOrganization,
  type Organization,
  type XeroCurrency,
} from "../api";
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
  const [org, setOrg] = useState<Organization | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    if (orgQuery.data) setOrg(orgQuery.data);
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

  return (
    <OrgSliceCard
      save={(org) => updateOrganization(orgToUpdate(org, { defaultCurrency: org.defaultCurrency }))}
    >
      {(org, setOrg, disabled) => (
        <>
          <div>
            <h2 className="text-lg font-black text-foreground">Default currency</h2>
            <p className="text-sm text-muted-foreground">
              The currency claims are submitted in. Xero refuses a bill in a currency the
              organisation isn't subscribed to.
            </p>
          </div>
          <div className="sm:max-w-sm">
            <label htmlFor="claim-currency" className={LABEL}>
              Currency
            </label>
            {currencies.length > 0 ? (
              <>
                <select
                  id="claim-currency"
                  className={`${INPUT} mt-1.5`}
                  disabled={disabled}
                  value={org.defaultCurrency}
                  onChange={(e) => setOrg({ ...org, defaultCurrency: e.target.value })}
                >
                  {currencies.some((c) => c.code === org.defaultCurrency) ? null : (
                    <option value={org.defaultCurrency}>{org.defaultCurrency} — not in Xero</option>
                  )}
                  {currencies.map((c) => (
                    <option key={c.code} value={c.code}>
                      {c.code} — {c.description}
                    </option>
                  ))}
                </select>
                <p className="mt-1 text-xs text-muted-foreground">
                  From your Xero organisation. Add one there to see it here.
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
              <select
                id="mileage-unit"
                disabled={disabled}
                className={`${INPUT} mt-1.5`}
                value={org.mileageUnit}
                onChange={(e) => setOrg({ ...org, mileageUnit: e.target.value as "KM" | "MILE" })}
              >
                <option value="KM">Kilometres</option>
                <option value="MILE">Miles</option>
              </select>
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
