# hr_prod → altomatehr : company + settings migration

Ran **2026-09-18**. Moves company records and their configuration from AltomateHR v1
production (`hr.altomate.io`, db `hr_prod`) into the live v2 database (`altomatehr`,
served by `altomatehr-v2.fusioneta.com.my`).

**No people and no transactional history.** Users, memberships, employee profiles,
attendance, claims, leave applications and payroll runs are NOT touched.

Both databases live on the same DO cluster (53787), so this is plain cross-database
`INSERT … SELECT` rather than an ETL process. `hr_prod` is only ever read.

## Scope

42 organizations — every `hr_prod` org with at least one member. The other 30 prod
orgs are empty shells and were skipped.

| Table (v2)              | rows loaded | source (v1)              |
|-------------------------|-------------|--------------------------|
| Organizations           | 42          | `Organization`           |
| LeaveTypes              | 337         | `LeaveType`              |
| Projects                | 730         | `XeroProject`            |
| Teams                   | 159         | `Team`                   |
| EmployeePolicies        | 90          | `EmployeePolicy`         |
| PolicyLeaveEntitlements | 4           | `PolicyLeaveEntitlement` |
| PayrollSettings         | 32          | `PayrollSettings`        |
| PayrollCompanyInfos     | 32          | `PayrollCompanyInfo`     |

## Running it

```sh
mysql … < 00-orgmap.sql     # builds altomatehr._mig_orgmap (scope + id map)
mysql … < 02-settings.sql   # the migration; every write is a PK upsert
mysql … < 03-verify.sql     # all checks must return 0
mysql … < 99-rollback.sql   # removes exactly what 02 wrote
```

Re-running is safe and produces no duplicates — verified by running it twice and
confirming identical row counts.

## The cuid collision (important)

v1 cuids are carried over verbatim as v2 primary keys, as in the 2026-08-24 `hr_dev`
migration. That is legal because v2 `Id` columns are `varchar(255)` and EF only
generates GUIDs for new rows — but **dev and prod share 15 org ids and 54 user ids**,
and the v2 database already held a dev-sourced slice.

One in-scope org collided:

| cuid | in `hr_prod` | already in v2 as |
|---|---|---|
| `cmogup4k90000kukzos5fj1qn` | Fusion ETA Sdn. Bhd. (6 people) | **ZR TEST** |

Rather than overwrite ZR TEST, that org and all its children were written under a
deterministic `prod-` prefix (`prod-cmogup4k90000kukzos5fj1qn`). ZR TEST is byte-for-byte
unchanged: still 8 LeaveTypes and 4 EmployeePolicies, still named "ZR TEST".

The remap rule in `00-orgmap.sql` is generic — any prod org whose id already exists in
v2 **under a different name** gets prefixed. A matching name means the row is one an
earlier run of this migration wrote, so it keeps its id and the upsert stays idempotent.

**Fusion ETA Sdn. Bhd. and ZR TEST are almost certainly the same tenant** (dev was cloned
from prod and renamed). They are now two separate orgs in v2. Merging them is a decision
for Simon, not something this migration should guess at.

## What v2 has no home for (dropped)

- `Organization.timezone`, `otEnabled`, `allowedCurrencies`, `supervisorReportEnabled`,
  `allowForecastedLeaveApply`
- `ProjectGeoLocation` — 250 rows of multi-point geofences. v2 `Projects` holds a single
  `Latitude`/`Longitude`, so only the primary point survives.
- `ApprovalChainStep` — 492 rows. v2 models approvals through `TeamApprovalOverrides`,
  which is a different shape (and currently empty).
- `XeroProject.projectManagerId`, `isManual`, `allowedIpsList`
- `Team.requireClockInApproval` / `requireClockOutApproval` / `requireBreakStartApproval` /
  `requireBreakEndApproval`
- Xero connections — deliberately not copied. **Every migrated org must reconnect
  Xero.** (Not because v2 *cannot* read v1's tokens — v1 stores them in plaintext —
  but because Xero refresh tokens are single-use and rotate, so a copy would break
  whichever of v1/v2 refreshes second while both are live. Reasoning in
  `../prod-coa/README.md`.) The chart of accounts those connections produced **is**
  migrated, separately, by `../prod-coa`.

### Enum spelling trap (cost a live error)

v2 stores enums as strings and EF throws `Cannot convert string value …` on **read** if a
value is not a member of the C# enum — the row loads fine and breaks later. v1's
`EmployeePolicy.salaryType` is `enum('HOURLY','MONTHLY_BASED')`; v2's `SalaryType` is
`{HOURLY, MONTHLY}`. Carrying it verbatim put 52 unreadable policies in the live db and
threw on every policy read until `MONTHLY_BASED` was rewritten to `MONTHLY`.
`02-settings.sql` now does that translation, and `03-verify.sql` checks every migrated
enum column. Every other enum (OtMethod, AccrualMethod, WorkingDaysRule, Plan, Tier,
MileageUnit) matched v1 exactly.

Renames that are NOT losses: `claimCutoffDay`→`ClaimRunCutoffDay`,
`xeroMapping`→`XeroMappingJson`, `autoClockOutAfterMin`→`AutoClockOutAfterMinutes`,
`archivedAt`→`IsArchived`, `moduleConfig`→`ModuleApprovalConfig`,
`XeroProject.status`→`XeroStatus`, `isDisabled`→`IsArchived`.
`Organization.addons` converts from a JSON array to v2's comma-joined string
(`OrgModules.Join`), e.g. `["expense_claim", "clock"]` → `expense_claim,clock`.

## Verification done

- All 8 tables loaded the exact predicted row counts.
- Field-level diff of all 42 orgs against `hr_prod`: **0 mismatches**.
- Referential integrity: 0 orphans across every migrated FK.
- Per-org LeaveType and Project counts match source exactly.
- Idempotency: second run exits 0, row counts unchanged.
- App healthy afterwards: `/api/employees` 401, frontend 200, no container errors.

Pre-existing debris, NOT caused by this migration (present in the pre-migration backup):
one `PayrollSettings` and one `PayrollCompanyInfos` row pointing at a non-existent org
`org-phase2-verify`, created 2026-09-10.

## Backup

`/var/backups/altomatehr-v2/altomatehr-pre-prod-settings-migration-20260918-094214.sql[.gz]`

## Note

The 2026-08-24 `hr_dev` migration tooling was left uncommitted and has since been lost
(almost certainly a `git clean` during a redeploy). **Commit this directory.**
The repo has unrelated uncommitted work around it, so never `git add -A` here.
