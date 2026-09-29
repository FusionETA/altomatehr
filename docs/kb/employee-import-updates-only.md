---
id: employee-import-updates-only
title: Importing employees doesn't add new people
module: employees
audience: [admin]
kind: expected-behaviour
status: open
severity: medium
escalate: false
verified: 2026-09-22
symptoms:
  - I imported employees but nobody new was added
  - No employee in this organization matches
  - Can I bulk upload employees
  - How do I add many employees at once
  - Macam mana nak tambah ramai pekerja sekali gus
keywords: [import, employees, bulk, add, create, onboarding, spreadsheet, pekerja, tambah]
related: [socso-shows-zero, employee-missing-from-payroll-run]
internal_ref: "runbook §1, issue 9"
---

## What's happening

The payroll employee import fills in payroll details — salary, EPF, SOCSO,
bank — for people who already exist in AltomateHR. It doesn't create new
employees, so a row for someone not yet added reports "No employee in this
organization matches".

## What you can do

1. Add each person first: **Company/Employee → Manage Employee → Add
   employee**. An employee ID is required.
2. Then run the import to fill in everyone's payroll details at once.
3. Afterwards, check every employee has a SOCSO scheme — the import can leave
   it blank.

## When to contact support

If you have a large number of employees to add and want help getting them in.
