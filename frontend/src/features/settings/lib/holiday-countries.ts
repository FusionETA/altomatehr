// Countries offered when importing a public-holiday calendar.
//
// ISO 3166-1 alpha-2 codes — what the backend passes to Calendarific (and to
// Nager.Date as its fallback). The region this app is sold into comes first,
// then the countries clients most often have staff in; names come from the
// browser so they are never misspelled here.
//
// Nager.Date does not publish every country (Brunei, Cambodia, Laos and
// Myanmar among them). Those import through Calendarific, so they need the
// server's Calendarific key; without it the import says no holidays came back.

const REGION = ["MY", "SG", "ID", "TH", "PH", "VN", "BN", "KH", "LA", "MM"] as const;

const OTHERS = [
  "AE", "AU", "BD", "CA", "CN", "DE", "FR", "GB", "HK", "IE", "IN", "IT", "JP",
  "KR", "LK", "MO", "NL", "NP", "NZ", "PK", "QA", "SA", "TW", "US",
] as const;

export const DEFAULT_HOLIDAY_COUNTRY = "MY";

export type HolidayCountry = { code: string; name: string };

const names = (() => {
  try {
    return new Intl.DisplayNames(["en"], { type: "region" });
  } catch {
    return null;
  }
})();

export function countryName(code: string): string {
  return names?.of(code) ?? code;
}

const byName = (a: HolidayCountry, b: HolidayCountry) => a.name.localeCompare(b.name);

// Region first (Malaysia leading, the rest alphabetical), then everyone else
// alphabetical — two groups, so the common picks are never a scroll away.
export const HOLIDAY_COUNTRY_GROUPS: { label: string; countries: HolidayCountry[] }[] = [
  {
    label: "Southeast Asia",
    countries: [
      { code: DEFAULT_HOLIDAY_COUNTRY, name: countryName(DEFAULT_HOLIDAY_COUNTRY) },
      ...REGION.filter((c) => c !== DEFAULT_HOLIDAY_COUNTRY)
        .map((code) => ({ code, name: countryName(code) }))
        .sort(byName),
    ],
  },
  {
    label: "Other countries",
    countries: OTHERS.map((code) => ({ code, name: countryName(code) })).sort(byName),
  },
];

const KNOWN = new Set<string>([...REGION, ...OTHERS]);

// The last country this browser imported, so an org outside Malaysia does not
// re-pick it every year. Per-viewer convenience only — storage can be blocked
// or cleared, and the default is always a valid answer.
const STORAGE_KEY = "altomatehr.holidayImportCountry";

export function rememberedHolidayCountry(): string {
  try {
    const saved = window.localStorage.getItem(STORAGE_KEY);
    return saved && KNOWN.has(saved) ? saved : DEFAULT_HOLIDAY_COUNTRY;
  } catch {
    return DEFAULT_HOLIDAY_COUNTRY;
  }
}

export function rememberHolidayCountry(code: string): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, code);
  } catch {
    /* storage unavailable — the default still works */
  }
}
