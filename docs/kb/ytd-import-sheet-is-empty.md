---
id: ytd-import-sheet-is-empty
title: YTD import says "The sheet is empty"
module: payroll-import
audience: [admin]
kind: error-message
status: by-design
severity: medium
escalate: false
verified: 2026-09-24
symptoms:
  - The sheet is empty when I import 2026 payroll
  - This file cannot be imported yet
  - My YTD file won't import
  - The history import can't read my file
  - Fail YTD saya tidak boleh diimport
keywords: [YTD, import, year to date, history, sheet is empty, template, xlsx]
related: [ytd-import-button-disabled, imported-month-payslips-only]
internal_ref: "runbook §3 YTD import says: The sheet is empty"
---

## What's happening

The importer looks for a header row containing an employee name column
("Employee Name" or "Full Name") and "Basic Salary". If it can't find one in
any sheet of the file, it reports the sheet as empty — even when the file has
data.

## What you can do

1. Go to **Payroll → Payroll runs** and, under **Import earlier 2026 payroll**,
   click **Template for 2026**. Your employees are already listed in it.
2. Fill in the month rows, keeping the header row and column names as they
   are.
3. Upload it, then click **Check the file**.

Files exported from the previous AltomateHR system, with an Instructions sheet
first, are also accepted.

## When to contact support

If a file made from the template, or exported from the previous system, still
shows this message.
