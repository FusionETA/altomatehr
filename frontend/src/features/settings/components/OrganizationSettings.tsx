import { useEffect, useState } from "react";
import { LoaderCircle } from "lucide-react";
import {
  getOrganization,
  getXeroCurrencies,
  updateOrganization,
  type Organization,
  type XeroCurrency,
} from "../api";
import { XeroConnectionCard } from "./XeroConnectionCard";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import { SkeletonPanel } from "@/shared/components/Skeleton";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50";
const LABEL = "block text-sm font-semibold text-foreground";

// "expense_claim" → "Expense Claim", "attendance" → "Attendance"
const titleCase = (s: string) =>
  s.split("_").map((w) => w.charAt(0).toUpperCase() + w.slice(1)).join(" ");

// ISO weekday numbers, 1 = Monday … 7 = Sunday — the stored CSV format.
const DAYS = [
  { n: 1, label: "Mon" },
  { n: 2, label: "Tue" },
  { n: 3, label: "Wed" },
  { n: 4, label: "Thu" },
  { n: 5, label: "Fri" },
  { n: 6, label: "Sat" },
  { n: 7, label: "Sun" },
] as const;

// A null/blank workingDays means the org default of Mon–Fri, so that is what
// the toggles show until the admin picks their own set.
function parseDays(csv: string | null): Set<number> {
  if (!csv || csv.trim() === "") return new Set([1, 2, 3, 4, 5]);
  const out = new Set<number>();
  for (const p of csv.split(",")) {
    const n = Number(p.trim());
    if (n >= 1 && n <= 7) out.add(n);
  }
  return out;
}

