import type { SupportOrganization } from "../api";

// "DIY · PAID", "EXPERT" — how a company's package reads in the table.
export function planLabel(org: Pick<SupportOrganization, "plan" | "tier">) {
  return org.tier && org.plan === "DIY" ? `${org.plan} · ${org.tier}` : org.plan;
}

// Case-insensitive match on company name or owner email/name.
export function matchesSearch(org: SupportOrganization, query: string) {
  const q = query.trim().toLowerCase();
  if (!q) return true;
  return [org.name, org.ownerEmail, org.ownerName].some((v) => (v ?? "").toLowerCase().includes(q));
}
