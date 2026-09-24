# hr_prod -> altomatehr : GLOBE SUCCESS LEARNING top-up

Ran **2026-09-24** for one org, `cmpcs5z6300006qkzpew0mymt` (not remapped, every id verbatim).
GSL's company, people, projects, teams, leave and payroll settings were already loaded by the
2026-09-18 estate runs; this adds what those missed:

| table | rows |
|---|---|
| ChartOfAccounts | 208 (74 liability accounts skipped, same rule as `../prod-coa`) |
| Projects (geo re-sync) | 1 |
| ProjectGeofencePoints | 1 |
| SalaryChanges | 1 |
| PayrollRuns / Payslips / PayslipLineItems / PayrollRunAdjustments | 3 / 15 / 2 / 3 |

The COA and the geofence were created in v1 on 2026-09-24 (GSL connected Xero that day),
after `../prod-coa` had run. Payroll had never been migrated to v2 for any org.

Payroll notes: `PcbCalculationJson` is loaded NULL (v1's breakdown keys don't match v2's
`PcbBreakdown`; a partial parse would print a wrong worksheet). `PcbAdditional` comes from
that JSON, `PcbNormal = pcb - PcbAdditional`, `ProrationDaysInPeriod` = calendar days.
No `PayrollRunMembers` rows (no member rows = everyone). No Xero tokens.

```sh
mysql … < 01-gsl.sql     # one transaction, PK upserts, re-run is a no-op (verified)
mysql … < 02-verify.sql  # every `bad` must be 0 (all 17 were)
mysql … < 99-rollback.sql
```

Backup: `/var/backups/altomatehr-v2/altomatehr-pre-gsl-topup-20260924-154139.sql`
