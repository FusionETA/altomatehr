import { ChevronDown, ChevronUp, Crosshair, LoaderCircle, Plus, X } from "lucide-react";
import { getMyIp } from "../api";
import { isValidIpOrCidr } from "../lib/ip-allowlist";
import { requestGeolocation } from "@/shared/lib/geolocation";
import { useState } from "react";

// The two list editors on a project: where people may clock in from, by
// location and by network. Both were single fields — one lat/lng pair and one
// comma-separated string — which could not express a site with two entrances
// or say what "203.0.113.0/24" actually is.

const INPUT =
  "w-full rounded-xl border border-border/70 bg-card px-3 py-2 text-sm text-foreground shadow-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary";

const ROW = "rounded-2xl border border-border/60 bg-card p-2.5";

const ICON_BUTTON =
  "grid h-8 w-8 shrink-0 place-items-center rounded-full border border-border/60 text-muted-foreground transition hover:bg-muted hover:text-foreground disabled:pointer-events-none disabled:opacity-35";

const ADD_BUTTON =
  "inline-flex items-center gap-1.5 rounded-full border border-border/60 bg-card px-3 py-1.5 text-xs font-semibold text-foreground transition hover:bg-muted disabled:opacity-50";

export type SiteDraft = { label: string; latitude: string; longitude: string };
export type IpDraft = { label: string; cidr: string };

/**
 * A project's geofenced sites.
 *
 * The order is editable because it is behaviour, not presentation: the check
 * walks the sites in order and stops at the FIRST one inside the radius, so
 * moving a site up changes which one an employee is judged against. That is
 * why there are arrows here and none on the allowlist below.
 */
export function GeofenceSitesEditor({
  sites,
  onChange,
}: {
  sites: SiteDraft[];
  onChange: (next: SiteDraft[]) => void;
}) {
  const [locatingIndex, setLocatingIndex] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  function update(index: number, patch: Partial<SiteDraft>) {
    onChange(sites.map((s, i) => (i === index ? { ...s, ...patch } : s)));
  }

  function move(index: number, delta: number) {
    const target = index + delta;
    if (target < 0 || target >= sites.length) return;
    const next = [...sites];
    [next[index], next[target]] = [next[target], next[index]];
    onChange(next);
  }

  async function fillFromDeviceLocation(index: number) {
    setLocatingIndex(index);
    setError(null);
    try {
      const coords = await requestGeolocation();
      update(index, { latitude: coords.lat.toFixed(6), longitude: coords.lng.toFixed(6) });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Couldn't get your location.");
    } finally {
      setLocatingIndex(null);
    }
  }

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between gap-2">
        <label className="text-xs font-semibold text-muted-foreground">Geofenced sites</label>
        <button
          type="button"
          className={ADD_BUTTON}
          onClick={() => onChange([...sites, { label: "", latitude: "", longitude: "" }])}
        >
          <Plus className="h-3 w-3" />
          Add site
        </button>
      </div>

      {sites.length === 0 ? (
        <p className="rounded-2xl border border-dashed border-border/60 px-3 py-4 text-center text-xs text-muted-foreground">
          No geofence. Employees can clock in from anywhere on this project.
        </p>
      ) : (
        sites.map((site, index) => (
          <div key={index} className={ROW}>
            <div className="flex items-center gap-2">
              <input
                className={INPUT}
                value={site.label}
                onChange={(e) => update(index, { label: e.target.value })}
                placeholder="Site name (e.g. Main gate)"
              />
              {/* Only worth showing when the order can actually change. */}
              {sites.length > 1 ? (
                <>
                  <button
                    type="button"
                    className={ICON_BUTTON}
                    onClick={() => move(index, -1)}
                    disabled={index === 0}
                    aria-label={`Move ${site.label || "site"} earlier`}
                  >
                    <ChevronUp className="h-4 w-4" />
                  </button>
                  <button
                    type="button"
                    className={ICON_BUTTON}
                    onClick={() => move(index, 1)}
                    disabled={index === sites.length - 1}
                    aria-label={`Move ${site.label || "site"} later`}
                  >
                    <ChevronDown className="h-4 w-4" />
                  </button>
                </>
              ) : null}
              <button
                type="button"
                className={ICON_BUTTON}
                onClick={() => onChange(sites.filter((_, i) => i !== index))}
                aria-label={`Remove ${site.label || "site"}`}
              >
                <X className="h-4 w-4" />
              </button>
            </div>

            <div className="mt-2 grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
              <input
                className={INPUT}
                type="number"
                step="any"
                value={site.latitude}
                onChange={(e) => update(index, { latitude: e.target.value })}
                placeholder="Latitude (e.g. 3.1578)"
              />
              <input
                className={INPUT}
                type="number"
                step="any"
                value={site.longitude}
                onChange={(e) => update(index, { longitude: e.target.value })}
                placeholder="Longitude (e.g. 101.7123)"
              />
              <button
                type="button"
                className={ADD_BUTTON}
                onClick={() => void fillFromDeviceLocation(index)}
                disabled={locatingIndex !== null}
              >
                {locatingIndex === index ? (
                  <LoaderCircle className="h-3 w-3 animate-spin" />
                ) : (
                  <Crosshair className="h-3 w-3" />
                )}
                Use my location
              </button>
            </div>
          </div>
        ))
      )}

      {error ? <p className="text-xs font-medium text-destructive">{error}</p> : null}
      {sites.length > 1 ? (
        <p className="text-xs text-muted-foreground">
          Checked top to bottom — the first site within the radius counts as on-site. When none
          match, the employee is told how far they are from the nearest one.
        </p>
      ) : null}
    </div>
  );
}

