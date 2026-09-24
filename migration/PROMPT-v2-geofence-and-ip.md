# Prompt: v2 parity — multi-point geofences + multi-entry IP allowlist

Paste everything below the line into Claude Code in a **local checkout of the v2 repo**.

It is self-contained. It carries the v1 ("legacy AltomateHR", Next.js + Prisma) source
inline, because the local checkout has no access to the v1 repo or to the production
databases. Every row count was measured on the droplet on **2026-09-18** against
`hr_prod`, scoped to the 42 migrated orgs.

Supersedes Task 1 of `PROMPT-v2-schema-gaps.md`. The three questions in that file were
answered server-side — see `ANSWERS-v1-v2-gaps.md`. Do not work from the old file.

---

You are working on AltomateHR v2 (ASP.NET 8 + EF Core + MySQL, React/Vite frontend).
Backend: `backend/`, modules under `backend/Modules/<Area>/`, entities in
`backend/Modules/<Area>/Entities/`, DbSets in `backend/Data/AppDbContext.cs`, EF
migrations in `backend/Migrations/`. Tenant-scoped entities implement
`AltomateHR.Api.Common.ITenantScoped` (a single `string OrganizationId`), which the
DbContext auto-stamps on insert and auto-filters on read.

There are **two build tasks**. Both are cases where v1 supports a *list* and v2 supports
only a *single value*. In both cases v1's implementation is the reference — it is
production-proven, it is quoted in full below, and you should port its behaviour rather
than invent your own.

---

# TASK 1 — multi-point project geofences

**v2 supports exactly ONE geofence point per project. v1 supports many. That is the bug.**

## The data this is about

| | |
|---|---|
| geofence points in `hr_prod.ProjectGeoLocation` (42-org scope) | **250** |
| projects carrying at least one point | **138** |
| projects carrying **more than one** | **89** (73×2, 11×3, 3×4, 2×5) |
| points whose project already exists in v2 `altomatehr.Projects` | 250 (all) |
| projects with points but no primary `latitude`/`longitude` | **0** |

So v2's current model silently drops ~112 real geofence points. A site genuinely has
several valid clock-in locations — gate, site office, warehouse — and an employee
standing at any one of them has to be able to clock in.

That last row matters for your fallback design: every project that has points *also*
has a primary lat/lng, so "fall back to the primary point" is always a live path, never
a null hole.

## v1's schema (the source of truth to port)

```prisma
model ProjectGeoLocation {
  id         String      @id @default(cuid())
  projectId  String
  label      String
  latitude   Float
  longitude  Float
  createdAt  DateTime    @default(now())
  updatedAt  DateTime    @updatedAt
  project    XeroProject @relation(fields: [projectId], references: [id], onDelete: Cascade)

  @@index([projectId])
}
```

`XeroProject.latitude` / `.longitude` still exist in v1 alongside this table. v1 did NOT
delete them — they are the fallback for projects that were never given labelled points.

## v1's matcher — `lib/geo.ts`, port this behaviour exactly

```ts
export const DEFAULT_GEOFENCE_RADIUS_METERS = 200

export function haversineMeters(lat1, lng1, lat2, lng2): number {
  const R = 6371000
  const toRad = (d: number) => (d * Math.PI) / 180
  const dLat = toRad(lat2 - lat1)
  const dLng = toRad(lng2 - lng1)
  const a = Math.sin(dLat / 2) ** 2 +
    Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLng / 2) ** 2
  return 2 * R * Math.asin(Math.sqrt(a))
}

/// Result of a multi-site geofence check. `ok: true` means one of the
/// `locations` was within `radiusMeters`; `matchedIndex` / `matchedLabel`
/// identify which. `ok: false` returns the `nearest` site (if any) so the
/// UI can auto-populate an off-site remark like "500m from HQ".
export type GeofenceMultiResult =
  | { ok: true; matchedIndex: number; matchedLabel: string; distanceMeters: number }
  | { ok: false; nearest: { label: string; distanceMeters: number } | null }

/// Walk `locations` in the given order — the first site within
/// `radiusMeters` wins. Deterministic tie-breaking on order matches the
/// admin's expressed preference in the UI. If none match, returns the
/// nearest site's label + distance so callers can surface it.
///
/// Empty `locations` → `{ ok: false, nearest: null }`. The caller is
/// expected to then fall back to the legacy single-lat/lng
/// `checkGeofence` path for projects that haven't been backfilled.
export function checkGeofenceMulti(
  coords: { latitude: number; longitude: number } | null,
  locations: Array<{ label: string; latitude: number; longitude: number }>,
  radiusMeters: number,
): GeofenceMultiResult {
  if (!coords) return { ok: false, nearest: null }
  if (locations.length === 0) return { ok: false, nearest: null }

  let nearest: { label: string; distanceMeters: number } | null = null
  for (let i = 0; i < locations.length; i += 1) {
    const loc = locations[i]!
    const distance = haversineMeters(coords.latitude, coords.longitude, loc.latitude, loc.longitude)
    if (distance <= radiusMeters) {
      return { ok: true, matchedIndex: i, matchedLabel: loc.label, distanceMeters: distance }
    }
    if (nearest === null || distance < nearest.distanceMeters) {
      nearest = { label: loc.label, distanceMeters: distance }
    }
  }
  return { ok: false, nearest }
}
```

