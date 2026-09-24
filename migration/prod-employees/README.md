# hr_prod -> altomatehr : employee migration

Ran **2026-09-18**, after `../prod-settings/` (companies + configuration) and
`../prod-admins/` (owner/admin accounts). This brings across the remaining people --
SUPERVISOR and EMPLOYEE -- with their memberships and full employee/payroll profiles.

## Shape of the source

v1 splits what v2 keeps in one table:

| v1 | rows in scope | v2 |
|---|---|---|
| `User` (identity + role + home org) | 458 distinct | `Users` (identity only) |
| `EmployeeOrganization` (user x org x profile) | 467 | `OrganizationMemberships` |
| `EmployeeProfile` (job title, employee no., policy) | 467 | `EmployeeProfiles` (merged) |
| `PayrollProfile` (personal, statutory, bank, salary) | 455 matched | `EmployeeProfiles` (merged) |

Unlike admins -- who have no `EmployeeOrganization` row at all -- every employee has one,
and it is the scope definition here. 12 rows have no `PayrollProfile`; they migrate with
personal fields empty and the NOT NULL defaults below.

## Loaded

| table | rows added |
|---|---|
| `Users` | 450 |
| `OrganizationMemberships` | 467 |
| `EmployeeProfiles` | 467 |

Totals after: 479 users, 500 memberships (385 Employee, 89 Supervisor, 14 Owner,
12 Admin), 471 profiles, across 46 orgs.

## Identity rules

v2 `Users.Email` is UNIQUE globally and v1's is **not**, so email always wins over id:

1. email already in `altomatehr.Users` -> reuse that account, insert no identity row
2. several v1 `User` rows share one email -> collapse onto the earliest-created row
3. id already in `altomatehr.Users` under a different email -> cuid collision, prefix `prod-`
4. otherwise -> carry the v1 cuid verbatim

Result: 458 v1 rows -> **453 v2 accounts**, 3 reused, 1 prefixed, 5 collapsed.

**Rule 2 in practice.** v1 gives a person one `User` row *per company*; v2 gives one
account with several memberships. Three people at the Globe group held 2-3 rows each under
one email (`terencetan@`, `wendyytan@`, `tineshwaranodiah@`) spanning GLOBE ENGINEERING,
GLOBE EXPRESS and GLOBE SUCCESS LEARNING. They are now single accounts with 2-3
memberships, which is the v2 model, and the earliest row's name won where the spelling
differed (`TINESH WARAN A/L ODIAH` vs `Tinesh waran Odiah`).

**Rule 3 in practice.** `cmol1wghv0000yokzdentkoqf` is `simon1@fusioneta.com` in hr_prod
and `simon@fusioneta.com` in v2 -- the documented dev/prod cuid overlap, two different
people. The prod one was written as `prod-cmol1wghv0000yokzdentkoqf`. Carrying it verbatim
would have silently handed one person the other's account.

## Field notes

- **`EpfEmployeeRate` is carried verbatim as a percentage** (v1 holds `11.00` / `2.00`).
  `PayslipCalculator` reads it as a percent and `EpfCalculator` clamps upward from `11m`,
  so `0` means "statutory rate", not "no contribution". Note the pre-existing v2 demo rows
  hold `0.1100`, i.e. the *fraction* -- those are in the wrong unit, and the clamp is what
  hides it.
- **NOT NULL defaults** where v1 allowed NULL: `PaymentMethod` -> `BANK_TRANSFER`,
  `SalaryType` -> `MONTHLY`, every boolean flag -> `0`, `EpfEmployeeRate` -> `0`.
- **`PolicyId`** carries the same `prod-` prefix its org did, and is dropped to NULL if the
  policy did not come across -- it can never plant an orphan. 0 rows needed that.
- Every enum value in the source is already a valid v2 enum member (`Gender`, `IdType`,
  `MaritalStatus`, `SocsoScheme`, `PaymentMethod`, `SalaryType`), and no string column
  overflows v2's narrower varchars. Both were checked before loading, not after --
  see the `MONTHLY_BASED` incident in `../prod-settings/README.md`.
- Dropped, no v2 column: `PayrollProfile.addressLine3` (14 rows carry one).
- `ShiftId` stays NULL -- v1 has no equivalent on this table.

## Verification

All checks returned 0: missing users / memberships / profiles, wrong role, duplicate
emails, orphans against Users, Organizations and EmployeePolicies, profiles without a
membership, all 6 enum guards, and a field-level diff of `MonthlySalary`, `HourlyRate`,
`EpfEmployeeRate`, `IdNumber`, `BankAccountNumber`, `JoinDate` and `DateOfBirth` against
`hr_prod`. Per-org headcount matches source exactly for all 42 orgs. App healthy
afterwards: `/employees` 401, frontend 200, no container failures.

## Running it

```sh
mysql … < 00-peoplemap.sql   # builds altomatehr._mig_peoplemap
mysql … < 01-people.sql      # Users + OrganizationMemberships
mysql … < 02-profiles.sql    # EmployeeProfiles
mysql … < 03-verify.sql      # every 'bad' must be 0
mysql … < 99-rollback.sql    # removes exactly what 01 + 02 wrote
```

Every write is an upsert, so re-running is safe. `PasswordHash` is deliberately never
refreshed on conflict: `AuthService` rewrites v1 scrypt to BCrypt on first login
(`625639c`) and a re-run must not undo that. These employees sign in with their existing
v1 production passwords.

## Backup

`/var/backups/altomatehr-v2/altomatehr-pre-employee-migration-20260918-060711.sql[.gz]`
(`Users`, `OrganizationMemberships`, `EmployeeProfiles`).

## Still not migrated

Transactional and history tables, none of which have been touched by any of the three
migrations: `EmployeeTeamMembership` (529), `EmployeeProjectAssignment` (622),
`EmploymentStint` (346), `SalaryChange` (63), `EmployeeTransfer` (3), `EmployeeLoan` (2),
plus attendance, claims, leave applications and payroll runs.

`EmployeeTeamMembership` is worth doing next if approvals matter -- v2 has a
`TeamMemberships` table and the teams themselves are already migrated. Mind the trap: its
`employeeProfileId` is an **EmployeeProfile id**, not a user id, unlike `Claim` and
`AttendanceRecord` which carry user ids.
