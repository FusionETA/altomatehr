// IPv4 / CIDR validation, mirroring the server's Common/IpAllowlist so the
// form rejects exactly what the matcher would drop.
//
// The server remains the authority — it re-validates and refuses the save.
// This exists so an admin finds out while typing rather than after a failed
// save, and so a malformed entry never sits in the list looking saved.

function isIpv4(part: string): boolean {
  const octets = part.split(".");
  if (octets.length !== 4) return false;
  return octets.every((o) => {
    if (o.length === 0 || o.length > 3) return false;
    const n = Number(o);
    // Rejects "01" / "001": ambiguous enough that an allowlist should not
    // depend on whether the reader takes them as decimal or octal.
    return Number.isInteger(n) && n >= 0 && n <= 255 && String(n) === o;
  });
}

/** True for a bare IPv4 address (treated as /32) or an IPv4 CIDR range. */
export function isValidIpOrCidr(entry: string): boolean {
  const parts = entry.trim().split("/");
  if (parts.length > 2) return false;
  if (!isIpv4(parts[0])) return false;
  if (parts.length === 1) return true;
  const prefix = Number(parts[1]);
  return Number.isInteger(prefix) && prefix >= 0 && prefix <= 32;
}
