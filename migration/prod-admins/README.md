# hr_prod -> altomatehr : admin account migration

Follow-on to `../prod-settings/`, which loaded 42 companies and their configuration but
**no people**. This migrates the OWNER/ADMIN accounts for those companies so the tenants
have someone who can log in and administer them.

Status: scripts ready and the id map is built; `01-admins.sql` had not been executed as of
writing (blocked by the session's permission classifier).

## What "admin" means in v1

v1 keeps the role on `User.role` (`OWNER | ADMIN | SUPERVISOR | EMPLOYEE`), not on the
membership table. Admins have **no `EmployeeOrganization` row and no `EmployeeProfile`** --
0 of the 29 OWNER/ADMIN users in `hr_prod` appear in `EmployeeOrganization`. They are tied
to their company by `User.organizationId` alone, so that column defines the scope here.

That also means no profile rows are needed on the v2 side: the existing v2 Owner/Admin
memberships mostly have no `EmployeeProfiles` row either, and nothing in `Modules/Auth`
reads one.

## Scope

| | count |
|---|---|
| OWNER/ADMIN users in `hr_prod` | 29 |
| ... whose org is one of the 42 migrated | **21** (10 Owner, 11 Admin) |
| ... whose org was skipped as an empty shell | 7 |
| ... with no `organizationId` at all | 1 |
| orgs of the 42 that get an admin | **20** |
| orgs of the 42 with no admin in the source | **22** |

### The 22 orgs that cannot get an admin

`hr_prod` simply has no OWNER or ADMIN user for them; 7 have supervisors, 15 have
employees only. This includes the two largest tenants in the estate:

- **GLOBE ENGINEERING SDN. BHD.** — 230 members, 51 supervisors, zero admins
- **GLOBE EXPRESS SERVICES SDN. BHD.** — 90 members, 6 supervisors, zero admins

They appear to be run by Fusioneta staff through the superadmin whitelist
(`SUPERADMIN_EMAILS`) rather than by a customer admin. Giving them a v2 admin is a
decision about who should hold that access, not something this migration can derive --
promoting a supervisor would grant someone access they do not have in v1.

### The 7 admins with no org to join

Their companies have an owner but zero employees, so `prod-settings` skipped them as empty
shells (`RINA TEST FAIRY`, `VEYA`, `NIMBLE SYSTEMS`, `DY'S COFFEE CENTER`,
`Creative Solution (Do not delete test)`, `KLINIK LOTUS SEREMBAN`, `MY NEXUS CONSULTING`).
Migrating them means widening the org scope first.

## Identity rules

v2 `Users.Email` is **globally unique** (`IX_Users_Email`), while v1 `User.email` is not,
so email takes priority over id:

1. email already in `altomatehr.Users` -> reuse that account, insert no identity row
2. id already in `altomatehr.Users` under a different email -> collision, prefix `prod-`
3. otherwise -> carry the v1 cuid verbatim

Two rows resolve through rule 1, both benign:

| v1 user | org | resolution |
|---|---|---|
| `cmogukwu80000iekzyzdw4fmp` (admin@example.com) | Fusion ETA Sdn. Bhd. | same id **and** email already in v2 as ZR TEST's Owner -- the same account, since ZR TEST is the dev clone. Membership only. |
| `cmpqcedc900014bm83dg1gns1` (simon@fusioneta.com) | Virtual.io | Simon's existing v2 account (`cmol1wghv…`, ZR TEST Supervisor) gets an Admin membership in Virtual.io. |

Rule 1 is also what makes a re-run idempotent: after a successful load every admin
re-resolves to the account the previous run created.

## Running it

```sh
mysql … < 00-adminmap.sql   # builds altomatehr._mig_adminmap (21 rows, 2 reuse, 0 prefixed)
mysql … < 01-admins.sql     # the load; upserts, additive only
mysql … < 02-verify.sql     # every 'bad' must be 0
mysql … < 99-rollback.sql   # removes exactly what 01 wrote
```

`01-admins.sql` deliberately does **not** refresh `PasswordHash` on conflict. v1 scrypt
hashes are carried over as-is and `AuthService` rewrites them to BCrypt on first login
(`625639c`); refreshing on a re-run would undo that upgrade. Passwords are otherwise
unchanged, so these admins sign in to v2 with their existing v1 production password.

Rollback leaves `reuse = 1` accounts alone -- they existed before this ran.

## Backup

`/var/backups/altomatehr-v2/altomatehr-pre-admin-migration-20260918-053638.sql[.gz]`
(`Users` + `OrganizationMemberships`).

## Still not migrated

Supervisors and employees (467 `EmployeeOrganization` rows across the 42 orgs), employee
profiles, and all transactional history. Xero connections cannot migrate at all.