And v1's caller — the multi/single fallback, from
`modules/attendance/application/services/employee-attendance.service.ts`:

```ts
/// When the project has `geoLocations` we walk them in order via
/// `checkGeofenceMulti` (first site within radius wins). Otherwise we
/// fall back to the legacy single lat/lng.
function resolveFenceVerdict(coords, project, radiusMeters) {
  if (project.geoLocations.length > 0) {
    const result = checkGeofenceMulti(coords, project.geoLocations, radiusMeters)
    if (result.ok) return { withinRadius: true, distanceMeters: result.distanceMeters }
    return { withinRadius: false, distanceMeters: result.nearest?.distanceMeters ?? null }
  }
  const fence = checkGeofence(coords, project, radiusMeters)     // single-point path
  return { withinRadius: fence.withinRadius, distanceMeters: fence.distanceMeters }
}
```

Two details worth keeping, both deliberate in v1:

- **Order matters, and it is insertion order.** v1 reads the rows with
  `orderBy: { createdAt: "asc" }` and the first point inside the radius wins. Nearest-point
  wins would be defensible too, but the admin UI promises order, so keep order.
- **On failure, report the NEAREST point's distance**, not the primary point's. That
  number is what the employee sees in the off-site prompt, and quoting a distance to a
  site they weren't heading for is what makes the current single-point behaviour feel broken.

## v1's write path — replace-all inside one transaction

```ts
// Multi-geolocation replace: delete-all-then-insert is simplest and atomic
// inside this transaction. Client-side ids (which may be temp uuids for
// freshly-added rows) are discarded — the DB assigns real ones. Preserving
// row order matters because the detection walk in `checkGeofenceMulti` uses
// the DB's `orderBy: { createdAt: "asc" }`.
if (data.geoLocations !== undefined) {
  await tx.projectGeoLocation.deleteMany({ where: { projectId: data.projectId } })
  for (const loc of data.geoLocations ?? []) {
    await tx.projectGeoLocation.create({
      data: { projectId: data.projectId, label: loc.label, latitude: loc.latitude, longitude: loc.longitude },
    })
  }
}
```

v1's validation, for parity (zod, in the admin server action):

```ts
const geoLocationSchema = z.object({
  label: z.string().trim().min(1, "Every geolocation needs a label.")
          .max(60, "Label must be 60 characters or fewer."),
  latitude: z.number().min(-90).max(90),
  longitude: z.number().min(-180).max(180),
})
```

v1's UI hint text, verbatim — reuse it, it sets the right expectation:

> Detection walks these in order at clock-in — first one within the org's geofence radius
> wins. Employees outside all locations get the off-site remark prompt.

There is no hard cap on the number of points in v1; 5 is just the busiest project in
production. Don't invent a limit.

## What v2 has today (verified — don't re-litigate these)

- `backend/Modules/Attendance/Geo.cs` — `HaversineMeters` (already a faithful port) and
  `DistanceToProject(empLat, empLng, projLat, projLng)`, single centre only.
