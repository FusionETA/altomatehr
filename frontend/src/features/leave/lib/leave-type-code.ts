// A short code for a leave type the admin is adding from an import, taken from
// its name's initials ("Hospitalisation Leave" → "HL"), and numbered when that
// code is already used ("HL2"). Only a suggestion: the admin can change it.
export function suggestLeaveTypeCode(name: string, taken: Iterable<string>): string {
  const used = new Set([...taken].map((c) => c.trim().toUpperCase()));
  const initials = name
    .split(/[^A-Za-z0-9]+/)
    .filter(Boolean)
    .map((word) => word[0].toUpperCase())
    .join("")
    .slice(0, 6) || "LEAVE";
  if (!used.has(initials)) return initials;
  for (let n = 2; ; n++) {
    const candidate = `${initials}${n}`;
    if (!used.has(candidate)) return candidate;
  }
}
