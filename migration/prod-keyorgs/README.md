# hr_prod → altomatehr : companies that hold an API key

Prepared **2026-09-24**. Brings over the companies New-Altomate provisioned through v1
that v2 does not have yet, so that once New-Altomate points at v2 (and the v1 API keys
are copied across) every key resolves to a real v2 company.

Company record + settings only, exactly like `../prod-settings`. **No people, no
transactional history, no keys** — the keys are a separate step that must wait until
the `payroll:write` scope release (`1dd959c`) is live, or the copied keys would open
every payroll endpoint.

## Why these were missing

`../prod-settings` (2026-09-18) took every `hr_prod` org with at least one member.
New-Altomate creates a company via `POST /admin/organizations` and gets a key back
before anyone joins it, so a company that was provisioned but not yet used has a key
and no members — and was skipped.

## Scope

26 organizations: every `hr_prod` org with an **active** `ApiIntegration` and no
v2 `Organizations` row. v1 has 56 orgs with keys; the other 30 are already in v2.
22 of the 26 were issued by the "Altomate Corporate Services Sdn Bhd" master key,
one (Acme Corp) by "Demo-Testing", and three keys were created by hand in Settings
(ABPJ, ABAP, ABSB).

| Table (v2)              | rows | source (v1)              |
|-------------------------|------|--------------------------|
| Organizations           | 26   | `Organization`           |
| LeaveTypes              | 208  | `LeaveType`              |
| Projects                | 21   | `XeroProject`            |
| Teams                   | 20   | `Team`                   |
| EmployeePolicies        | 54   | `EmployeePolicy`         |
| PolicyLeaveEntitlements | 0    | `PolicyLeaveEntitlement` |
| PayrollSettings         | 1    | `PayrollSettings`        |
| PayrollCompanyInfos     | 3    | `PayrollCompanyInfo`     |

None of the 26 has a chart of accounts or a Xero connection in v1, so `../prod-coa`
has nothing to add for them.

**LHDN cert Sdn. Bhd.** is the one exception to "no members": it has 5 in v1 and was
created after the 09-18 run. Its people are NOT moved here.

## Running it

```sh
node run.mjs                # dry run of 00→03 in ONE transaction, rolled back, scratch map dropped
node run.mjs --commit       # same, commits only if 01 and 03 pass; JSON backup of the 8 tables
                            # first, to ~/altomatehr-migration-backups/ (outside the repo)
```

Or by hand, as ../prod-settings did:

```sh
mysql … < 00-orgmap.sql     # builds altomatehr._mig_orgmap_keyorgs (scope, frozen on first run)
mysql … < 01-preflight.sql  # every row must be 0 — stop if not
mysql … < 02-settings.sql   # the migration; every write is a PK upsert
mysql … < 03-verify.sql     # all 0, except the known org-phase2-verify debris (1 + 1)
mysql … < 99-rollback.sql   # removes exactly what 02 wrote
```

`02`, `03` and `99` are the `../prod-settings` scripts pointed at a different map table.
That table is separate on purpose: `../prod-settings/99-rollback.sql` deletes by
`_mig_orgmap`, and must not start deleting these companies too.

## Checks

- **No org id collision.** 00 only takes org ids v2 does not have, so nothing is
  remapped (unlike Fusion ETA / ZR TEST on 09-18).
- **No child id collision.** dev and prod share cuids, and 02 upserts by primary
  key, so a child id already used in v2 by another org would be overwritten.
  `01-preflight.sql` checks all 7 child tables: 0.
- **Schema still fits.** No v2 column added since 09-18 is NOT NULL without a
  default, so the 09-18 column lists still insert cleanly (latest v2 migration at
  prep time: `20260923165436_MasterKeys`).
- **Enums.** v1 `MONTHLY_BASED` → `MONTHLY` is handled by 02, as before. Plan, tier
  and mileage-unit values are all ones v2 knows.
- **Addons.** 17 of the 26 have `addons = null`, which becomes `''`. v2 does not read
  addons to decide modules (DIY+PAID and EXPERT get every module, FREE only the
  base ones), so an empty value changes nothing.

## Ran 2026-09-24 (16:07 UTC)

`node run.mjs --commit`: preflight all 0; added exactly 26 / 208 / 21 / 20 / 54 / 0 / 1 / 3
rows (table order above); verify all 0 except the org-phase2-verify debris. v2 went from
47 to 73 organizations, and every one of v1's 56 key-holding orgs now exists in v2 under
the same id and name. `altomatehr._mig_orgmap_keyorgs` is left in place for 99-rollback.

Backup (outside the repo, on the machine that ran it):
`~/altomatehr-migration-backups/altomatehr-pre-keyorgs-2026-09-24T160727682Z.json`

## Dry run (2026-09-24)

Whole sequence in one transaction, then rolled back, with a scratch map table that
was then dropped: added exactly the counts above, preflight all 0, verify all 0
except the pre-existing debris. Afterwards v2 still had 47 organizations and no
scratch table.