- `backend/Modules/Attendance/AttendanceService.cs:1178` — `EvaluateGeofenceAsync(...)`
  returning `(bool Geofenced, double? Distance, bool OffSite)`. Called from clock-in
  (line 377) and clock-out (line 476); off-site demands a remark **and** a photo via
  `OffSiteProofMissing` → `OffSiteRequired(distance)`.
- Radius comes from `Organization.GeofenceRadiusMeters`, defaulting to
  `Geo.DefaultRadiusMeters` (200) — same default as v1.
- `backend/Modules/Projects/Entities/Project.cs` — nullable `Latitude` / `Longitude`.

So **radius checking does exist** in v2. You are extending it, not inventing it.

## Do this

1. Add `ProjectGeoLocation` in `backend/Modules/Projects/Entities/`, implementing
   `ITenantScoped`: `Id`, `OrganizationId`, `ProjectId`, `Label` (max 60), `Latitude`
   (double), `Longitude` (double), `CreatedAt`. Index `(OrganizationId, ProjectId)`.
2. Register the DbSet in `AppDbContext`.
3. **Keep `Project.Latitude` / `Longitude`.** Do not remove or deprecate them. Comment
   them as the project's primary point; the table is the full set. v1 kept both for the
   same reason and every project with points still has a primary.
4. Port `checkGeofenceMulti` into `Geo.cs` as a pure static (e.g. `GeofenceMulti(...)`)
   returning matched label + distance, or the nearest label + distance on failure. Keep
   it dependency-free like the rest of `Geo.cs`.
5. Rewire `EvaluateGeofenceAsync` to load the project's points ordered by `CreatedAt`
   and pass if the employee is inside the radius of **any** of them; fall back to the
   primary `Latitude`/`Longitude` when the project has no rows. On failure, surface the
   nearest point's distance.
   Watch the N+1: `EvaluateGeofenceAsync` runs per clock event, and there are batched
   paths nearby (`ResolveProjectIdsAsync`) that already load projects in one query —
   load points the same way rather than one query per event.
6. Surface the points in the project settings UI: list / add / remove rows with a label
   and lat/lng each, replace-all on save, same validation bounds as v1's zod schema
   above. Match the existing settings screens' conventions and reuse v1's hint text.
7. `dotnet ef migrations add ProjectGeoLocations`. Do not hand-write SQL, do not edit
   `AppDbContextModelSnapshot.cs`.
8. Tests: a project with 3 points where the employee is at the 3rd → within radius;
   employee near none → off-site with the *nearest* distance; project with 0 points but
   a primary → falls back and still works; project with neither → whatever v2 does today,
   unchanged.

Do **not** write the data migration for the 250 rows. That runs server-side against
`hr_prod` and is not your job here. Make the schema and the app ready to receive it.

---

# TASK 2 — multi-entry IP allowlist (labels + CIDR ranges)

**v2 stores one comma-separated string and compares IPs by exact string equality.
v1 stores a labelled list and matches CIDR ranges. Port v1's model and matcher.**

## Read this before you start: there is NO data to migrate

Measured in `hr_prod`, 42-org scope, 730 projects:

| | |
|---|---|
| projects with the legacy `allowedIps` string set | **0** |
| projects where `allowedIpsList` is SQL-NULL | 601 |
| projects where `allowedIpsList` holds the JSON literal `null` | **129** |
| projects with a real non-empty list | **0** |

The 129 are projects where an admin opened the allowlist editor and saved it empty —
v1 writes `Prisma.JsonNull` for an empty list. Nobody is using the feature in production
today. (Note for whoever measured this before: `allowedIpsList IS NOT NULL` returns 129,
which reads as "129 configured". It isn't. You need
`JSON_TYPE(allowedIpsList) = 'ARRAY' AND JSON_LENGTH(allowedIpsList) > 0`.)

**So this task is capability parity, not data recovery.** Nothing breaks today; the point
is that v2 can't express what v1 can express, so any org that turns the feature on in v2
gets a worse tool. Build it, but don't rush it, and don't let anyone tell you rows are
waiting on the other side.

## v1's model

Two columns on `XeroProject`, mid-expand-contract:

```prisma
/// Comma-separated IPv4 / CIDR allowlist (LEGACY — being replaced).
allowedIps      String?  @db.Text
/// Labelled allowlist: [{ label, cidr }]. Preferred by readers and
/// supersedes the legacy comma-separated `allowedIps` string above.
allowedIpsList  Json?
```

