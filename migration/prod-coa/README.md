# hr_prod → altomatehr : chart of accounts

Ran **2026-09-18**, after `../prod-settings`. Moves the chart of accounts from
AltomateHR v1 production (`hr_prod`) into the live v2 database (`altomatehr`).

**No Xero tokens are copied.** This is reference data only — the accounts claims are
coded to, so the app displays and files correctly. Every org still has to reconnect
Xero in v2 to resume syncing. See "Why no tokens" below.

Depends on `altomatehr._mig_orgmap`, built by `../prod-settings/00-orgmap.sql`.

## Scope

| | rows |
|---|---|
| `hr_prod.ChartOfAccount` in the 42-org scope | 1169 |
| skipped — liability types v2 has no home for | 497 |
| **loaded into `altomatehr.ChartOfAccounts`** | **672** |

10 of the 42 orgs have any chart of accounts at all. Split after load: 511 EXPENSE,
161 BANK.

## Running it

```sh
mysql … < 01-coa.sql      # the migration; one PK upsert per row
mysql … < 02-verify.sql   # all six checks must return 0
mysql … < 99-rollback.sql # removes exactly what 01 wrote
```

Re-running is safe: verified by running twice, row count unchanged at 736
(672 migrated + 64 that were already there).

## Type collapses to EXPENSE or BANK — this is the whole point

v2 models only two account types. `ChartOfAccount.Type` is a loose string, and the
settings UI filters on an **exact string match** — `AccountsSettings.tsx:150`
(`accounts.filter(a => a.type === tab)`) against the two tabs `EXPENSE` and `BANK`.
An account carrying any other string loads into the database fine and is then
**invisible in the app**. Same trap as the enum spelling bug in `../prod-settings`,
one layer up: the data is there, the screen is empty.

So v1's raw Xero types are collapsed the same way v2's own importer does it —
`XeroService.ToLocalAccountType()`: bank is bank, everything else importable is an
expense family type.

| v1 `type` | rows | → v2 `Type` |
|---|---|---|
| `EXPENSE` | 385 | EXPENSE |
| `BANK` | 161 | BANK |
| `DIRECTCOSTS` | 114 | EXPENSE |
| `NULL` (hand-made in-app) | 9 | EXPENSE |
| `Expenses`, `Exp` (hand-typed) | 3 | EXPENSE |
| `CURRLIAB` | 373 | **skipped** |
| `TERMLIAB` | 123 | **skipped** |
| `LIABILITY` | 1 | **skipped** |

### Why the 497 liability rows are skipped, not collapsed

`XeroService.ShouldImportAccount()` imports claimable types
(`EXPENSE`/`DIRECTCOSTS`/`OVERHEADS`) plus `BANK`, and nothing else — so the moment
one of these orgs reconnects Xero, v2 would not receive a single liability account.
Carrying them over would plant 497 rows that no tab can display and that diverge from
the org's very next sync. Collapsing them to EXPENSE would be worse: they'd surface
in the claim form's account picker, which v1 never did.

Nothing is lost. All 497 are `isSelectable = 0` in v1, and **zero claims in `hr_prod`
are filed against any of them**.

## Field mapping

`id`→`Id`, `organizationId`→`OrganizationId` (through the org map),
`code`→`Code`, `name`→`Name`, `xeroAccountId`→`XeroAccountId`, `status`→`XeroStatus`,
`isSelectable`→`IsSelectable`, `limitAmount`→`LimitAmount`,
`allowMileageClaim`→`AllowMileageClaim`, `mileageRate`→`MileageRate`,
`createdAt`→`CreatedAt`, and `isDisabled`→`IsArchived` (same rename as
`../prod-settings`).

`XeroSyncedAt` has no v1 column. For Xero-sourced rows it takes `updatedAt`, which
*is* the last sync write; hand-made accounts stay NULL.

### Dropped, and why it costs nothing

- `isCustom`, `isBankAccount`, `archivedByXeroConnect`, `updatedAt` — no v2 column.
  `isBankAccount` is redundant with `type='BANK'`: exactly one row of 1169 disagrees,
  and the type wins, which is the rule v2 applies anyway.
- `limitPeriod`, `limitScope` — no v2 column. **Zero rows in `hr_prod` set either**,
  and zero set `limitAmount` or `mileageRate`, so the limit model carries no data at
  all. Two rows have `allowMileageClaim = 1`; both migrated.

## Why no tokens (corrected reasoning)

An earlier note said v2's DataProtection "cannot read" v1's tokens. That was wrong and
is worth not repeating: v1 stores `accessToken`/`refreshToken` as **plaintext**. The
real constraint is on the write side — v2 wraps tokens with
`IDataProtector.Protect()` (`XeroService.cs:33`, key ring in `storage/dp-keys`), which
SQL cannot manufacture. A small C# utility could, so copying them is *possible*.

It still must not be done while v1 is live. Xero refresh tokens are single-use and
rotate on every refresh, and v1 refreshes **lazily, on demand**
(`getUsableXeroAccessToken()` — no cron), so whichever app calls Xero next wins the
race and the other one's copy dies. Worse, v1's own race guard compares the refresh
token against its own row, so a rotation done by v2 is invisible to it: v1 concludes
the grant is dead and starts telling users "reconnect Xero" on live production.

Reconnecting in v2 mints an independent token pair and leaves v1 untouched, which is
what parallel running during cutover needs.

## Verification done

All six checks in `02-verify.sql` return 0: nothing missing, no liability rows leaked,
every migrated `Type` in `{EXPENSE, BANK}`, field-level diff against `hr_prod` clean
across all 672 rows, no orphaned org ids, no duplicate code within a migrated org.
`GET /accounts` returns 401 (route alive, auth required), frontend 200, no container
errors.

## Pre-existing debris, NOT caused by this migration

Present before the run, in orgs this migration never touches — left alone deliberately:

- `ZR TEST` account `cmqia69lq000c52kz915l1img` ("200 General Expenses") has
  `Type = 'Expenses'`, so it is invisible in both UI tabs. Created 2026-06-17.
- Duplicate `(OrganizationId, Code)` pairs on code `6100` "Travel Expenses":
  three rows in `Fusioneta Sdn Bhd`, two in `Oscar Test Org`. `hr_prod` has no
  duplicate pairs at all.

`02-verify.sql` scopes its type and duplicate checks to migrated rows so this debris
neither masks a real failure nor gets silently "fixed" by a data migration.

## Backup

`/var/backups/altomatehr-v2/altomatehr-ChartOfAccounts-pre-prod-coa-20260918-145408.sql`
(table-level; `ChartOfAccounts` has no FK constraints in either direction).

## Note

Commit this directory. The repo has unrelated uncommitted work around it, so never
`git add -A` here.
