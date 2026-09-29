---
id: cimb-passport-holder
title: CIMB bank file refused because an employee uses a passport
module: payroll-bank
audience: [admin]
kind: error-message
status: by-design
severity: medium
escalate: false
verified: 2026-09-24
symptoms:
  - CIMB file refused because of a passport
  - CIMB's bulk-payroll file identifies each employee by NRIC
  - I can't make the CIMB file for foreign workers
  - Fail CIMB ditolak kerana pasport
keywords: [CIMB, BizChannel, passport, foreign worker, NRIC, refused, pasport, pekerja asing]
related: [bank-file-refused]
internal_ref: "runbook §3 bank file refused"
---

## What's happening

CIMB's bulk payroll file identifies each employee by NRIC. It has no confirmed
way to identify a passport holder, so AltomateHR won't guess — a wrong code
could get the whole file rejected, or a payment misrouted.

## What you can do

- Pay passport-holding employees separately in CIMB BizChannel. Their account
  and amount are on the **Payment Schedule** report.
- Or confirm the correct passport code with CIMB and tell support, so it can
  be added.

This only affects companies whose payroll bank is CIMB. Other banks' files
include passport holders.

## When to contact support

If CIMB gives you the passport code, so the file can support it.
