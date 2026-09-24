import { useEffect, useMemo, useState } from "react";
import { CalendarDays, ChevronLeft, ChevronRight, LoaderCircle, Plus, Trash2 } from "lucide-react";
import {
  createHoliday,
  deleteHoliday,
  getHolidays,
  importHolidays,
  getOrganization,
  updateOrganization,
  type Holiday,
  type Organization,
} from "../api";
import { useCachedQuery } from "@/shared/lib/use-cached-query";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectSeparator,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import {
  HOLIDAY_COUNTRY_GROUPS,
  countryName,
  rememberHolidayCountry,
  rememberedHolidayCountry,
} from "../lib/holiday-countries";
import { Skeleton, SkeletonPanel } from "@/shared/components/Skeleton";

const CARD =
  "rounded-[28px] border border-border/70 bg-card/90 p-5 shadow-ambient backdrop-blur-sm sm:p-6";
const INPUT =
  "h-12 w-full rounded-2xl border border-border bg-card px-4 text-sm text-foreground shadow-sm placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50";
const LABEL = "block text-sm font-semibold text-foreground";
const PRIMARY_BUTTON =
  "inline-flex items-center justify-center gap-2 rounded-2xl bg-primary px-5 py-2.5 text-sm font-semibold text-primary-foreground shadow-[0_12px_30px_rgba(76,26,134,0.18)] transition hover:opacity-90 disabled:opacity-50";

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

