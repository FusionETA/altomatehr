// Today's date as attendance, claims and overtime file it: the business day in
// Asia/Kuala_Lumpur (the server's AttendanceTime.DefaultTimeZone), as
// "YYYY-MM-DD".
//
// `new Date().toISOString().slice(0, 10)` is the UTC date, which in Malaysia
// is YESTERDAY until 08:00. The admin attendance board asked for the 24th at
// 03:21 on the 25th, so that morning's clock-ins — filed under the 25th — were
// nowhere on it, and new claims and OT defaulted to the previous day.
//
// Fixed to the business zone rather than the browser's, so an admin abroad
// sees the same day the server files under.
export const BUSINESS_TIME_ZONE = "Asia/Kuala_Lumpur";

const FORMAT = new Intl.DateTimeFormat("en-CA", {
  timeZone: BUSINESS_TIME_ZONE,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
});

export function businessToday(now: Date = new Date()): string {
  return FORMAT.format(now); // en-CA formats as YYYY-MM-DD
}
