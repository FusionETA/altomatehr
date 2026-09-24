import type { MalaysianBank } from "@/features/payroll/api";

// Which register bank a free-text name means, or null.
//
// The same rule as MalaysianBanks.Find on the server — exact alias first, then
// the longest partial match — so the dropdown shows an existing "Cimb" as the
// bank the bank file will actually use, and flags exactly the names the bank
// file would refuse. Keep the two in step.
export function matchBank(name: string | null | undefined, banks: MalaysianBank[]): MalaysianBank | null {
  const needle = name?.trim().toLowerCase();
  if (!needle) return null;

  const exact = banks.find((b) => b.aliases.includes(needle));
  if (exact) return exact;

  let best: MalaysianBank | null = null;
  let bestLength = 0;
  for (const bank of banks) {
    for (const alias of bank.aliases) {
      if (!needle.includes(alias) && !alias.includes(needle)) continue;
      const length = Math.min(alias.length, needle.length);
      if (length > bestLength) {
        best = bank;
        bestLength = length;
      }
    }
    const canonical = bank.name.toLowerCase();
    if (needle.includes(canonical) && canonical.length > bestLength) {
      best = bank;
      bestLength = canonical.length;
    }
  }
  return best;
}