// "2026-01-01" → "Thu, 1 Jan 2026". Parsed as a local date (no timezone shift),
// since holidays are calendar days, not instants.
function formatHolidayDate(iso: string): string {
  const [y, m, d] = iso.split("-").map(Number);
  if (!y || !m || !d) return iso;
  return new Date(y, m - 1, d).toLocaleDateString(undefined, {
    weekday: "short",
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}

// The org's default working hours, days, and lunch break. Lives here rather than
// on the Organization tab because it pairs with holidays: together they define
// what counts as a working day. A project with its own schedule overrides this.
export function WorkScheduleSettings() {
  return (
    <div className="space-y-5">
      <ScheduleCard />
      <HolidaysCard />
      <CalendarCard />
    </div>
  );
}

function ScheduleCard() {
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
      // A full PUT: carry the rest of the org's settings through untouched so
      // saving the schedule can't reset the name / currency / geofence.
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

  if (orgQuery.loading) return <SkeletonPanel />;
  if (!org) {
    return (
      <div className="rounded-[28px] border border-destructive/20 bg-destructive/5 p-6 text-sm font-medium text-destructive">
        {orgQuery.error ?? "Could not load the organization."}
      </div>
    );
  }

  const selected = parseDays(org.workingDays);

  return (
    <form onSubmit={handleSubmit} className={`${CARD} space-y-5`}>
      <div>
        <h2 className="text-lg font-black text-foreground">Default work schedule</h2>
        <p className="text-sm text-muted-foreground">
          The org-wide default hours, used to work out expected daily working minutes. A project
          with its own schedule overrides this.
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

      <div className="space-y-2">
        <span className={LABEL}>Working days</span>
        <div className="flex flex-wrap gap-1.5">
          {DAYS.map((d) => {
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

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}
      {saved ? <p className="text-sm font-medium text-primary">Saved.</p> : null}

      <button type="submit" disabled={saving} className={PRIMARY_BUTTON}>
        {saving ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
        Save schedule
      </button>
    </form>
  );
}

function HolidaysCard() {
  const holidaysQuery = useCachedQuery("/holidays", getHolidays);
  const [date, setDate] = useState("");
  const [name, setName] = useState("");
  const [adding, setAdding] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);

  // Importing a year at a time, because that is how the calendars are
  // published. Defaults to next year once October is past: the reason anyone
  // opens this card in Q4 is to load the year ahead.
  const [importYear, setImportYear] = useState(() => {
    const now = new Date();
    return now.getMonth() >= 9 ? now.getFullYear() + 1 : now.getFullYear();
  });
  // Malaysia unless this browser last imported somewhere else.
  const [importCountry, setImportCountry] = useState(rememberedHolidayCountry);
  const [importing, setImporting] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  // Only org-wide holidays live here; project-specific ones are managed per
  // project. Sorted so the calendar reads top-to-bottom.
  const holidays = useMemo(
    () =>
      (holidaysQuery.data ?? [])
        .filter((h) => h.projectId === null)
        .sort((a, b) => a.date.localeCompare(b.date)),
    [holidaysQuery.data],
  );

  async function add(e: React.FormEvent) {
    e.preventDefault();
    if (!date || !name.trim()) return;
    setAdding(true);
    setError(null);
    try {
      await createHoliday({ date, name: name.trim() });
      setDate("");
      setName("");
      await holidaysQuery.refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not add the holiday.");
    } finally {
      setAdding(false);
    }
  }

  async function runImport() {
    setImporting(true);
    setError(null);
    setNotice(null);
    try {
      const result = await importHolidays(importYear, importCountry);
      rememberHolidayCountry(importCountry);
      await holidaysQuery.refresh();
      // Both halves reported: "imported 0, already had 16" is a different
      // outcome from "imported 16", and an admin re-running after a revision
      // needs to tell them apart.
      setNotice(
        [
          `Added ${result.imported} ${result.imported === 1 ? "holiday" : "holidays"} for ${countryName(importCountry)} ${importYear}`,
          result.skipped > 0 ? `${result.skipped} already on the calendar` : null,
          result.source ? `via ${result.source}` : null,
        ]
          .filter(Boolean)
          .join(" · "),
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not import the holidays.");
    } finally {
      setImporting(false);
    }
  }

  async function remove(h: Holiday) {
    setDeletingId(h.id);
    setError(null);
    try {
      await deleteHoliday(h.id);
      await holidaysQuery.refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not remove the holiday.");
    } finally {
      setDeletingId(null);
    }
  }

  return (
    <section className={`${CARD} space-y-5`}>
      <div>
        <h2 className="text-lg font-black text-foreground">Public holidays</h2>
        <p className="text-sm text-muted-foreground">
          Company-wide days off. They don't count as working days for leave or attendance.
        </p>
      </div>

      {/* Import first, then correct by hand. Typing a year of public holidays
          one at a time is how a calendar ends up half-entered — and a missing
          holiday is silently charged to someone's leave balance. */}
      <div className="flex flex-wrap items-end gap-3 rounded-2xl border border-border/60 bg-surface-low p-4">
        <div className="space-y-1.5">
          <label htmlFor="holiday-import-country" className="text-xs text-muted-foreground">
            Country
          </label>
          <Select value={importCountry} onValueChange={setImportCountry}>
            <SelectTrigger id="holiday-import-country" className="w-56">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {HOLIDAY_COUNTRY_GROUPS.map((group, i) => (
                <SelectGroup key={group.label}>
                  {i > 0 ? <SelectSeparator /> : null}
                  <SelectLabel>{group.label}</SelectLabel>
                  {group.countries.map((c) => (
                    <SelectItem key={c.code} value={c.code}>
                      {c.name}
                    </SelectItem>
                  ))}
                </SelectGroup>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1.5">
          <label htmlFor="holiday-import-year" className="text-xs text-muted-foreground">
            Year
          </label>
          <input
            id="holiday-import-year"
            type="number"
            min={2000}
            max={2100}
            className={`${INPUT} w-32`}
            value={importYear}
            onChange={(e) => setImportYear(Number(e.target.value))}
          />
        </div>
        <button
          type="button"
          disabled={importing}
          onClick={() => void runImport()}
          className="inline-flex h-12 items-center gap-2 rounded-2xl border border-border bg-card px-5 text-sm font-semibold text-foreground transition hover:border-primary/40 disabled:opacity-50"
        >
          {importing ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
          Import
        </button>
        <p className="w-full text-xs text-muted-foreground">
          Existing dates are left as they are, so re-running after a revision won't undo a
          name you edited.
        </p>
      </div>

      {notice ? (
        <p className="text-sm font-medium text-foreground">{notice}</p>
      ) : null}

      <form onSubmit={add} className="grid gap-3 sm:grid-cols-[auto_1fr_auto] sm:items-end">
        <div className="space-y-1.5">
          <label htmlFor="holiday-date" className="text-xs text-muted-foreground">
            Date
          </label>
          <input
            id="holiday-date"
            type="date"
            className={INPUT}
            value={date}
            onChange={(e) => setDate(e.target.value)}
          />
        </div>
        <div className="space-y-1.5">
          <label htmlFor="holiday-name" className="text-xs text-muted-foreground">
            Name
          </label>
          <input
            id="holiday-name"
            className={INPUT}
            value={name}
            maxLength={160}
            placeholder="New Year's Day"
            onChange={(e) => setName(e.target.value)}
          />
        </div>
        <button type="submit" disabled={adding || !date || !name.trim()} className={PRIMARY_BUTTON}>
          {adding ? <LoaderCircle className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
          Add
        </button>
      </form>

      {error ? <p className="text-sm font-medium text-destructive">{error}</p> : null}

      {holidaysQuery.loading ? (
        <div className="space-y-2">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-14 w-full rounded-2xl" />
          ))}
        </div>
      ) : holidays.length === 0 ? (
        <div className="flex items-center gap-3 rounded-2xl border border-dashed border-border/70 px-4 py-6 text-sm text-muted-foreground">
          <CalendarDays className="h-5 w-5 shrink-0" />
          No holidays yet. Add the ones your company observes.
        </div>
      ) : (
        <ul className="divide-y divide-border/60 overflow-hidden rounded-2xl border border-border/60">
          {holidays.map((h) => (
            <li key={h.id} className="flex items-center justify-between gap-3 px-4 py-3">
              <div className="min-w-0">
                <p className="truncate font-semibold text-foreground">{h.name}</p>
                <p className="text-xs text-muted-foreground">{formatHolidayDate(h.date)}</p>
              </div>
              <button
                type="button"
                onClick={() => void remove(h)}
                disabled={deletingId === h.id}
                aria-label={`Remove ${h.name}`}
                className="shrink-0 rounded-full p-2 text-muted-foreground transition hover:bg-destructive/10 hover:text-destructive disabled:opacity-50"
              >
                {deletingId === h.id ? (
                  <LoaderCircle className="h-4 w-4 animate-spin" />
                ) : (
                  <Trash2 className="h-4 w-4" />
                )}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

const WEEKDAY_INITIALS = ["M", "T", "W", "T", "F", "S", "S"] as const;

// A month grid that colours working days, rest days, holidays and today — so the
// schedule and holidays read at a glance, the way the previous system showed it.
function CalendarCard() {
  const orgQuery = useCachedQuery("/organizations/current", getOrganization);
  const holidaysQuery = useCachedQuery("/holidays", getHolidays);

  const today = new Date();
  const [viewYear, setViewYear] = useState(today.getFullYear());
  const [viewMonth, setViewMonth] = useState(today.getMonth());

  const workingDays = parseDays(orgQuery.data?.workingDays ?? null);
  const holidayMap = new Map(
    (holidaysQuery.data ?? [])
      .filter((h) => h.projectId === null)
      .map((h) => [h.date, h.name] as const),
  );

  const firstOfMonth = new Date(viewYear, viewMonth, 1);
  const daysInMonth = new Date(viewYear, viewMonth + 1, 0).getDate();
  // getDay() is 0=Sun…6=Sat; convert to ISO 1=Mon…7=Sun so the grid starts Monday.
  const firstWeekdayIso = ((firstOfMonth.getDay() + 6) % 7) + 1;
  const leadingBlanks = firstWeekdayIso - 1;

  const cells: Array<{ day: number; iso: string; weekday: number } | null> = [];
  for (let i = 0; i < leadingBlanks; i++) cells.push(null);
  for (let d = 1; d <= daysInMonth; d++) {
    const date = new Date(viewYear, viewMonth, d);
    const iso = `${viewYear}-${String(viewMonth + 1).padStart(2, "0")}-${String(d).padStart(2, "0")}`;
    const weekday = ((date.getDay() + 6) % 7) + 1;
    cells.push({ day: d, iso, weekday });
  }
  while (cells.length % 7 !== 0) cells.push(null);

  function shift(delta: number) {
    const next = new Date(viewYear, viewMonth + delta, 1);
    setViewYear(next.getFullYear());
    setViewMonth(next.getMonth());
  }

  const monthLabel = firstOfMonth.toLocaleDateString(undefined, {
    month: "long",
    year: "numeric",
  });
  const todayIso = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, "0")}-${String(today.getDate()).padStart(2, "0")}`;

  return (
    <section className={`${CARD} space-y-4`}>
      <div className="flex items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-black text-foreground">Calendar</h2>
          <p className="text-sm text-muted-foreground">Working days and holidays at a glance.</p>
        </div>
        <div className="flex items-center gap-2">
          <span className="text-sm font-semibold tabular-nums text-foreground">{monthLabel}</span>
          <button
            type="button"
            onClick={() => shift(-1)}
            aria-label="Previous month"
            className="rounded-full p-1.5 text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <button
            type="button"
            onClick={() => shift(1)}
            aria-label="Next month"
            className="rounded-full p-1.5 text-muted-foreground transition hover:bg-muted hover:text-foreground"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>
      </div>

      <div className="mb-1 grid grid-cols-7 text-center text-[10px] font-semibold uppercase tracking-wide text-muted-foreground/70">
        {WEEKDAY_INITIALS.map((d, i) => (
          <div key={i} className="py-1">
            {d}
          </div>
        ))}
      </div>
      <div className="grid grid-cols-7 gap-px overflow-hidden rounded-2xl bg-border/40">
        {cells.map((cell, idx) => {
          if (!cell) return <div key={`blank-${idx}`} className="min-h-[58px] bg-card" />;
          const isWorking = workingDays.has(cell.weekday);
          const holidayName = holidayMap.get(cell.iso);
          const isHoliday = !!holidayName;
          const isToday = cell.iso === todayIso;
          return (
            <div
              key={cell.iso}
              title={holidayName ?? undefined}
              className={`min-h-[58px] p-1.5 transition-colors ${
                isHoliday ? "bg-primary/5" : isWorking ? "bg-card" : "bg-muted/50"
              }`}
            >
              <span
                className={`inline-flex h-5 w-5 items-center justify-center rounded-full text-[11px] tabular-nums ${
                  isToday
                    ? "bg-primary font-bold text-primary-foreground"
                    : isHoliday
                      ? "font-semibold text-primary"
                      : isWorking
                        ? "text-foreground"
                        : "text-muted-foreground/60"
                }`}
              >
                {cell.day}
              </span>
              {isHoliday ? (
                <p className="mt-0.5 line-clamp-2 text-[10px] font-medium leading-tight text-primary/80">
                  {holidayName}
                </p>
              ) : null}
            </div>
          );
        })}
      </div>

      <div className="flex flex-wrap items-center gap-x-4 gap-y-1.5 text-[11px] text-muted-foreground">
        <span className="flex items-center gap-1.5">
          <span className="h-2.5 w-2.5 rounded-sm border border-border/60 bg-card" />
          Working day
        </span>
        <span className="flex items-center gap-1.5">
          <span className="h-2.5 w-2.5 rounded-sm border border-border/60 bg-muted/50" />
          Rest / off
        </span>
        <span className="flex items-center gap-1.5">
          <span className="h-2.5 w-2.5 rounded-sm border border-primary/20 bg-primary/10" />
          Public holiday
        </span>
        <span className="flex items-center gap-1.5">
          <span className="h-2.5 w-2.5 rounded-full bg-primary" />
          Today
        </span>
      </div>
    </section>
  );
}
