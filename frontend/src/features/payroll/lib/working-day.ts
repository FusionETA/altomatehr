// OCBC Velocity only takes a payment date on a working day in Kuala Lumpur
// (the server refuses a weekend; see PayrollBankFileOcbc). These spot the
// problem in the download panel before anyone presses Download.

// yyyy-MM-dd, read as a calendar date — no timezone shift.
const dayOfWeek = (iso: string) => {
  const [y, m, d] = iso.split("-").map(Number);
  return new Date(Date.UTC(y, m - 1, d)).getUTCDay();
};

/** Saturday or Sunday — KL's weekend. */
export const isWeekend = (iso: string) => {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(iso)) return false;
  const day = dayOfWeek(iso);
  return day === 0 || day === 6;
};

/** The weekday on or before `iso` (Saturday → Friday, Sunday → Friday). */
export const previousWeekday = (iso: string) => {
  const [y, m, d] = iso.split("-").map(Number);
  const date = new Date(Date.UTC(y, m - 1, d));
  while (date.getUTCDay() === 0 || date.getUTCDay() === 6) date.setUTCDate(date.getUTCDate() - 1);
  return date.toISOString().slice(0, 10);
};

/** "Fri 30 Oct 2026". */
export const formatDay = (iso: string) => {
  const [y, m, d] = iso.split("-").map(Number);
  return new Date(Date.UTC(y, m - 1, d)).toLocaleDateString("en-MY", {
    weekday: "short",
    day: "numeric",
    month: "short",
    year: "numeric",
    timeZone: "UTC",
  });
};
