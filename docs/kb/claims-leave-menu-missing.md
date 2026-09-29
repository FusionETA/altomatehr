---
id: claims-leave-menu-missing
title: I can't see Claims or Leave, or I'm refused when I use them
module: settings
audience: [employee, admin]
kind: expected-behaviour
status: by-design
severity: low
escalate: false
verified: 2026-09-28
symptoms:
  - I don't have a Claims menu
  - I can't apply for leave
  - Access denied when I submit a claim
  - Leave option is missing for me
  - Saya tak boleh buat tuntutan
  - Menu cuti tiada
keywords: [claims, leave, menu, missing, access, denied, policy, module, tuntutan, cuti]
related: [approvals-stuck-pending]
internal_ref: "runbook §2, commit 8090831"
---

## What's happening

Each employee's policy decides which parts of AltomateHR they use —
attendance, claims, leave. If a module is turned off on your policy, you can't
use it.

## What you can do

- **If you're an employee:** ask your HR admin whether your policy includes
  Claims or Leave.
- **If you're an admin:** go to **System Settings → Policies**, edit the
  employee's policy, and under **Module access** tick **Claims** or **Leave**.

## When to contact support

If the module is ticked on the employee's policy and they're still refused.
