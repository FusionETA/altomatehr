---
id: approvals-stuck-pending
title: A claim or leave request is stuck waiting for approval
module: approvals
audience: [employee, supervisor, admin]
kind: known-issue
status: open
severity: high
escalate: true
verified: 2026-09-28
symptoms:
  - My claim has been pending for days and nobody approved it
  - My supervisor says they can't see my claim to approve
  - My leave is still waiting for approval
  - I'm the approver but I don't have a Claims queue or Approvals tab
  - The approve button gives an error or access denied
  - Tuntutan saya masih belum diluluskan
  - Cuti saya masih menunggu kelulusan
  - Penyelia saya tak nampak tuntutan saya
  - claim stuck pending
keywords: [approve, approval, pending, stuck, queue, supervisor, approver, lulus, kelulusan, tertangguh, penyelia]
related: [claims-leave-menu-missing]
internal_ref: "runbook §1, issue 1"
---

## What's happening

Requests go to whoever sits above the employee in their team. But the approval
screens only open for people whose **role** is Supervisor. If the approver's
role was left as Employee, the request reaches them and they can't act on it,
so it waits forever. An admin can't approve it on their behalf either.

## What you can do

- **If you're an employee:** ask your HR admin to check that your approver's
  role is set to **Supervisor**. Your request doesn't need to be resubmitted.
- **If you're the approver and can't see an Approvals or Claims queue tab:**
  your role is probably still Employee. Ask your HR admin to change it to
  Supervisor.
- **If you're an admin:**
  1. Go to **Company/Employee → Manage Employee** and open the approver.
  2. Change **Role** to **Supervisor** and save.
  3. Ask them to sign out and back in. The waiting requests then appear in
     their queue.

  Do this whenever you seat someone as an approver in **Company/Employee →
  Company Structure**.

## When to contact support

If the approver's role is already Supervisor and the request still can't be
approved, or if it's urgent payroll-cutoff business.
