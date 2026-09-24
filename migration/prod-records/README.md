# hr_prod → altomatehr : leave, attendance and claims records

Ran **2026-09-18**, after `../prod-settings`, `../prod-employees`, `../prod-coa`.
Moves the transactional records — the three modules a user actually looks at —
out of AltomateHR v1 production into the live v2 database.

## Was this blocked by the approval-chain problem?

**No.** That was the open question going in, and the answer is that historical
records carry their own decided state; they do not need a chain to exist. What the
missing chain costs is narrower and specific: **PENDING rows load correctly but
nobody can act on them**, because v2 derives "who approves this" from the team
roster, and `TeamMemberships` still holds 7 rows (see `../ANSWERS-v1-v2-gaps.md`,
Q1). In flight right now: **29 claims, 12 leave applications, 618 attendance
approvals**. They are not lost or wrong — they are parked until rosters land.

## What loaded

| table | loaded | of | not loaded |
|---|---|---|---|
| `AttendanceRecords` | **891** | 893 | 2 |
| `AttendanceSessions` | **905** | 912 | 7 |
| `AttendanceBreaks` | **9** | 18 | 9 |
| `AttendanceApprovalRequests` | **1816** | 1860 | 44 |
| `Claims` | **64** | 74 | 10 |
| `LeaveApplications` | **47** (+1 already there) | 48 | 0 |
| `LeaveEntitlements` | **3735** | 3815 | 80 |

Every FK resolves, every enum is a v2 member, every child sits in its parent's
org — `04-verify.sql` asserts all of it.

## Everything that did NOT load, and why

**10 claims + 2 attendance records — an `hr_dev` twin already holds the key.**
The 42-org prod load had to remap one org: v1's `cmogup4k90000kukzos5fj1qn` is
"Fusion ETA Sdn. Bhd." in `hr_prod` and "ZR TEST" in `hr_dev`, and the dev slice
got to v2 first. So v2 has **two** org rows, and the dev one already holds 16
claims whose `ClaimNumber` and `Id` match prod rows — the two databases are forks
of one ancestor. `ClaimNumber` is UNIQUE in v2, so those 10 prod claims cannot be
inserted without either renumbering them or deleting the dev rows. Same story for
2 attendance rows on the UNIQUE `(EmployeeId, Date)`.

Those 2 records cascade: **7 sessions, 4 breaks and 28 approval requests** hang off
them and were skipped with them. So 2 skipped rows cost 41.

*This needs a decision, not a script:* does prod data displace the ZR TEST demo
rows, or does ZR TEST keep them and those 10 prod claims stay out?

**5 breaks — no session to hang off.** v2's `AttendanceBreaks.AttendanceSessionId`
is NOT NULL. These 5 v1 breaks belong to records with **zero** sessions. Faking a
session id would be inventing data.

**3 OT approvals — no representable home.** v2 routes overtime through its own
`OvertimeRequests`, whose `StartAt`, `EndAt`, `RequestedMinutes`, `Reason` and
`BeforePhotoUrl` are all NOT NULL. Two of v1's three OT rows are threshold-derived
("Worked 8h 32m (threshold 8h 00m)") with NULL `otStartAt`/`otEndAt`, and none has
a photo. Migrating them means inventing a time range and a photo url.

**13 approval requests — no attendance record.** v1's `ApprovalRequest` has no FK
to the record; it is keyed by `(employeeId, date)`, which is the record's unique
key. These 13 CLOCK_OUT rows point at a day with no record. All 13 belong to the
mis-filed-day set below.

**80 leave entitlements — stale profiles.** v1 keeps `EmployeeProfile` rows that no
`EmployeeOrganization` links any more, with live-looking entitlements attached. See
the dedupe note in `03-leave.sql`: this is the one that fails loudly rather than
quietly, with a duplicate-key error on the first run.

## Things that are now true and were not obvious