The read-side coercion — note the fallback ladder and how defensive it is about a column
the DB doesn't type-check:

```ts
/**
 * Coerces the two allowlist columns on a XeroProject row into the view
 * shape (`AllowedIp[]`). Prefers the new JSON `allowedIpsList` column
 * when present; falls back to parsing the legacy comma-separated
 * `allowedIps` string; returns [] when neither has anything.
 *
 * Defensive: the JSON column is untyped in the DB, so malformed entries
 * (missing label/cidr, wrong shape) are silently dropped rather than
 * poisoning the caller.
 */
function coerceAllowedIps(row): AllowedIp[] {
  const list = row.allowedIpsList
  if (Array.isArray(list)) {
    const out: AllowedIp[] = []
    for (const entry of list) {
      if (entry && typeof entry === "object" && !Array.isArray(entry) &&
          typeof entry.label === "string" && typeof entry.cidr === "string") {
        const label = entry.label.trim(), cidr = entry.cidr.trim()
        if (cidr.length > 0) out.push({ label: label.length > 0 ? label : "IP", cidr })
      }
    }
    if (out.length > 0) return out
    // Empty array in the JSON column falls through to the legacy string check
    // so a stray `[]` write doesn't hide a pre-existing legacy string.
  }
  const legacy = typeof row.allowedIps === "string" ? row.allowedIps : ""
  if (legacy.trim().length === 0) return []
  return legacy.split(",").map(s => s.trim()).filter(s => s.length > 0)
    .map((cidr, i) => ({ label: `IP ${i + 1}`, cidr }))
}
```

And the write side — one writer for both columns, so there is never a second source of truth:

```ts
// Single writer for both allowlist columns during expand-contract:
// whenever the caller updates the allowlist, we write the new JSON
// `allowedIpsList` AND null out the legacy `allowedIps` string in the
// same statement. Readers prefer the JSON column and only fall back to
// the string, so keeping the string populated here would create two
// sources of truth.
if (data.allowedIps !== undefined) {
  const entries = data.allowedIps ?? []
  allowlistWrite.allowedIpsList = entries.length === 0
    ? Prisma.JsonNull
    : entries.map(e => ({ label: e.label, cidr: e.cidr }))
  allowlistWrite.allowedIps = null
}
```

Validation before save:

```ts
const allowedIpSchema = z.object({
  label: z.string().trim().min(1, "Every IP row needs a label.")
          .max(60, "Label must be 60 characters or fewer."),
  cidr: z.string().trim().min(1, "Every IP row needs an address.")
          .refine(isValidIpOrCidr, { message: "Not a valid IPv4 address or CIDR range." }),
})
```

## v1's matcher — `lib/ip-whitelist.ts`, port this to C#