/**
 * A project's IP allowlist.
 *
 * No reordering: matching asks whether ANY entry covers the address, so unlike
 * the sites above the order means nothing.
 */
export function AllowedIpsEditor({
  entries,
  onChange,
}: {
  entries: IpDraft[];
  onChange: (next: IpDraft[]) => void;
}) {
  const [loadingIp, setLoadingIp] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function update(index: number, patch: Partial<IpDraft>) {
    onChange(entries.map((e, i) => (i === index ? { ...e, ...patch } : e)));
  }

  async function addMyIp() {
    setLoadingIp(true);
    setError(null);
    try {
      const { ip } = await getMyIp();
      if (!ip) {
        setError("Couldn't determine your IP address.");
        return;
      }
      if (entries.some((e) => e.cidr.trim() === ip)) return;
      onChange([...entries, { label: "This device", cidr: ip }]);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Couldn't determine your IP address.");
    } finally {
      setLoadingIp(false);
    }
  }

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <label className="text-xs font-semibold text-muted-foreground">IP allowlist</label>
        <div className="flex items-center gap-2">
          <button type="button" className={ADD_BUTTON} onClick={() => void addMyIp()} disabled={loadingIp}>
            {loadingIp ? (
              <LoaderCircle className="h-3 w-3 animate-spin" />
            ) : (
              <Crosshair className="h-3 w-3" />
            )}
            Use my IP
          </button>
          <button
            type="button"
            className={ADD_BUTTON}
            onClick={() => onChange([...entries, { label: "", cidr: "" }])}
          >
            <Plus className="h-3 w-3" />
            Add entry
          </button>
        </div>
      </div>

      {entries.length === 0 ? (
        <p className="rounded-2xl border border-dashed border-border/60 px-3 py-4 text-center text-xs text-muted-foreground">
          No allowlist. The IP check is skipped for this project, even for employees whose policy
          requires it.
        </p>
      ) : (
        entries.map((entry, index) => {
          // Blank is "not filled in yet", not "wrong" — flagging an empty row
          // the moment it appears is noise.
          const invalid = entry.cidr.trim() !== "" && !isValidIpOrCidr(entry.cidr);
          return (
            <div key={index} className={ROW}>
              <div className="grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
                <input
                  className={INPUT}
                  value={entry.label}
                  onChange={(e) => update(index, { label: e.target.value })}
                  placeholder="Name (e.g. KL office wifi)"
                />
                <input
                  className={`${INPUT} ${invalid ? "border-destructive" : ""}`}
                  value={entry.cidr}
                  onChange={(e) => update(index, { cidr: e.target.value })}
                  placeholder="203.0.113.0/24"
                  aria-invalid={invalid}
                />
                <button
                  type="button"
                  className={ICON_BUTTON}
                  onClick={() => onChange(entries.filter((_, i) => i !== index))}
                  aria-label={`Remove ${entry.label || "entry"}`}
                >
                  <X className="h-4 w-4" />
                </button>
              </div>
              {invalid ? (
                <p className="mt-1 text-xs font-medium text-destructive">
                  Not a valid IPv4 address or range.
                </p>
              ) : null}
            </div>
          );
        })
      )}

      {error ? <p className="text-xs font-medium text-destructive">{error}</p> : null}
      <p className="text-xs text-muted-foreground">
        A single address or a range (<code>203.0.113.0/24</code> covers the whole network). Only
        enforced for employees whose policy requires it.
      </p>
    </div>
  );
}
