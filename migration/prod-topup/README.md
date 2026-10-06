# hr_prod → altomatehr : full records top-up (claims, attendance, leave, payroll, files)

Ran **2026-09-25 00:08 MYT**, on top of `../prod-settings`, `../prod-employees`,
`../prod-coa`, `../prod-records` (all 2026-09-18) and `../prod-gsl` (2026-09-24).
v1 had kept moving for a week, and payroll had only ever been migrated for GSL.

```sh
bash run.sh 00-backup.sh \
  ../prod-records/01-attendance.sql ../prod-records/02-claims.sql ../prod-records/03-leave.sql \
  02-drift.sql 03-payroll.sql 04-files.sql 04-copy-files.sh 05-verify.sql
```
Every step is re-runnable: a second run is a no-op. The full sequence was first dry-run
in one transaction ending in ROLLBACK. The live run produced identical counts.
Backup: `/var/backups/altomatehr-v2/altomatehr-pre-topup-20260925-000857.sql(.gz)`.

## What it did

| step | rows |
|---|---|
| new attendance rows since 09-18 (records+sessions+breaks+approvals) | 222 |
| new claims / leave applications | 3 / 5 |
| **drift**: v1 edits since 09-18 applied (records 38, sessions 20, approvals 48, claims 16, entitlements 2) | 124 |
| **payroll**: runs / payslips / line items / adjustments | 142 / 1823 / 2128 / 66 |
| payroll run members (runs that excluded someone in v1) | 120 |
| salary changes / loans | 67 / 2 |
| file refs: claim receipts 12, supporting docs 9, record selfies 229, session selfies 321, employee docs 2 | — |
| v1 disk files copied into the v2 volume (10 selfies; 14 were already there, 0 missing) | 10 |

## Rules worth knowing before re-running

**Drift: who wins.** A migrated row is overwritten only if v1 is newer **and** v2 has
never touched it. The "never touched" test is `MICROSECOND(UpdatedAt) % 1000 = 0`.
It works because migrated rows carry v1's millisecond (Prisma) timestamps, while every EF
write uses `DateTime.UtcNow` with microseconds. On 2026-09-25, 0 migrated rows had a
v2 edit.

**Payroll is insert-if-free.** Nothing existing is updated. A v1 run whose
`(org, year, month)` v2 already holds is skipped with all its children, because v2 is
the source of truth there. That skipped **Peak Bridges Apr–Aug 2026**, which their admin
re-entered in v2 on 09-23 with the same payslip counts. The remapped Fusion ETA org gets
`prod-` on every payroll child id, the same as its profiles. v1 stores *exclusions*
while v2 stores *inclusions* (`PayrollRunMembers`, and no rows means everyone). So runs
that excluded someone get member rows = the people who got a payslip.

**Files.** Xero-hosted files are **not** downloaded; the rows are pointed at v2's Xero
proxy routes:

| v1 | v2 |
|---|---|
| `/api/xero/files/<id>/content` (receipt / supporting doc) | `/claims/receipts/xero/<id>` (+ `ReceiptXeroFileId`) |
| selfie `<xero guid>` | `/attendance/photos/xero/<guid>` |
| `/api/leave/files/<id>/content` | unchanged, since `LeaveService` derives the url from `XeroFileId` |
| `/uploads/receipts/<f>` | `/claims/receipts/<f>` + file copied |
| `/uploads/attendance-selfies/<f>` | `/attendance/photos/<f>` + file copied |
| PayrollProfile.payrollDocuments (camelCase) | PascalCase + `StoredFileName` |

The Xero-hosted ones render **only once that org is connected to Xero in v2, to the
same tenant as in v1**. As of 09-25 only GSL is. Orgs owning Xero-hosted files:
GLOBE ENGINEERING (~300 selfies + 13 receipts), Peak Bridges, Virtual.io,
GLOBE EXPRESS, and Fusion ETA. Fusion ETA's 12 receipts have no v1 connection left
either, so they may be unrecoverable.

`../prod-employees/02-profiles.sql` now converts employee documents to PascalCase. The
09-24 re-sync had silently reverted 2 fixed rows by copying camelCase verbatim.

## Not in v2, and why (05-verify section C)

- **Payroll**: 5 Peak Bridges runs (v2 wins), and 7 runs of the empty org rows "Gosaas"/
  "Evergrowth". Those have 0 members and were never migrated; the real GOSAAS MALAYSIA /
  Evergrowth Consulting orgs are separate ids. 1 salary change hangs off a v1 profile with
  no org.
- **Records**: the same set as `../prod-records/README.md`. That is 10 claims and 2
  attendance records blocked by the ZR TEST twins, plus their cascade, breaks with no
  session, and approval requests with no record. There are also 3 OT approvals (no
  representable v2 home), 7 `ClaimApprovalEntry` rows (v2 has no claim-approval history
  table) and 8 stale-profile entitlements.
- Dropped payslip columns with no v2 home: `netShortfall` (6 non-zero), `voluntaryPcb` (3).

## Pre-existing issues this did NOT cause (05-verify flags them)

- 16 ZR TEST claims (hr_dev slice) with `XeroSyncStatus = ''`. EF throws on read, so
  ZR TEST's claims screen is broken until they are set to `NOT_SYNCED`.
- 15 `PayrollRunMembers` pointing at deleted runs (Peak Bridges ×3 runs, org-altomate ×3).
  v2's run delete does not remove member rows. It's harmless, but it's a v2 bug.
