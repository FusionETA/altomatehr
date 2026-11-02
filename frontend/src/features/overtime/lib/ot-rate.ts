import type { OtDayType, OtRateResolution } from "../api";

export const otDayTypeLabels: Record<OtDayType, string> = {
  NORMAL_DAY: "Normal working day",
  REST_DAY: "Rest day",
  PUBLIC_HOLIDAY: "Public holiday",
};

// Rest days and holidays pay a higher rate, so they read as the notable cases;
// a normal day is the baseline.
export function isPremiumDay(dayType: OtDayType) {
  return dayType !== "NORMAL_DAY";
}

// "1.5x" rather than "1.50x" — trailing zeros read as false precision.
export function formatMultiplier(value: number | null) {
  if (value === null) return null;
  return `${Number(value.toFixed(2))}×`;
}

// One line describing what the employee will be paid.
export function describeRate(rate: OtRateResolution): string | null {
  const multiplier = formatMultiplier(rate.outOfShiftMultiplier);
  return multiplier ? `Overtime hours ${multiplier}` : null;
}
