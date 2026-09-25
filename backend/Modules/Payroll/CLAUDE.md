# Modules/Payroll — context for Claude

Malaysian statutory payroll, ported from the Next.js monolith's
`modules/payroll/` (~34,600 LOC — the most domain-heavy module in that repo).
Reference source: https://github.com/FusionETA/ClaimGuard.

Country: Malaysia only. Currency: MYR only.

## Migration status

| Phase | Scope | State |
|---|---|---|
| 0 | Pure domain math: statutory tables, EPF, SOCSO/EIS/SKBBK, proration, OT pay | ✅ done |
| 1 | PCB engine (tax bands, reliefs, additional remuneration, zakat) | ✅ done |
| 2 | `PayrollSettings` + `PayrollCompanyInfo` — org config, first vertical slice | ✅ done |
| 3 | `PayrollRun` / `Payslip` / `PayslipLineItem` — generate + snapshot | ✅ done |
| 4 | `PayrollRunAdjustment` + `PayrollRunClaim` — survive regeneration | ✅ done |
| 5 | Run state machine: DRAFT → PENDING_APPROVAL → SUBMITTED, revert, staleness | ✅ done |
| 6a | `calcPcbBreakdown` — the LHDN form decomposition | ✅ done |
| 6b | Statutory files: EPF CSV, SOCSO/EIS TXT, PCB TXT + the readiness guard | ✅ done |
| 6c | Documents: payslip PDF, summary, payment schedule, bank file | ✅ done |
| 6d | The PCB calculation-details PDF (renders the 6a breakdown) | ⬜ **next** |
| 7 | Xero sync — needs manual journals + tracking categories on `IXeroClient` first | ⬜ |
| 8 | Loans, YTD import, employee import, annual EA/CP8D/Form E, portal payslips | ⬜ |

## Layers

Controller → Service → Repository, one direction only — the same house rule as
every other module. Files sit flat at the module root; only `Dtos/` and
`Entities/` are subfolders.

The statutory math is a set of **pure static classes** — `StatutoryTables`,
`EpfCalculator`, `PerkesoCalculator`, `PcbCalculator`, `PcbTaxBands`, `PcbReliefs`,
`PayPeriod`, `OvertimePay`, `SocsoSchemeAdvisor`, `Money`, and the `PayslipCalculator`
that composes them — in the same spirit as `AttendanceHoursMath` next
door. No EF Core, no `DbContext`, no HTTP, no clock: everything they need arrives
as a parameter, including the payroll period, so rerunning an old month
reproduces that month's law rather than today's. Services orchestrate them; the
math never reaches for data itself.

They are unit-tested in `backend.Tests/Payroll/`, and those tests are the
regression net for every phase above. Keep them green.

## Conventions

- **Money is `decimal`, never `double`.** Cent drift on a contribution is a filing
  discrepancy. `Money.Round2` rounds half away from zero — not .NET's default
  banker's rounding.
- **Statutory rates live in `StatutoryTables.cs`** with the circular that
  set them. Never inline a rate at a call site, and never "fix" a failing table
  test by editing the expectation — check the gazette PDF first.
- **Read the table, don't compute the percentage.** SOCSO, EIS and SKBBK are
  gazetted stepped tables whose values do not equal `rate × wage` (SKBBK band 5
  pays RM 0.90 where 0.75% × 140 would give RM 1.05).