```ts
/**
 * IPv4 whitelist matcher — supports single IPs (treated as `/32`) and
 * CIDR ranges (e.g. `203.106.51.0/24`) in a comma-separated string.
 *
 * IPv6 not supported yet (Malaysian office networks are still
 * overwhelmingly IPv4). If the parser sees an entry that isn't a
 * plausible IPv4 or IPv4 CIDR it silently drops that entry — safer
 * than throwing at request time and blocking every clock-in.
 */

export function ipMatchesAllowlist(ip, allowlist): boolean {
  const ipAsInt = ipv4ToInt(ip)
  if (ipAsInt === null) return false
  for (const entry of allowlist) if (matchCidr(ipAsInt, entry.network, entry.prefix)) return true
  return false
}

/** Parse a single CIDR string OR a bare IPv4 (treated as `/32`). Null if unparseable. */
function parseCidrEntry(entry): { network: number; prefix: number } | null {
  const [ipPart, prefixPart] = entry.split("/")
  const ip = ipv4ToInt(ipPart)
  if (ip === null) return null
  let prefix = 32
  if (prefixPart !== undefined) {
    const parsed = Number.parseInt(prefixPart, 10)
    if (!Number.isFinite(parsed) || parsed < 0 || parsed > 32) return null
    prefix = parsed
  }
  // Mask the IP down to the network address so range comparisons are
  // canonical regardless of what the admin typed (e.g. accepting
  // `203.106.51.42/24` and treating it as `203.106.51.0/24`).
  const mask = prefix === 0 ? 0 : (0xffffffff << (32 - prefix)) >>> 0
  return { network: (ip & mask) >>> 0, prefix }
}

function ipv4ToInt(ip): number | null {
  if (!ip) return null
  const parts = ip.trim().split(".")
  if (parts.length !== 4) return null
  let out = 0
  for (const p of parts) {
    if (p.length === 0 || p.length > 3) return null
    const n = Number.parseInt(p, 10)
    if (!Number.isFinite(n) || n < 0 || n > 255) return null
    // Reject "01" / "001" style padded octets which parseInt happily
    // accepts but are ambiguous / non-standard.
    if (String(n) !== p) return null
    out = ((out << 8) | n) >>> 0
  }
  return out
}

function matchCidr(ipInt, network, prefix): boolean {
  if (prefix === 0) return true
  const mask = (0xffffffff << (32 - prefix)) >>> 0
  return ((ipInt & mask) >>> 0) === network
}

/** True when `entry` parses as a bare IPv4 or an IPv4 CIDR. Used by the
 *  admin form to reject a bad row before the write silently drops it. */
export function isValidIpOrCidr(entry: string): boolean {
  return parseCidrEntry(entry.trim()) !== null
}
```

In C#, `System.Net.IPAddress.TryParse` + `IPNetwork.TryParse` (.NET 8) will do most of
this — but **keep v1's rejection of zero-padded octets** (`01.2.3.4`), because
`IPAddress.TryParse` historically accepted odd forms, and an allowlist that quietly
matches more than the admin typed is the wrong failure direction for a security control.

## What v2 does today, and the four gaps

`backend/Modules/Attendance/AttendanceService.cs:1212` —

```csharp
private async Task<bool> IpAllowedAsync(string employeeId, string? projectId, EmployeePolicy? policy)
{
    if (policy is null || !policy.RequireIpWhitelist) return true;
    if (string.IsNullOrEmpty(projectId)) return true;

    var project = await _projects.GetByIdAsync(projectId);
    var allowed = (project?.AllowedIps ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (allowed.Length == 0) return true;   // not configured → skip

    var ip = _currentUser.IpAddress;
    if (string.IsNullOrEmpty(ip)) return false;   // enforced but unverifiable → block
    return allowed.Contains(ip, StringComparer.OrdinalIgnoreCase);
}
```

Called at clock-in (line 374) and clock-out (473); failure returns `IpNotAllowed()`.
Storage is `Project.AllowedIps varchar(1000)`, assigned straight from the DTO in
`ProjectService` with no validation.

**Gap 1 — no CIDR.** `allowed.Contains(ip)` is exact string equality. An admin typing
`203.106.51.0/24` gets a rule that matches literally nothing. v1 has matched ranges since
the feature shipped. This is the main reason to do this task.

**Gap 2 — no labels.** v1 rows are `{label, cidr}` ("KL Office", "Site VPN"). v2 has a
bare string, so an admin auditing 6 entries a year later cannot tell what any of them is.

**Gap 3 — behaviour on an unverifiable IP is inverted.** v2 blocks when the client IP is
unknown; v1 **skips the check** (`clientIp` null → `ipAllowed = null`, clock-in proceeds).
v1's docblock is explicit: *"Fail-safe: callers should treat null as 'check skipped'
rather than 'check failed'."*

**Gap 4 — mismatch is a hard block in v2, a soft gate in v1.** v1 lets an off-network
employee clock in **with a remark**, exactly like the off-geofence flow, and records the
outcome:

```ts
// IP-whitelist check. Same remark-override contract as geofence: an
// off-network employee can still clock in by providing a reason
// (site visit / WFH).
let ipAllowed: boolean | null = null       // true=matched, false=mismatch, null=skipped
if (enforceIp && projectAllowedIps && clientIp) {
  ipAllowed = ipMatchesRawAllowlist(clientIp, projectAllowedIps)
  if (!ipAllowed && !notes) throw new Error(OFF_NETWORK_REMARK_REQUIRED)
}
```

