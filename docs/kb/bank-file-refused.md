---
id: bank-file-refused
title: The bank file won't download
module: payroll-bank
audience: [admin]
kind: error-message
status: by-design
severity: medium
escalate: false
verified: 2026-09-24
symptoms:
  - I can't download the bank file
  - Bank file error when I click download
  - No payroll bank is set
  - The Public Bank payor account number must be exactly 10 digits
  - No Maybank Corporate ID is set
  - No CIMB Organisation Code is set
  - Nothing to disburse
  - banks could not be matched to a recognised Malaysian bank
  - Fail bank tak boleh dimuat turun
keywords: [bank file, download, refused, error, payor account, organisation code, corporate ID, fail bank]
related: [bank-file-includes-cash-employees, cimb-passport-holder, imported-month-payslips-only]
internal_ref: "runbook §1, issue 10; §3 bank file refused"
---

## What's happening

AltomateHR checks the bank file before producing it, and refuses rather than
give you a file your bank would reject. The message names what's missing.
These settings are only checked when you download, not when you save them — so
check them well before payday.

## What you can do

Bank settings are under **Payroll → Settings → General → Payroll disbursement
bank**. (Some messages say "Company Info"; the settings are here.)

| Message says | Fix |
|---|---|
| No payroll bank is set | Choose your company's **Bank** — the one salaries are paid *from* |
| Payroll bank is set to "Other" | There's no upload file for your bank. Pay from the **Payment Schedule** report instead |
| Public Bank payor account number must be exactly 10 digits | Correct the **Account number** — Public Bank needs exactly 10 digits |
| No Maybank Corporate ID / No CIMB Organisation Code | Fill in **Organisation code** — issued by your bank when bulk payments were set up |
| No debiting / payor account number is set | Fill in **Account number** |
| Banks could not be matched to a recognised Malaysian bank | The named employee's bank is misspelled. Fix it in their profile under **Bank / payout** |
| Nothing to disburse | Everyone on the run has zero net pay or no bank account |
| This run has not been approved yet | Submit and approve the run first |
| Imported from your previous payroll system | Expected — see "Only payslips download for an imported month" |

## When to contact support

If your bank rejects a file AltomateHR produced, or the message isn't listed
here.
