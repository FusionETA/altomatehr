---
id: lunch-break-setting-no-effect
title: Changing the company's default lunch break changes nothing
module: settings
audience: [admin]
kind: known-issue
status: open
severity: low
escalate: false
verified: 2026-09-24
symptoms:
  - I changed the lunch break but working hours didn't change
  - What is the Lunch (min) setting for
  - Default work schedule lunch doesn't work
  - Waktu rehat makan tengah hari tidak berkesan
keywords: [lunch, break, work schedule, working hours, default, rehat, makan tengah hari]
related: []
internal_ref: "runbook §1, issue 8"
---

## What's happening

The lunch break under **System Settings → Work Schedule → Default work
schedule** is saved, but isn't used in any calculation yet. Attendance uses
the lunch break on each employee's **shift** instead, and overtime currently
assumes an 8-hour working day.

## What you can do

- Set the lunch break on the shift: **Attendance → Shifts**, edit the shift,
  and set its lunch minutes. That's what attendance hours use.
- Keep the default schedule's lunch sensible anyway (usually 60), so nothing
  changes unexpectedly if it's used later.

## When to contact support

If your company's normal working day isn't 8 hours. Overtime rates may need
checking.
