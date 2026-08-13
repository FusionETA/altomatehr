export function formatShortDate(value?: string) {
  if (!value) return "Not set";
  return new Intl.DateTimeFormat("en-MY", {
    day: "2-digit",
    month: "short",
    year: "numeric",
  }).format(new Date(value));
}

export function formatCurrency(amount: number, currency = "MYR") {
  return new Intl.NumberFormat("en-MY", {
    style: "currency",
    currency,
  }).format(amount);
}

export function formatMonthYear(value?: string) {
  if (!value) return "Not set";
  return new Intl.DateTimeFormat("en-MY", {
    month: "short",
    year: "numeric",
  }).format(new Date(value));
}
