---
id: ytd-import-button-disabled
title: The "Import 2026 history" button is greyed out
module: payroll-import
audience: [admin]
kind: expected-behaviour
status: by-design
severity: low
escalate: false
verified: 2026-09-23
symptoms:
  - I can't click Import 2026 history
  - The import button is disabled
  - Import button greyed out after choosing my file
  - Butang import tidak boleh ditekan
keywords: [import, button, disabled, greyed out, check the file, YTD]
related: [ytd-import-sheet-is-empty]
internal_ref: "runbook §3 Import button won't enable"
---

## What's happening

On purpose. Importing a year of history onto the wrong people would affect
everyone's tax for the rest of the year, so AltomateHR shows you who it matched
before it imports anything.

## What you can do

1. Choose your file.
2. Click **Check the file** and review the list of matched employees.
3. If the check is clean, **Import 2026 history** becomes clickable.

Choosing a different file clears the check, so click **Check the file** again.
If the check reports a problem, fix it in the file first — the most common is
an IC number that doesn't match the employee's profile.

## When to contact support

If the check comes back clean and the button still won't enable.
