---
id: employee-missing-from-payroll-run
title: An employee is missing from the payroll run, or the run won't submit
module: payroll
audience: [admin]
kind: how-to
status: by-design
severity: medium
escalate: false
verified: 2026-09-23
symptoms:
  - One employee isn't in this month's payroll
  - The payroll run shows fewer staff than we have
  - Needs attention on the payroll run
  - Cannot submit — employees need required fields filled
  - Cannot submit — Company Info is missing
  - Pekerja tiada dalam senarai gaji bulan ini
keywords: [payroll run, missing employee, needs attention, incomplete profile, cannot submit, company info, readiness]
related: [employee-import-updates-only, socso-shows-zero, pcb-differs-from-previous-system]
internal_ref: "runbook §4 go-live checklist"
---

## What's happening

AltomateHR only pays employees whose payroll profile is complete, and won't
submit a run until the company's own details are filled in. Both are
deliberate: a wrong or missing detail here means a wrong tax deduction or a
rejected filing.

## What you can do

**An employee is missing, or "employees need required fields filled":**

1. Open the run and look at the **Needs attention** tab, which lists who is
   incomplete and why.
2. Go to **Company/Employee → Manage Employee**, open each one, and fill in
   what's missing. Commonly: employee ID, IC and date of birth, marital status
   — and if married, whether the spouse works — and bank details.
3. Regenerate the draft run.

**"Company Info is missing: Employer name, Employer LHDN E-number, SSM
registration number, PERKESO employer code":** fill these in under
**Payroll → Settings → Form E (LHDN) → Employer details**, then submit again.

## When to contact support

If an employee's profile is complete and they still aren't included.
