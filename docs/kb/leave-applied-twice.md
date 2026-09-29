---
id: leave-applied-twice
title: The same leave dates were booked twice
module: leave
audience: [employee, supervisor, admin]
kind: known-issue
status: open
severity: medium
escalate: false
verified: 2026-09-28
symptoms:
  - I applied for the same leave twice by mistake
  - My leave balance dropped twice for one leave
  - Duplicate leave application
  - My annual leave was deducted double
  - Cuti saya ditolak dua kali
  - Tersilap mohon cuti dua kali
keywords: [leave, duplicate, twice, balance, deducted, overlap, cuti, dua kali, baki]
related: [approvals-stuck-pending]
internal_ref: "runbook §1, issue 5"
---

## What's happening

Leave doesn't yet stop someone applying twice for the same dates. Both
applications can be approved, and each one comes off the balance.

## What you can do

- **If you're an employee:** if one is still pending, cancel it from
  **Leave → My Leave**. If both are approved, ask your HR admin to cancel one.
- **If you're a supervisor:** before approving, check the employee doesn't
  already have leave on those dates.
- **If you're an admin:** cancel the duplicate from the admin **Leave**
  screen. Cancelling approved leave returns the days to the balance.

## When to contact support

If a duplicate was approved and the balance didn't come back after
cancelling.
