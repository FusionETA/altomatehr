---
id: bank-file-includes-cash-employees
title: An employee paid by cash or cheque also appears in the bank file
module: payroll-bank
audience: [admin]
kind: known-issue
status: open
severity: high
escalate: true
verified: 2026-09-28
symptoms:
  - Someone we pay in cash is in the bank file
  - An employee got paid twice
  - The bank file includes a cheque employee
  - Why is this person in the bank payment when they're paid in cash
  - Pekerja dibayar dua kali
  - Pekerja bayar tunai pun masuk fail bank
keywords: [bank file, cash, cheque, paid twice, double payment, duplicate, tunai, cek, dua kali]
related: [bank-file-refused]
internal_ref: "runbook §1, issue 2"
---

## What's happening

The bank file includes everyone with net pay and a bank account on file. It
doesn't yet check whether that person is actually paid by bank transfer. So if
an employee moved to cash or cheque but their old account is still on their
profile, they're included — and would be paid twice if you also hand over the
cash or cheque.

## What you can do

**Before you download the bank file:**

1. For each employee paid by cash or cheque, go to **Company/Employee →
   Manage Employee**, open them, and in **Bank / payout** remove the bank
   account.
2. Then download the bank file. They won't be in it.
3. Before uploading to your bank, check the file against the **Payment
   Schedule** report, which lists everyone and how much they're due.

## When to contact support

**Straight away if the file has already been uploaded or paid.** Recovering a
duplicate payment is a matter for a person and your bank, not the app.
