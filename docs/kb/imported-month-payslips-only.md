---
id: imported-month-payslips-only
title: Only payslips can be downloaded for an imported month
module: payroll-import
audience: [admin]
kind: expected-behaviour
status: by-design
severity: low
escalate: false
verified: 2026-09-24
symptoms:
  - I can't download the EPF file for April
  - Where is the bank file for an imported month
  - The download list only shows payslips
  - Statutory files are missing for imported payroll
  - Kenapa hanya slip gaji boleh dimuat turun
keywords: [imported, history, EPF file, SOCSO file, PCB file, bank file, download, payslips only]
related: [bank-file-refused, ytd-import-sheet-is-empty]
internal_ref: "runbook §2, commits 12d0ec9 and 4166ed0"
---

## What's happening

On purpose. A month imported from your previous payroll system was paid and
filed by that system. AltomateHR only stores the figures, so it offers the
payslips, but not the bank file, EPF, SOCSO or PCB files, or run reports.
Producing those again could mean paying people twice or filing the same
return twice.

## What you can do

- Download payslips as usual from **Download files** on the run.
- For that month's statutory files and bank records, use your previous payroll
  system.
- Months you run in AltomateHR offer every file as normal.

## When to contact support

If a month you ran in AltomateHR, not imported, only offers payslips.