- **Two divisors, two statutes.** `PayPeriod.WorkingDaysForPeriod` is the s.60I
  ordinary-rate basis (honours the org's CALENDAR/TWENTY_SIX setting) and drives
  the hourly rate. Proration uses s.18A, which is always calendar days and
  explicitly overrides s.60I. Do not collapse them.
- **Round once, at the end.** Keep the hourly rate unrounded through the OT
  multiplication.
- **Period-gate anything dated.** SKBBK started Jun 2026; a historical rerun must
  return 0, not today's phase.

## Tenancy

Every payroll table implements `ITenantScoped` and gets a global query filter in
`AppDbContext`. The Prisma originals key off `employeeProfileId` rather than
`organizationId` — porting that shape verbatim would be a tenant-isolation leak,
so each new table gets its own `OrganizationId` regardless of what the reference
schema does.

`PayrollSettings` and `PayrollCompanyInfo` are one row per org, so their tenant
column is a UNIQUE index rather than a plain one, and their repositories have no
id-based lookup — the tenant filter is the lookup.

## Generation is destructive

Pressing Generate **discards the run's payslips and line items** and rebuilds them
from the employees' current profiles. That is what makes a draft run safe to
re-run after fixing a profile — and why anything that must survive a
regeneration cannot live on a payslip alone. `PayrollRunAdjustment` and
`PayrollRunClaim` exist for exactly that: overtime hours, one-off pay and
attached reimbursements live there and are re-applied on every generation.

Both are DRAFT-only, and every mutation on either calls
`IPayrollRunRepository.MarkMutatedAsync` — the payslips on screen were built
from the inputs as they were, so an admin has to be told they are behind.
Generation clears it again.

**One-off rows go through the category catalogue, not around it.**
`PayrollRunAdjustments.Merge` folds a run's manual line items and fixed-allowance
overrides into the same list the calculator already prices, so a one-off
deduction obeys `ReducesBase`, the exemption ceilings and the six `SubjectTo*`
flags exactly as a recurring one does. An override keeps the original row's
CATEGORY — overriding the amount but losing it would silently move a travel
allowance into the EPF base.

Payslips **snapshot** what applied at generation: identity, salary, the EPF rates
the branch actually used, and each line item's `subjectTo*` flags. Raising
someone's salary in March must not rewrite what January paid them.

Only a DRAFT run can be generated, adjusted, or have claims attached.

## The status machine

```
DRAFT ──submit──▶ PENDING_APPROVAL ──approve──▶ SUBMITTED
  ▲                      │                          │
  └───────reject─────────┘                          │
  └──────────────────revert──────────────────────────┘
```

Two steps, two actors, recorded apart: `SubmittedForApprovalById` proposed the
month, `SubmittedById` put it live. An audit of a filing asks the second.

**SUBMITTED is the only status that feeds YTD.** `GetYtdByEmployeeAsync` reads
submitted runs only, and a payslip snapshot is immutable once filed. That single
fact is why:

- **Getting in is guarded.** Submission refuses an ungenerated run, a stale one,
  anyone taking home zero or less, and — the subtle one — a month submitted out
  of order. Submitting February before January freezes February's PCB, EPF and
  SOCSO against a zero year-to-date, and submitting January afterwards cannot go
  back and fix it. Both the "January is still a draft" and the "there is no
  January at all" cases are refused; an org's genuine first run is not.
- **Getting out cascades.** Reverting a month also reverts every later SUBMITTED
  month in the same year, because their YTD-cumulative figures were computed off
  it. `GetRevertImpactAsync` names them for the confirm dialog first.

A status change never touches `LastMutatedAt` — submitting a run does not make
its payslips any fresher.

## Statutory files

KWSP, PERKESO and LHDN parse by **byte position**. A field one character short
shifts every column after it, and the row is rejected — or silently misread
into someone else's account. So `StatutoryFileFields` is total: every helper
returns exactly `width` characters, truncating rather than overflowing, and the
tests assert exact slices rather than "contains".

The renderers are **pure functions over `StatutoryRunPayload`** — no EF, no
clock — which is what makes byte-exact tests possible. `StatutoryFileService`
does the loading.

- **Money comes from the payslip snapshot; identifiers are read LIVE.** What
  someone was paid is the month's filed figure and must never move. An EPF
  number is a current fact about a person, so a corrected typo should reach the
  next regeneration rather than being frozen into a bad submission.
- **One PERKESO renderer, two files.** v1 and v2 (SKBBK) differ by a single
  6-byte column carved out of filler. Both are offered, as the previous system
  did (`files/socso-eis` and `files/socso-eis-skbbk`) — the caller picks the
  layout; the period does not.
- **Additional PCB rides in CP39's PCB field.** `deduct_additional_pcb` lands
  in `Payslip.VoluntaryPcb`, never `Payslip.Pcb` (the MTD spec's X excludes
  it, so next month's formula must not see it). `PcbCp39Txt` files
  `Pcb + VoluntaryPcb`; CP38 keeps its own column. Run totals and the annual
  EA/CP8D figures use `Pcb` alone, as the previous system did.
- **Names and bytes match the previous system.** File names, run-document
  names (`Payroll_Summary_January_2026.pdf`, `Payslips_2026_01_All.zip`,
  `{id}_{name}_{MM-YYYY}.pdf`) and the edge-case field rules (untrimmed EPF
  number, null-coalescing SOCSO id, its tax-ref normaliser) are ported as
  they were, deliberately — "tidying" one changes what gets filed.
- **A missing IC is a refusal, not an exception.** It is an admin's data
  problem with a specific fix, so it returns a message naming the person and
  the controller answers 409.

## Documents

QuestPDF, following `Modules/LhdnForms/Pdf` conventions. Each renderer is a
pure function of a model the service builds, and exposes `Build` (the
`IDocument`) as well as `Render` (the bytes) — `Build` is what lets a document
be rasterised and LOOKED AT.

**Look at a rendered page before believing a document is finished.** The
compiler and the unit tests both passed on a payroll summary whose columns had
silently shifted by one and whose rows no longer reconciled; a single PNG made
it obvious.

Rules the documents keep:

- **A payslip's columns must sum to its net pay** — the figure that reaches
  the bank. `PayslipPdf.TotalDeductions` mirrors `PayslipCalculator`'s net-pay
  arithmetic exactly, and is tested against it.
- **Zakat and CP38 are inside `TotalDeductions`** and also have their own
  rows, so the catch-all "Other" nets them off rather than counting twice.
  **Additional PCB** (`VoluntaryPcb`) is too, but is shown INSIDE the
  "PCB / MTD" row (`Pcb + VoluntaryPcb`) rather than as its own row.
- **The summary is the previous system's layout** (A3 landscape): GROSS,
  the employee PCB/EPF/SOCSO/EIS/SKBBK band, NET, the employer
  EPF/SOCSO/EIS/HRDF band, COST — run totals under each heading — and every
  employee's base salary, overtime and line items itemised under their name
  (deductions in red), then the summary block. There is no "other deductions"
  column; non-statutory deductions reconcile through the itemised lines. Each
  employee's block is kept whole across a page break.
- **A payslip masks the bank account; the payment schedule shows it in full.**
  One is handed to an employee, the other exists so an approver can verify the
  destination before money moves.

## Bank file

`MalaysianBanks.Find` matches the free-text `EmployeeProfile.BankName` against
43 banks and their aliases. An unrecognised name returns **null**, and
`PayrollBankFileXlsx` REFUSES the whole file rather than dropping the row —
silently omitting someone means they are not paid and nobody notices until
they say so. (No account number at all is different: there is nothing to pay
into, so the row is skipped and the payment schedule flags it.)

Public Bank ECP quirks, each of which rejects the upload if got wrong: the
payment date must be a real Excel date, every amount is TEXT with exactly two
decimals (a numeric cell drops a trailing zero), and the footer `TOTAL:` row
is validated.

## The readiness guard

Submission is refused while the fields those files need are missing.
`PayrollRunReadiness` lives beside the generators because **they** define what
required means. Company Info needs all four of employer name, LHDN E-number,
SSM number and PERKESO code; each employee needs a payroll number and an IC
(or passport).

The income tax number is deliberately **not** in the gate — PCB computes
without one, and a new joiner waiting on a TIN must not hold up everyone
else's pay. `PcbCp39Txt` checks it at generation time instead, and only for
employees who actually had tax withheld.

## Loans

`PayrollLoans` (pure) + `EmployeeLoanService`. The schedule is POSITIONAL —
installment i is start + i months — and an installment is PAID when its month
has a SUBMITTED run. Nothing records payments, which is why reverting a month
un-pays it for free, and why every change below keeps money tied to its month.

- **Locked months never change.** A month with a SUBMITTED or
  PENDING_APPROVAL run is locked (`FirstEditableIndex`). A started loan cannot
  be re-termed wholesale; `Replan` keeps the locked prefix and re-spreads what
  is left (over N months, RM X a month, or typed month by month). Re-planning
  a started loan makes it CUSTOM.
- **RM 0 is a skipped month, and skips are calendar months.** `InsertSkipped`
  moves what was due into the next months that are NOT skipped, so a later
  skip or pause never drags an earlier one along. A trailing RM 0 is invalid.
- **PAUSED deducts nothing from `PausedFrom`**, and months under it are never
  "paid" whatever run exists. Resume writes the paused months in as RM 0 (the
  same `InsertSkipped`); cancelling a paused loan does the same for the paused
  months already filed, or they would read back as repaid.
- **Warnings are advisory**: repayments past the leaving date, and a month
  where the person's loans together exceed half the monthly salary.
- **A loan needs a real EmployeeProfile.** The payroll roster lists members
  without one under a stand-in id no payslip carries; `CreateAsync` refuses.

## Attendance and unpaid leave

`WorkedHours` / `ExpectedHours` come from `IHoursSummaryService`, overridable
per run by the admin. Two rules:

- **NORMAL minutes only.** `BeyondShiftMin` becomes money through an approved
  overtime submission, never here — counting it as worked hours as well pays it
  twice, once flat and once at the OT multiplier.
- **No attendance access → null, not zero.** For MONTHLY staff the figures are
  display-only, but for HOURLY staff `WorkedHours` IS the paid quantity, so a
  confident zero is a zero payslip.

Approved UNPAID leave is docked as its own `deduct_unpaid_leave` line (MONTHLY
only) rather than by shrinking the salary, so the payslip says why. The divisor
is the s.60I ordinary-rate basis — this is a rate question, so the org's
CALENDAR/TWENTY_SIX setting DOES apply, unlike proration.

## One formula, one implementation

`PcbCalculator.Explain` returns the deducted money AND every intermediate the
LHDN form names (Y, K, Y1, K1, Y2, K2, D, S, Du, Su, Q, C, QC, ΣLP, LP1, P, M,
R, B, Z, X, and the five AR steps). `Calculate` is a three-line read of it.

**Never compute the breakdown separately.** The reference has two routines —
`calcPcb` for the money, `calcPcbBreakdown` for the form — with a comment asking
whoever edits one to remember the other. They have already drifted: the form
subtracts a 2dp-rounded CS and the money an unrounded one, so its PDF can print
a PCB(C) a sen away from what left the employee's pay. `PcbBreakdownTests`
pins the agreement.

`Payslip.PcbCalculationJson` carries this snapshot, so a Detailed Calculations
PDF rendered years later shows the arithmetic that actually applied. Nullable
only for payslips generated before phase 6a.

## Not yet true (but will be)

Nothing on the payslip is a placeholder any more.

## Don't

- Don't compute PCB inline — call `PcbCalculator`.
- Don't add a second implementation of the PCB formula for display purposes.
  `Explain` already returns every intermediate a form needs.
- Don't let the org's `WorkingDaysRule` reach proration.
- Don't add a rate without a source comment.
- Don't inline a `subjectTo*` decision — read `PayrollAdjustmentCategories`.
- Don't put anything on a `PayslipLineItem` that has to survive a regeneration.
- Don't pay cash OT without checking the policy — `TIME_BANK` already credited
  the same hours as time off, and paying them again pays them twice.
- Don't mutate an adjustment or attachment without `MarkMutatedAsync`.
- Don't change anything a draft is built from OUTSIDE the run (an employee's
  payroll profile, a loan, unpaid leave, approved OT, payroll settings, salary
  history, YTD figures) without `IPayrollDraftStaleness` — otherwise the draft
  keeps yesterday's figures and can still be submitted. The UI calls this
  "Run payroll" / "Re-run payroll", as the previous system did.
- Don't let a status change touch `LastMutatedAt`.
- Don't revert a month without cascading to the later submitted months in its
  year — their year-to-date depends on it.
- Don't count `BeyondShiftMin` as worked hours.
- Don't build a statutory field by hand — use `StatutoryFileFields`, which
  cannot return the wrong width.
- Don't snapshot a statutory identifier onto the payslip; read it live.
- Don't throw for a missing employee field in a renderer — refuse with a
  message naming the person.
- Don't ship a document without rasterising a page and looking at it.
- Don't add a deduction to a payslip or summary without checking the row
  still reconciles to net pay.
- Don't guess at an unrecognised bank name.
