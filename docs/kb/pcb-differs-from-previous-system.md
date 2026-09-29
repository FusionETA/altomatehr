---
id: pcb-differs-from-previous-system
title: PCB is different from the previous system or from last month
module: payroll
audience: [employee, admin]
kind: how-to
status: by-design
severity: medium
escalate: true
verified: 2026-09-24
symptoms:
  - My PCB is different from the old system
  - Why did my tax deduction change this month
  - PCB in AltomateHR doesn't match hr.altomate.io
  - My monthly tax is lower or higher than before
  - Potongan cukai bulanan saya berbeza
  - PCB saya lain dari sistem lama
keywords: [PCB, MTD, tax, income tax, deduction, different, previous system, cukai, potongan, LHDN]
related: [employee-missing-from-payroll-run, socso-shows-zero]
internal_ref: "runbook §3 v1 and v2 give different payroll figures"
---

## What's happening

PCB is worked out from the whole year, not just this month: pay so far, pay
expected for the rest of the year, reliefs, and tax already deducted. If any of
those differ between two systems, PCB differs — even when salary, EPF and
SOCSO match exactly.

The most common reasons:

- **Allowances from the previous system.** Some allowances, such as travel on
  official duty, are exempt from tax up to a yearly limit. AltomateHR applies
  that exemption to history imported from a previous system, so its PCB can be
  lower.
- **A previous employer earlier this year.** Income and tax from another
  employer this year count only if recorded on your profile with the year.
- **Marital status and reliefs.** Spouse and child reliefs change PCB.

## What you can do

- **If you're an employee:** ask your HR admin to check your marital status,
  reliefs and any previous employer this year. For your official figure, use
  LHDN's PCB calculator.
- **If you're an admin:**
  1. Compare each column. If only PCB differs, the cause is in the inputs
     above, not the calculation itself.
  2. Check the employee's **Previous employment (TP3)**: amounts only count
     with the **year** filled in.
  3. Check marital status, and whether the spouse works.
  4. If the run is a draft, regenerate it after any change.

## When to contact support

If the inputs all look right and the difference remains. Don't adjust PCB by
hand to match another system — ask support to confirm the correct figure
first.