export function OrganizationSettings() {
  const [org, setOrg] = useState<Organization | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [currencies, setCurrencies] = useState<XeroCurrency[]>([]);

  const orgQuery = useCachedQuery("/organizations/current", getOrganization);
  // Best-effort: a Xero outage must not stop the settings form loading, it
  // just falls back to the free-text field — so its error is never surfaced
  // and it never gates `loading`.
  const currenciesQuery = useCachedQuery("/xero/currencies", getXeroCurrencies);
  const loading = orgQuery.loading;

  useEffect(() => {
    if (orgQuery.data) setOrg(orgQuery.data);
  }, [orgQuery.data]);
  useEffect(() => {
    setCurrencies(currenciesQuery.data ?? []);
  }, [currenciesQuery.data]);
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
      const updated = await updateOrganization({
        name: org.name,
        defaultCurrency: org.defaultCurrency,
        defaultMileageRate: org.defaultMileageRate,
        mileageUnit: org.mileageUnit,
        geofenceRadiusMeters: org.geofenceRadiusMeters,
        workingHoursStart: org.workingHoursStart ?? "09:00",
        workingHoursEnd: org.workingHoursEnd ?? "18:00",
        workingDays: org.workingDays,
        lunchBreakMinutes: org.lunchBreakMinutes,
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

      <section className={`${CARD} space-y-4`}>
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2 className="text-lg font-black text-foreground">Subscription</h2>
            <p className="text-sm text-muted-foreground">The package this company is on.</p>
          </div>
          <span className="inline-flex shrink-0 items-center rounded-full bg-primary/10 px-3 py-1 text-xs font-bold uppercase tracking-wide text-primary">
            {org.plan}
            {org.tier ? ` · ${org.tier}` : ""}
          </span>
        </div>

        <div className="space-y-2">
          <p className="text-sm font-semibold text-foreground">Enabled modules</p>
          <div className="flex flex-wrap gap-2">
            {org.enabledModules.map((m) => (
              <span
                key={m}
                className="inline-flex items-center rounded-full border border-border/70 bg-muted px-3 py-1 text-xs font-semibold text-foreground"
              >
                {titleCase(m)}
              </span>
            ))}
          </div>
        </div>

        {org.addons.length > 0 ? (
          <p className="text-xs text-muted-foreground">
            Add-ons: {org.addons.map(titleCase).join(", ")}
          </p>
        ) : null}

        <p className="text-xs text-muted-foreground">
          Packages are provisioned by AltomateHR — contact support to change your plan.
        </p>
      </section>

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
          {/* A picker once Xero is connected, free text otherwise. Every claim
              is denominated in this, and Xero refuses a bill in a currency the
              organisation is not subscribed to — so typing one it does not hold
              breaks EVERY subsequent claim, and only says so weeks later when a
              bill is pushed. The server enforces the same rule. */}
          {currencies.length > 0 ? (
            <>
              <select
                id="org-currency"
                className={INPUT}
                value={org.defaultCurrency}
                onChange={(e) => setOrg({ ...org, defaultCurrency: e.target.value })}
              >
                {/* A currency already saved but since removed in Xero would
                    otherwise vanish from the box and look like a blank field. */}
                {currencies.some((c) => c.code === org.defaultCurrency) ? null : (
                  <option value={org.defaultCurrency}>
                    {org.defaultCurrency} — not in Xero
                  </option>
                )}
                {currencies.map((currency) => (
                  <option key={currency.code} value={currency.code}>
                    {currency.code} — {currency.description}
                  </option>
                ))}
              </select>
              <p className="text-xs text-muted-foreground">
                From your Xero organisation. Add one there to see it here.
              </p>
            </>
          ) : (
            <>
              <input
                id="org-currency"
                className={INPUT}
                maxLength={3}
                value={org.defaultCurrency}
                onChange={(e) => setOrg({ ...org, defaultCurrency: e.target.value.toUpperCase() })}
                placeholder="MYR"
              />
              <p className="text-xs text-muted-foreground">
                Connect Xero to pick from the currencies it holds.
              </p>
            </>
          )}
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

        <div className="space-y-3 sm:col-span-2">
          <div>
            <label className={LABEL}>Default work schedule</label>
            <p className="text-xs text-muted-foreground">
              The org-wide default hours, used to work out expected daily working minutes. A
              project with its own schedule overrides this.
            </p>
          </div>
          <div className="grid gap-3 sm:grid-cols-3">
            <div>
              <span className="text-xs text-muted-foreground">Start</span>
              <input
                type="time"
                className={`${INPUT} mt-1`}
                value={org.workingHoursStart ?? ""}
                onChange={(e) => setOrg({ ...org, workingHoursStart: e.target.value })}
              />
            </div>
            <div>
              <span className="text-xs text-muted-foreground">End</span>
              <input
                type="time"
                className={`${INPUT} mt-1`}
                value={org.workingHoursEnd ?? ""}
                onChange={(e) => setOrg({ ...org, workingHoursEnd: e.target.value })}
              />
            </div>
            <div>
              <span className="text-xs text-muted-foreground">Lunch (min)</span>
              <input
                type="number"
                min="0"
                max="480"
                className={`${INPUT} mt-1`}
                value={org.lunchBreakMinutes}
                onChange={(e) => setOrg({ ...org, lunchBreakMinutes: Number(e.target.value) })}
              />
            </div>
          </div>
          <div className="flex flex-wrap gap-1.5">
            {DAYS.map((d) => {
              const selected = parseDays(org.workingDays);
              const on = selected.has(d.n);
              return (
                <button
                  key={d.n}
                  type="button"
                  onClick={() => {
                    const next = parseDays(org.workingDays);
                    if (next.has(d.n)) next.delete(d.n);
                    else next.add(d.n);
                    setOrg({ ...org, workingDays: [...next].sort((a, b) => a - b).join(",") });
                  }}
                  className={`rounded-full border px-3 py-1 text-xs font-semibold transition ${
                    on
                      ? "border-primary bg-primary text-primary-foreground"
                      : "border-border/60 bg-card text-muted-foreground hover:text-foreground"
                  }`}
                >
                  {d.label}
                </button>
              );
            })}
          </div>
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
    </div>
  );
}