…persisted on the attendance session as `clockInIpAddress` + the tri-state
`clockInIpAllowed` (true matched / false overridden by remark / null check skipped), which
roll-call surfaces as a chip. v2's `AttendanceRecord` has **neither column**, so v2 can
neither explain nor audit an IP decision after the fact.

Gaps 3 and 4 are behaviour changes, not just schema. Implement v1's behaviour — the
`RequireIpWhitelist` policy flag that orgs already have set was written against v1's
meaning, and a hard block silently makes an existing policy stricter than the admin who
ticked it intended. **Say so explicitly in your summary** so it's a visible decision, and
if you think the hard block is worth keeping for a security control, argue it there
rather than quietly choosing.

## Do this

1. Add `ProjectAllowedIp` in `backend/Modules/Projects/Entities/`, `ITenantScoped`:
   `Id`, `OrganizationId`, `ProjectId`, `Label` (max 60), `Cidr` (max 43), `CreatedAt`.
   Index `(OrganizationId, ProjectId)`. A child table rather than a JSON column — it is
   the same shape as Task 1's geofence table, and EF handles it far better than JSON.
2. Register the DbSet. **Keep `Project.AllowedIps`** as the legacy fallback and port
   `coerceAllowedIps`'s ladder: rows in the table win; an empty table falls back to
   splitting the string into `IP 1`, `IP 2`… ; neither → empty → check skipped. One
   writer for both, nulling the string when the table is written, exactly as v1 does.
3. Add `backend/Modules/Attendance/IpAllowlist.cs` (or `Common/`) — a pure static port
   of v1's parser/matcher: bare IPv4 → `/32`, CIDR masked to its network address,
   unparseable entries dropped not thrown, padded octets rejected. Unit-test it against
   v1's cases: `10.0.0.5` vs `10.0.0.0/24` (match), `/32` exact, `0.0.0.0/0` (match all),
   `01.2.3.4` (reject), `10.0.0.5/33` (reject), garbage entry alongside a good one
   (good one still matches).
4. Rewrite `IpAllowedAsync` to use it, and change the two behaviours above: unknown
   client IP → skip the check, not block; mismatch → allow with a remark, mirroring
   `OffSiteProofMissing` / `OffSiteRequired`. Return the tri-state, not a bool.
   Leave `CurrentUser.IpAddress` alone — it already takes the **last** X-Forwarded-For
   hop on purpose (the first is client-supplied and spoofable), and that is correct.
5. Add `ClockInIpAddress` (string?) and `ClockInIpAllowed` (bool?) to `AttendanceRecord`,
   written on clock-in only when the policy enforces the check. Tri-state, same meaning
   as v1. Surface the chip wherever v2 shows clock-in detail.
6. Validate on save in `ProjectService` — every row needs a non-empty label ≤60 chars and
   a `Cidr` that passes the parser. Reject the write with a field error; do not silently
   drop rows the way the read path does.
7. Project settings UI: list / add / remove labelled entries, same screen and conventions
   as Task 1's points. Mention in the hint text that a bare IP means that one address and
   that ranges are written as CIDR.
8. `dotnet ef migrations add ProjectAllowedIps` (separate migration from Task 1's).

---

## Constraints (both tasks)

- Generate EF migrations with `dotnet ef migrations add <Name>`. Never hand-edit
  `AppDbContextModelSnapshot.cs`.
- Every new tenant-scoped entity must implement `ITenantScoped` or it will leak across
  organizations — the global query filter is what enforces isolation. Both of these
  tables hang off `Project`, which is tenant-scoped; the children must be too.
- **Additive only.** Do not drop or rename existing columns. The live database is shared
  and already holds migrated production data (42 orgs, 737 projects, 736 accounts, 479
  users). `Project.Latitude`, `Project.Longitude` and `Project.AllowedIps` all stay.
- v2 stores enums as strings and EF throws `Cannot convert string value …` on **read** if
  a value isn't a member of the C# enum. The row inserts fine and breaks later — prefer a
  plain string with a documented value set for anything sourced from v1.
- `dotnet build` and `dotnet test` must both pass before you call either task done.
- Report anything you found that contradicts this brief. It was written from the v1
  source and a production database you cannot see; if the v2 code says otherwise, the v2
  code in front of you wins and I want to know.
