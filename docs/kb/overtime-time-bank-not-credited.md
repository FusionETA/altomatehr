---
id: overtime-time-bank-not-credited
title: Approved overtime isn't paid or added as time off
module: overtime
audience: [employee, admin]
kind: known-issue
status: open
severity: high
escalate: true
verified: 2026-09-28
symptoms:
  - My overtime was approved but it's not on my payslip
  - My OT isn't showing as time off
  - Overtime approved but not paid
  - Where did my overtime hours go
  - OT saya dah lulus tapi tak masuk gaji
  - Kerja lebih masa tidak dibayar
keywords: [overtime, OT, time bank, time off, time in lieu, not paid, kerja lebih masa, tak dibayar]
related: []
internal_ref: "runbook §1, issue 3"
---

## What's happening

A policy can pay overtime in cash, or bank it as time off instead. The time-off
option doesn't work yet: payroll correctly doesn't pay it in cash, but the time
is never added to the employee's balance either. Overtime approved under that
option is neither paid nor banked.

## What you can do

- **If you're an employee:** tell your HR admin which overtime is missing.
  Nothing is lost — it's recorded as approved — but it needs to be paid.
- **If you're an admin:** go to **System Settings → Policies**, edit the
  policy, and set **Overtime method** to **Cash (paid out)**. Overtime approved
  from then on is paid through payroll.

## When to contact support

For overtime already approved under the time-off option. It has to be paid by
hand for those periods, and support can help you find it.
