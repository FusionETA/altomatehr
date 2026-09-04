import type { OtDayType, OtRateResolution } from "../api";

export const otDayTypeLabels: Record<OtDayType, string> = {
  NORMAL_DAY: "Normal working day",
  REST_DAY: "Rest day",
  PUBLIC_HOLIDAY: "Public holiday",
};

// Rest days and holidays pay a premium even for hours inside the normal shift
// length, so they read as the notable cases; a normal day is the baseline.
export function isPremiumDay(dayType: OtDayType) {
  return dayType !== "NORMAL_DAY";
}

// "1.5x" rather than "1.50x" — trailing zeros read as false precision.
export function formatMultiplier(value: number | null) {
  if (value === null) return null;
  return `${Number(value.toFixed(2))}×`;
}

// One line describing what the employee will be paid. In-shift only matters on a
// premium day: on a normal day the hours inside the shift are ordinary pay, so
// there is no second rate to mention.
export function describeRate(rate: OtRateResolution): string | null {
  const beyond = formatMultiplier(rate.outOfShiftMultiplier);
  if (!beyond) return null;

  const inShift = formatMultiplier(rate.inShiftMultiplier);
  return inShift && isPremiumDay(rate.dayType)
    ? `Within shift hours ${inShift}, beyond ${beyond}`
    : `Overtime hours ${beyond}`;
}
