---
id: socso-shows-zero
title: SOCSO is zero on a payslip
module: payroll
audience: [employee, admin]
kind: known-issue
status: open
severity: high
escalate: true
verified: 2026-09-23
symptoms:
  - SOCSO is 0.00 on my payslip
  - No SOCSO deduction this month
  - The employer SOCSO is zero
  - PERKESO not deducted
  - SOCSO saya kosong
  - Tiada potongan PERKESO
keywords: [SOCSO, PERKESO, zero, 0.00, missing, not deducted, scheme, kosong]
related: [employee-import-updates-only]
internal_ref: "runbook §1, issue 6"
---

## What's happening

SOCSO is only calculated for employees who have a **SOCSO scheme** chosen on
their profile. If it's blank — most often after employees were brought in by
import — no SOCSO is calculated, and the payroll run doesn't warn you.

## What you can do

- **If you're an employee:** let your HR admin know. It's a setting on your
  profile, and they can fix it before the next run.
- **If you're an admin:**
  1. Go to **Company/Employee → Manage Employee** and open the employee.
  2. Under **SOCSO, EIS & SKBBK**, choose the SOCSO scheme and save.
  3. If the payroll run is still a draft, regenerate it.

  After any import, check this for **every** employee before running payroll.

## When to contact support

If a run has already been approved or filed with zero SOCSO. The missing
contributions need correcting with PERKESO.