**The day key needed no conversion.** v1's `AttendanceRecord.date` is already the
UTC-midnight local-day key v2 expects — all 893 rows are exactly `00:00:00`. It is
copied verbatim. **13 rows are mis-filed one day behind in v1 itself** (clocked late
evening UTC = next morning MYT), and that bad data is carried across unchanged on
purpose: 9 of the 13 would collide with a real row if "corrected", so fixing them
needs merge logic and its own run, not a `+1 day` shift.

**`REVIEWED` is gone.** 32 claims carried v1's `REVIEWED` status, which v2's
`ClaimStatus` does not have. They load as `APPROVED` — the same mapping the 10
already-loaded rows used. v2 stores enums as strings and throws
`Cannot convert string value` on **read**, so an unmapped spelling would have
loaded fine and emptied the claims screen later.

**Break subtype lives only in the title.** v1 has one `BREAK` kind; v2 splits
`BREAK_START` / `BREAK_END`. v1 has no column for the subtype — the `Team` model
comments reference a `breakSubtype` field that does not exist. The title
("Break start 09:53") is the only carrier, and all 35 rows match the pattern.

**ID conventions differ per table and nothing enforces it.** `Projects` and `Teams`
ids ARE prefixed `prod-` for the remapped org; `ChartOfAccounts` ids are NOT.
Getting it backwards produces silent orphans, not errors. A join that "finds
nothing" against one of these is usually this, not missing data.

**`_mig_orgmap.v2_id` is `utf8mb4_unicode_ci`** while every EF-created table is
`utf8mb4_0900_ai_ci`, so even a same-database `OrganizationId IN (SELECT v2_id …)`
throws `Illegal mix of collations` without an explicit `COLLATE`.

## Leave balances will read differently in v2 — 85 days of them

v2 has no `UsedDays` column. It derives days taken from APPROVED
`LeaveApplications`, which is correct *if* every day ever taken has an application
behind it. **22 entitlements fail that test**: their `usedDays` equals their
`openingUsedDays`, i.e. leave taken before the org joined AltomateHR and typed in
as an opening figure, with no application rows behind it. **77 days across 2025 and
8 days in 2026** disappear, and those employees will look like they have more leave
left than they do.

The other 18 of the 40 rows with `usedDays > 0` reconcile exactly against the
applications that loaded, so the derivation itself is sound — the gap is only the
opening balances. Fixing it means either an `OpeningUsedDays` column on
`LeaveEntitlement` (v1 has exactly that) or synthetic "opening balance"
applications, which would be fabricated history. **Decide before anyone trusts a
2026 balance in v2.**

## Known bad data in v2 that this migration did NOT cause

**16 claims carry an empty `XeroSyncStatus`** — all of them in the ZR TEST org, from
the earlier `hr_dev` slice, none from this run. EF will throw
`Cannot convert string value ''` when reading them, so that org's claims screen is
broken until they are set to `NOT_SYNCED`. One `UPDATE` fixes it; left alone here
because it is another migration's data.

**1 demo claim (`org-altomate`) points at a `User.Id` that does not exist.** Seed
data, not migrated data.

## Running it

```sh
mysql … < 01-attendance.sql   # records → sessions → breaks → approval requests
mysql … < 02-claims.sql
mysql … < 03-leave.sql        # entitlements → applications
mysql … < 04-verify.sql       # every check 0 except the labelled counts
mysql … < 99-rollback.sql     # removes exactly what 01–03 wrote
```

**Conflict policy: insert only where the unique key is free.** No existing v2 row is
ever updated or deleted, which is what keeps the hr_dev twins safe. The trade-off:
**a re-run is a no-op, it does not pick up later v1 edits.** v1 is still live and
still moving — this run alone found one chart-of-account created in v1 *after*
`../prod-coa` had run, which orphaned a claim until that migration was re-run (it
is idempotent; re-running it added the 1 missing account, 736 → 737 rows). Cutover
needs a drift pass over every migrated table, and that pass has to decide whether
v1 or v2 wins for a row that both have touched.
