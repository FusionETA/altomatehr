# Payroll migration — phase plan & handoff

Porting the Next.js monolith's `modules/payroll/` (~34,600 LOC) to this ASP.NET
Core backend. Malaysia only, MYR only.

**Read this first when starting cold.** Module conventions live next door in
[CLAUDE.md](./CLAUDE.md); this file is the plan and the running state.

---

## Setup for a new session

The reference app is **not in this repo** — the path in the root `CLAUDE.md`
(`/Users/chenzirong/Documents/globe-engineering-claim`) does not exist.

⚠️ **Take a FRESH clone. Do not trust `~/Empty/ClaimGuard`.** That copy is
hundreds of commits behind and carries uncommitted local edits, and reading it
sent one session's work off the current design. Clone somewhere scratch
**outside this repo** and read from `origin/main`:

```bash
git clone --depth 1 https://github.com/FusionETA/ClaimGuard.git /tmp/cg-ref
```

Every phase below needs it. The payroll source is at `modules/payroll/`, the
schema at `prisma/schema.prisma`.

Verify the current state builds and passes before changing anything:

```bash
cd backend && dotnet build && cd ../backend.Tests && dotnet test
```

Expect **0 errors** and **1,338 passing** (2 pre-existing warnings, in
`OvertimeService.cs` and `SalaryChangeService.cs`, unrelated to payroll).

---

## Status

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
| 6d | The PCB calculation-details PDF (renders the 6a breakdown) | ✅ done |
| 7 | Xero sync — manual journals, tracking, the balanced journal | ✅ done |
| 8a | Staff loans — schedules, and the deduction reaching a payslip | ✅ done |
| 8b | The employee portal: my payslips, my payslip, my PDF | ✅ done |
| 8c | Annual filings: Form EA, CP8D, Form E | ✅ done |
| 8d | YTD import — seeding a mid-year migration | ✅ done |
| 8e | Employee import — on the house `Common/Tabular` machinery | ✅ done |
| 8f-1 | `SalaryChange` + mid-cycle hints — **the correctness gap** | ✅ done |
| 8f-2 | Portal credentials + the past-leaver sweep | ✅ done |
| UI-1 | Admin surface: Overview · Runs · Loans · Annual forms · Settings | ✅ done |
| UI-2 | Run detail: payslips, adjustments, claims, downloads, the status machine | ✅ done |
| UI-3 | **Employee payslip portal** | ⬜ **not started** |

**923 payroll tests** of the 1,338 total, all in `backend.Tests/Payroll/`.

---

## Picking this up cold — what is NOT done

The sections below are a running log, appended as each phase landed. This is
the short version of what is still open, so nobody has to read 2,000 lines to
find it.

### Not built

1. **The employee payslip portal.** The backend is finished —
   `PayslipsController` serves list, detail and PDF at `/payslips`, and
   `EmployeePayrollService` already returns only SUBMITTED runs. The UI is
   still a placeholder: `EmployeeShell.tsx` renders `<EmptyModule title="Payslips">`.
   Worse, the employee dashboard has a **"Latest payslip"** card that links to
   it and says *"No payslips yet — they'll appear here once payroll finalises
   your first run"*, hardcoded. That sentence becomes false the moment a run is
   approved. **This is the biggest remaining gap and the only user-facing lie.**
2. **Three bank formats.** Only Public Bank ECP renders. The reference also has
   Maybank M2E, CIMB BizChannel and Hong Leong Connect. The Settings bank
   dropdown deliberately offers only Public Bank and "Other bank (no upload
   file)" so it cannot promise a file that never arrives.
3. **PCB borne by the employer (gross-up).** Not implemented — see the note at
   the head of `PcbCalculator`. The profile toggle exists and must stay off.
4. **Form E / CP8D do not print the declarant or tax agent.** Those fields are
   now collectable in Settings → Form E, and stored, but no renderer reads
   them. Neither does the reference's.
5. **Xero preview and manual re-sync.** `getXeroPreview` / `syncPayrollToXero`
   are typed in `api.ts` and unused. Sync-on-approval works on its own.
6. **HRDF tier and the live SKBBK table.** The reference derives the Part I /
   Part II tier from the Malaysian headcount and renders the gazette table;
   ours takes the rate as an input and states the phase statically. Both would
   need the figures exposed over the API.
7. **Hand-varied loan schedules.** `SaveEmployeeLoan.schedule` is accepted by
   the server; the form only offers equal-split and fixed-amount.
8. **EmploymentStint / EmployeeTransfer.** Judged out of payroll scope. Needs
   scoping separately.

### Never verified

- **decimal-vs-float parity against a real production payslip.** The engine is
  tested against LHDN's own worked examples, but no figure has been compared
  end to end with what the Next.js app pays the same employee.
- **The approved-run download path.** Files are correctly refused on a draft
  (tested), but no run in the dev org could be approved, so "approve → the
  Documents section appears → the files download" has not been walked.

### Environment gotchas

- **User-secrets are required**, and one is new: `Jwt:Key`,
  `ConnectionStrings:Default`, and `Secrets:PortalCredentialsKey`. `SecretBox`
  **refuses to start in production** without the last one, by design — portal
  passwords would otherwise be stored unprotected.
- **Eleven EF migrations** ship with this work. `dotnet ef database update`.
- **This org's stored Xero refresh token cannot be decrypted** — its
  data-protection key is no longer in the ring. `/xero/status` still reports
  connected, but anything needing the token (tracking categories) returns
  empty. Reconnect Xero to fix.
- **Claims will never reach payroll until the settlement route is switched.**
  The org is on `XERO_BILL`; the route is stamped on each claim AT CREATION, so
  changing it under Claims → Settings only affects claims submitted afterwards.
- **Test data was written into the dev org** while verifying: employer name and
  registration numbers, the Public Bank payor account `3161234567`, and a
  declarant name. All placeholders — overwrite them.

---

## Loose ends

### From phase 2 — still open

1. **Verification rows in the dev database.** An end-to-end check wrote real rows
   under a fake org. Harmless — the tenant filter hides them from every real org
   — but they are junk:

   ```sql
   DELETE FROM PayrollSettings WHERE OrganizationId = 'org-phase2-verify';
   DELETE FROM PayrollCompanyInfos WHERE OrganizationId = 'org-phase2-verify';
   ```

2. **Decimal-vs-float parity is unverified against a real payslip.** See
   "Known risks" below. Phase 3 now produces whole payslips, so this is the
   next thing worth closing — nothing here has been checked against a real one.

### Found during phase 3

3. **MIGRATION.md claimed a net-pay floor that the reference does not have.**
   The old phase-3 section listed "net pay has a floor (EA 1955 s.24)" and a
   `netShortfall` column as an invariant. Neither exists in the reference —
   `grep -r netShortfall` over `modules/` and `prisma/` finds nothing, and
   `calcPayslip` lets net go negative. So it was **not** implemented, and the
   claim has been removed rather than built on. Whether EA s.24 actually
   requires a floor is a real question worth answering, but it is a NEW rule,
   not a port, and needs a decision before anyone writes it.

---

## What exists now

### Pure math (phases 0–1)

All flat at the module root, all `static`, all `decimal`. No EF, no HTTP, no
clock — everything arrives as a parameter, **including the payroll period**, so
rerunning an old month reproduces that month's law rather than today's.

| File | Holds |
|---|---|
| `StatutoryTables.cs` | SOCSO Act 4 + EIS Act 800 (65 gazette rows each), KWSP Third Schedule band rule, SKBBK phase schedule |
| `EpfCalculator.cs` | Branch resolver (Parts A/C/E/F) + contribution money |
| `PerkesoCalculator.cs` | SOCSO with the Cat 1→2 age flip, SKBBK opt-in, EIS |
| `PcbCalculator.cs` | Non-resident flat rate, resident annualisation, AR tax-delta, K-decomposition EPF projection, MTD rounding |
| `PcbTaxBands.cs` | Progressive bands + RM 400/800 rebate, `FindBand` for the LHDN form's {M, R, B} |
| `PcbReliefs.cs` | D / DU / S / SU / QC reliefs, EPF RM 4,000 and PERKESO RM 350 caps |
| `PayPeriod.cs` | s.60I working-days basis + s.18A incomplete-month proration |
| `OvertimePay.cs` | Hourly-rate derivation, daily hours, OT pay |
| `SocsoSchemeAdvisor.cs` | Age/citizenship scheme recommendation |
| `Money.cs` | `Round2`, `Trunc2`, `CeilRinggit` |

### Vertical slice (phase 2)

`PayrollSettings` (operational rules) and `PayrollCompanyInfo` (filing identity),
each one row per org. Entities → repositories → services → `PayrollController`,
DI in `Program.cs`, audit actions in `Modules/Audit/AuditActions.cs`, migration
`20260910061705_AddPayrollSettingsAndCompanyInfo` **already applied** to the dev DB.

Endpoints (all `[Authorize(Roles = "Admin,Owner")]`):

```
GET  /payroll/settings        PUT /payroll/settings
GET  /payroll/company-info    PUT /payroll/company-info
```

### Runs and payslips (phase 3)

`PayrollRun` → `Payslip` → `PayslipLineItem`, all `ITenantScoped` with their own
`OrganizationId` and their own global query filter. Migration
`20260910070920_AddPayrollRunsAndPayslips` **already applied** to the dev DB.

| File | Holds |
|---|---|
| `PayslipCalculator.cs` | The orchestrator — the port of `calcPayslip`. Owns no statutory arithmetic; decides which wage each agency sees |
| `PayrollAdjustmentCategories.cs` | The 46-category catalogue and its six `subjectTo*` flags, exemption ceilings, and routing flags |
| `Entities/FixedAllowance.cs` | The JSON shape stored in `EmployeeProfile.FixedAllowancesJson` |
| `PayrollRunRepository.cs` | Runs, keyed by (org, period) |
| `PayslipRepository.cs` | Payslips + line items, the destructive `ReplaceForRunAsync`, and the batched YTD read |
| `PayrollRunService.cs` | Create a run, generate every payslip, roll up the totals |

Endpoints (all `[Authorize(Roles = "Admin,Owner")]`):

```
GET  /payroll/runs            GET  /payroll/runs/{id}
POST /payroll/runs            POST /payroll/runs/{id}/generate
```

Two seams were added outside this module to feed it:
`IEmployeeProfileRepository.GetAllForCurrentOrgAsync` and
`IDirectoryService.GetProfilesForCurrentOrgAsync` — payroll reads the roster
through the shared directory rather than reaching into Employees' repository.

### Per-run inputs (phase 4)

`PayrollRunAdjustment` (what an admin typed) and `PayrollRunClaim` (which
approved claims are being reimbursed through pay), both `ITenantScoped` with
their own `OrganizationId` and filter. They exist because generation is
destructive. Full detail in **Phase 4 — done** below.

---

## Rules that must not be broken

These are the ones that cost the reference app real production defects.

**Money is `decimal`, never `double`.** `Money.Round2` rounds half away from zero
— not .NET's default banker's rounding, which under-collects on half-sen amounts.

**Statutory rates live in `StatutoryTables.cs`, with the circular that set them.**
Never inline a rate at a call site. Never "fix" a failing table test by editing
the expectation — check the gazette PDF first. These numbers are law.

**Read the table, don't compute the percentage.** SOCSO, EIS and SKBBK are
gazetted stepped tables whose values are not `rate × wage`. SKBBK band 5 pays
RM 0.90 where 0.75% × 140 would give RM 1.05.

**Two divisors, two statutes.** `PayPeriod.WorkingDaysForPeriod` is the s.60I
ordinary-rate basis (honours the org's CALENDAR/TWENTY_SIX setting) and drives
the hourly rate. Proration uses s.18A, which is **always calendar days** and
explicitly overrides s.60I ("Notwithstanding section 60I"). Letting the org
setting reach proration paid one employee 0.8077 of salary for missing one day.

**Round once, at the end.** Keep the hourly rate unrounded through the OT
multiplication. Double-rounding PCB pushes 155.99 to 156.05 instead of 156.00.

**Period-gate anything dated.** SKBBK started Jun 2026; a rerun of an earlier
month must return 0, not today's phase.

**Every payroll table implements `ITenantScoped`** and gets a global query filter
in `AppDbContext.OnModelCreating`. ⚠️ The Prisma originals key off
`employeeProfileId`, **not** `organizationId` — porting that shape verbatim is a
tenant-isolation leak. Give every new table its own `OrganizationId` regardless
of what the reference schema does.

**A GET must not write.** Unconfigured orgs get defaults with
`isConfigured: false`; creating a row on read destroys that signal.

---

## Phase 3 — done

`PayrollRun` / `Payslip` / `PayslipLineItem`, `PayslipCalculator`, the
`PayrollAdjustmentCategories` catalogue, repositories, `PayrollRunService`,
`/payroll/runs`, migration applied. 92 new tests
(`PayslipCalculatorTests`, `PayrollRunServiceTests`).

### Where the port deliberately diverges from the reference

Three places. Each is a fix, not an accident — do not "restore" them.

1. **Proration is calendar days over calendar days.** The reference's
   `effectiveWorkedDays` counts a weekday roster against the ÷26 basis under the
   TWENTY_SIX rule. s.18A opens "Notwithstanding section 60I" precisely to
   override that basis, so `PayPeriod.EffectiveWorkedDays` takes the CALENDAR
   length and `PayslipCalculator` divides by it. `TotalWorkingDays` (s.60I) is
   kept for the hourly rate only. The payslip stores all three day figures —
   `TotalWorkingDays`, `ProratedDays`, `ProrationDaysInPeriod` — so the pair that
   forms the factor is unambiguous on the row itself.
2. **`arEpfBase` is gone.** The reference declares it, passes it to `calcEpf` and
   into the snapshot, and never increments it — it is always 0. The cliff work is
   done entirely by `rateDeterminingWage`, so the port passes no separate AR wage
   and the behaviour is identical.
3. **The TP1 category list is derived, not restated.** The reference hardcodes
   the eight codes in `payslip.repository.ts` with a "keep this in sync" comment.
   `PayslipRepository.Tp1Categories` reads them off
   `PayrollAdjustmentCategories` instead, so a new TP1 sub-category cannot
   silently drop out of ΣLP.

Two other notes for whoever reads the reference next: the childcare exemption is
RM 2,400 in the reference's own category metadata, though one test's title says
RM 3,000 — the metadata is right (LHDN PR 5/2019 §7.2.4) and the title is stale.
And `wages_expense_claim` is `kind: ALLOWANCE` there, not REIMBURSEMENT, with
every flag false; ported verbatim.

### Deliberately deferred out of phase 3

- **OT hours and multipliers.** OT pay is wired end to end and tested, but the
  hours arrive on `PayrollRunAdjustment` (phase 4), so the service currently
  passes zero hours and the calculator's statutory default multipliers.
  Phase 4 supplies both together, resolving the rates from the employee's policy
  via `IPolicyService.GetEffectivePolicyAsync`. Wiring the rates alone now would
  have been N queries for a value that multiplies zero.
- **`Payslip.PcbCalculationJson`.** Column exists, nullable, always null. Phase 6
  fills it — the column is here now so that phase needs no migration.
- **Attendance hours.** `WorkedHours` / `ExpectedHours` / `UnpaidLeaveDays` are
  plumbed through the calculator and persisted, but nothing populates them yet
  (`HoursSummaryService` is the source, and it belongs with phase 4's adjustment
  row). Monthly pay is day-based, so they are display-only anyway — except for
  HOURLY staff, where `WorkedHours` IS the paid quantity.

---

## Phase 4 — done

`PayrollRunAdjustment` + `PayrollRunClaim`, the merge into the calculator, and
policy-driven overtime. Migration `20260910074932_AddPayrollRunAdjustmentsAndClaims`
**already applied** to the dev DB. 65 new tests.

| File | Holds |
|---|---|
| `Entities/PayrollRunAdjustment.cs` | One row per (run, employee): OT hours, manual rows, overrides, attendance overrides, notes |
| `Entities/PayrollRunClaim.cs` | One row per attached claim, `ClaimId` globally unique |
| `Entities/ManualLineItem.cs` | The JSON shape in `ManualLineItemsJson` |
| `Entities/FixedAllowanceOverride.cs` | The JSON shape in `FixedAllowanceOverridesJson` |
| `PayrollRunAdjustments.cs` | Pure: parse the JSON columns, apply overrides, merge one-off rows into the fixed list |
| `PayrollPeriodLabel.cs` | Extracted from `PayrollRunService` so the audit trail and the run list spell a month the same way |
| `PayrollRunAdjustmentService.cs` | Save/clear, DRAFT-gated, category-validated |
| `PayrollRunClaimService.cs` | Attach/detach and the attachable list |

Endpoints (all `[Authorize(Roles = "Admin,Owner")]`, all on `PayrollRunsController`):

```
GET    /payroll/runs/{id}/adjustments
GET    /payroll/runs/{id}/adjustments/{employeeProfileId}
PUT    /payroll/runs/{id}/adjustments/{employeeProfileId}
DELETE /payroll/runs/{id}/adjustments/{employeeProfileId}
GET    /payroll/runs/{id}/claims
GET    /payroll/runs/{id}/claims/attachable
POST   /payroll/runs/{id}/claims
DELETE /payroll/runs/{id}/claims/{claimId}
```

Two more cross-module seams, in the same spirit as phase 3's:
`IPolicyService.GetEffectivePoliciesForEmployeesAsync` (batched OT multipliers —
the per-employee call was two queries a head) and
`IClaimsService.GetPayrollReimbursableAsync` (what counts as reimbursable,
defined once; `ExportPayrollReimbursementsAsync` now reads it too, so the export
and the attach list cannot drift apart).

`PayrollRun.LastMutatedAt` is now actually written, so `PayrollRunDto.IsStale`
means something for the first time.

### Where the port deliberately diverges from the reference

Two more. As with phase 3's three, each is a fix — do not "restore" them.

4. **Overtime keeps the engine's dedicated path instead of becoming
   `wages_overtime` line items.** The reference converts the adjustment's OT
   hours into line items and zeroes `calcPayslip`'s own OT inputs. Both routes
   reach the same four answers — `wages_overtime` carries
   `SubjectToEpf=false, Socso=true, Eis=true, Pcb=true (AR)`, which is exactly
   what the engine's OT path does — but the reference rounds each of the three
   day-type buckets separately before summing, and this module's standing rule
   is to keep the hourly rate unrounded through the multiplication and **round
   once, at the end**. The dedicated path also fills `Payslip.OtPay` and the
   three hour columns from the same computation, where the reference has to
   copy the hours across by hand and comment that `result.ot*Hours` is always 0.
   Consequence to know: OT does not appear as its own payslip LINE. If phase 6's
   PDF needs one, derive it from `OtPay` and the hour columns — do not re-emit
   it as a category row, or it will be counted twice.

5. **No policy no longer means no overtime.** The reference's `cashOt` requires
   a policy (`policyForOt !== null && otEnabled && otMethod === "CASH"`), so an
   org with no policy configured pays nothing for hours an admin explicitly
   typed — an underpayment with nothing on the payslip to explain it. Here a
   policy can still switch cash OT off two ways (disabled, or `TIME_BANK`, which
   already credited the same hours as time off), but its ABSENCE cannot: EA 1955
   s.60A is a floor an employer does not opt into by configuring a policy, so
   the calculator's statutory defaults (1.5× / 2× / 3×) apply.

### Deliberately deferred out of phase 4

- **Leave cash-out.** The reference's run page can cash out expired
  carry-forward leave as a `wages_leave_pay` manual row
  (`listPendingLeaveCashoutsForRun` / `attachLeaveCashoutToRun`). It needs
  `LeaveEntitlement.carriedExpiredDays`, which this app's leave module does not
  track yet, and `ManualLineItem.SourceEntitlementId` — the backlink the
  reference uses to detach without label-matching — is deliberately NOT on the
  shape yet, since a field nothing writes is worse than one that is missing.
- **Attendance-derived hours.** `WorkedHours` / `ExpectedHours` are still only
  what an admin typed. `HoursSummaryService` is the source, and wiring it is
  phase 5's job alongside the auto unpaid-leave deduction.
- **Loan installments.** The reference pushes a `deduct_loan_repayment` row into
  the same merged list at generation time. Loans are phase 8; the merge point is
  ready for them (`PayrollRunAdjustments.Merge`).

### The trap, kept shut

`FixedAllowanceOverrides` is keyed by the fixed-allowance's **array index as a
string**. `PayrollRunAdjustments.ApplyOverrides` uses a record `with` so the
original row's CATEGORY survives an amount override — losing it would silently
move a travel allowance into the EPF base. An index past the end of the array is
ignored rather than throwing, which is the safe direction. But the key is still
POSITIONAL: reordering an employee's fixed allowances re-points every override
on every draft run, and nothing detects that. If phase 5+ ever gives fixed
allowances a stable id, re-key these off it.

---

## Phase 5 — done

The run status machine, plus the attendance and leave figures that feed a run.
Migration `20260910081503_AddPayrollRunApprovalTrail` **already applied** to the dev
DB. 40 new tests, mostly in `PayrollRunStateMachineTests`.

```
DRAFT ──submit──▶ PENDING_APPROVAL ──approve──▶ SUBMITTED
  ▲                      │                          │
  └───────reject─────────┘                          │
  └──────────────────revert──────────────────────────┘
```

New columns on `PayrollRun`: `SubmittedForApprovalAt/ById`, `SubmittedAt/ById`,
`ApprovalRejectionReason`. Proposer and approver are recorded apart — an audit
of a filing asks who put the month LIVE, not who prepared it.

Endpoints (all `[Authorize(Roles = "Admin,Owner")]`):

```
POST   /payroll/runs/{id}/submit     POST /payroll/runs/{id}/approve
POST   /payroll/runs/{id}/reject     POST /payroll/runs/{id}/revert
GET    /payroll/runs/{id}/revert-impact
DELETE /payroll/runs/{id}
```

### The submit guards — the point of the phase

`SUBMITTED` is the only status `GetYtdByEmployeeAsync` reads, and a payslip
snapshot is immutable once filed. Every guard follows from that:

1. **Generated.** An empty run cannot be finalised.
2. **Not stale.** `LastMutatedAt` newer than the newest payslip means the
   figures about to be filed are not the ones the inputs imply.
3. **Nobody takes home nothing.** Net ≤ 0 is refused, naming up to five people.
4. **In chronological order.** Two cases, one fix. The prior month exists but is
   not submitted → block. There is no prior-month run at all AND the org has an
   earlier submitted one → block, because that is a gap, not a first run. An
   org's genuine first run in any month is allowed.

Deferred: the reference's **statutory-readiness guard** (`payroll-readiness.service`)
blocks a submit when Company Info or an employee is missing a field the document
generators need. Those generators are phase 6, and what "required" means is
defined by them — so the guard belongs there, not here as a guess. The payslip's
`StatutoryWarnings` column already records missing TIN / EPF / SOCSO numbers and
is the natural input to it.

### Revert cascades

Reverting a month reverts every later SUBMITTED month in the same year, because
their YTD-cumulative figures (PCB annualisation, the SOCSO/EIS relief) were
computed off it. `GetRevertImpactAsync` names them for the confirm dialog
BEFORE the admin commits. A different year is untouched.

Payslips and attachments survive a revert — the point is to let an admin fix a
filed month, not to start it over.

### Attendance and unpaid leave

Two more cross-module seams, both deliberately narrow:

- `IHoursSummaryService.GetHoursForEmployeesAsync(userIds, from, to)`. NOT
  `GetOrgHoursSummaryAsync`, which is a reporting view that picks its own roster
  and drops Admin/Owner accounts — payroll pays whoever has an employment
  record, so it names the roster itself.
- `ILeaveService.GetApprovedUnpaidDaysForOrgAsync(from, to)`. NOT
  `GetApprovedDaysInRangeAsync`, which sums a whole application whenever it
  merely OVERLAPS the range. That is fine for "was this person away" and wrong
  for pay: a 28 Jan – 6 Feb absence would be docked in full from January AND
  again from February. The new one CLIPS the range and recounts against the same
  working-day calendar, so each day is charged exactly once.

`WorkedHours` uses NORMAL minutes only — `BeyondShiftMin` becomes money through
an approved overtime submission, and counting it here too would pay it twice.
No attendance access on the policy yields null rather than zero, because for an
HOURLY employee a confident zero is a zero payslip.

Unpaid leave is docked as its own `deduct_unpaid_leave` line for MONTHLY staff
(never HOURLY — they are paid for hours worked, so the absence is already
absent). `PayPeriod.UnpaidLeaveDeduction` uses the s.60I basis: what one day of
pay is WORTH is a rate question, so the org's CALENDAR/TWENTY_SIX setting
legitimately applies here, unlike s.18A proration.

### Deliberately deferred out of phase 5

- **The readiness guard**, as above — it belongs with phase 6's generators.
- **Xero posting on approve.** The reference fires a manual journal from
  `approvePayrollRunCore`, best-effort, never blocking the approval. `IXeroClient`
  has no manual journals yet, so approval simply flips the status. When phase 7
  adds them, hook in at `ApproveAsync` and keep it non-blocking — an approval
  already made must not be undone by an accounting integration being down.
- **Cached report invalidation.** The reference clears `payrollRunReport` and
  `payrollAnnualReport` rows on approve and revert. Neither table exists here
  yet; they arrive with phase 6/8. **When they do, revert and approve must clear
  them** — a cached PDF of a reverted month is a wrong document that looks right.

---

## Phase 6a — done

`calcPcbBreakdown`, deferred from phase 1. No migration — `Payslip.PcbCalculationJson`
has been waiting since phase 3. 24 new tests in `PcbBreakdownTests`.

Phase 6 was split because it is roughly phases 3+4 combined:

| Slice | Scope |
|---|---|
| **6a** ✅ | The LHDN form decomposition. Everything else in phase 6 reads it. |
| **6b** | EPF CSV, SOCSO/EIS(/SKBBK) TXT, PCB TXT, and the readiness guard. |
| **6c** | Payslip PDF, payroll summary, payment schedule, bank file. |

### The design decision

The reference implements the formula TWICE — `calcPcb` for the money,
`calcPcbBreakdown` for the form — with a comment asking whoever edits one to
remember the other. **They have already drifted.** `calcPcb` computes PCB(C)
from the unrounded annual tax; `calcPcbBreakdown` uses a 2dp-rounded CS
(`pcb.ts:1058`, which admits it: the rounding is there "so the formula printed
on the LHDN PDF reconciles"). So the reference's PDF can print a PCB(C) a sen
away from the sum actually withheld.

This port has ONE computation. `PcbCalculator.Explain` returns a `PcbBreakdown`
carrying both the intermediates and the deducted figures; `Calculate` reads
three fields off it and is otherwise unchanged, so all 87 pre-existing PCB
tests kept passing untouched.

The rounded CS was adopted (it is the value the form prints, so the printed
subtraction has to work) — and the four LHDN worked examples plus the RM 108.20
TP1 case still reproduce EXACTLY, which is what settled it. Had they moved, the
unrounded value would have won; the single-path invariant holds either way.

### What the breakdown carries

`PcbBreakdown` is a flat record with a `Formula` discriminator rather than a
polymorphic hierarchy — it is an audit snapshot that must deserialise years
from now, and a plain shape survives that better than type resolution.

Resident: Y, K, Y1, K1, Y2, K2, N · D, Du, S, Su, Q, C, QC · ΣLP, LP1 · P, M, R,
B · Z, X · YearlyTax, CurrentMonthPcb · `Ar` (nullable) · the three deducted
figures. Non-resident: the flat rate and the two amounts, nothing else — a band
or relief reported there would imply the 30% came from somewhere it did not.

Two presentation notes worth keeping:

- **Q and C are display-only.** The form shows child relief as "Q × C", but real
  children do not share one rate (RM 2,000 / 8,000 / 16,000). Q is the standard
  amount, C is QC ÷ Q (possibly fractional), and **QC is what the arithmetic
  uses**.
- **Kt vs KtEffective.** Kt is the EPF actually contributed on the bonus;
  KtEffective is how much of it still fits under the RM 4,000 annual cap, which
  is often 0 once the normal projection has claimed the budget. The chargeable
  figure uses KtEffective, and the form shows both — printing only Kt would make
  P look wrong to anyone subtracting the number on their payslip.

### Verified against LHDN's own intermediates

The spec (pages 45–50) prints more than the final ringgit, and the tests now
pin those too: K2 = 308.63, P = 47,000.07, band {M = 35,000, R = 6%, B = 600}.
The April bonus month reconciles step by step — CS = (P₂ − M₂)R₂ + B₂,
PCB(B) = X + trunc2(monthly) × (N+1), PCB(C) = CS − PCB(B) − Z = 757.50.

**PCB(B) uses the TRUNCATED monthly figure, not the thresholded PCB(A).** The
spec writes "PCB(A) × (n+1)" but means the truncated value; using the
thresholded one would drop a whole year's projection whenever the monthly
deduction sits under RM 10.

---

## Phase 6b — done

The three monthly submission files and the readiness guard phase 5 deferred.
No migration — every field already existed. 30 new tests.

| File | Holds |
|---|---|
| `StatutoryFileFields.cs` | Total field formatting — always exactly `width` chars |
| `StatutoryRunPayload.cs` | What the renderers read, plus `StatutoryFileResult` |
| `EpfContributionCsv.cs` | KWSP i-Akaun bulk upload |
| `PerkesoContributionTxt.cs` | SOCSO + EIS (+ SKBBK) 278-char fixed width |
| `PcbCp39Txt.cs` | LHDN CP39: 57-char header + 136-char details |
| `PayrollRunReadiness.cs` | What the files need that the run does not have |
| `StatutoryFileService.cs` | Loads the payload; the renderers stay pure |

```
GET /payroll/runs/{id}/files/epf        GET /payroll/runs/{id}/files/socso-eis
GET /payroll/runs/{id}/files/pcb        GET /payroll/runs/{id}/readiness
```

### Byte positions are the contract

These formats are parsed by column, so a field one character short shifts
everything after it — the row is rejected, or worse, silently misread into
another employee's account. `StatutoryFileFields` is therefore **total**: every
helper returns exactly `width` characters, truncating rather than overflowing,
and the tests assert exact slices (`line[194..200]`) rather than `Contains`.

The renderers are pure functions over `StatutoryRunPayload`, which is what
makes that testable at all.

### Where the port diverges

6. **One PERKESO renderer, not two.** The reference ships `socso-eis-txt.ts`
   and `socso-eis-skbbk-txt.ts` — 117 and 121 near-identical lines — with a
   comment saying "diff them when fixing one and the other almost certainly
   needs the same fix". They differ by a single 6-byte column carved out of
   filler. One renderer with a period-derived flag cannot fall out of step
   with itself. **Which layout is decided by the PERIOD**, not the caller:
   SKBBK began 1 Jun 2026, so rerunning May 2026 produces the v1 layout that
   month was actually filed under — the same rule the contribution tables
   already follow.

7. **A missing field is a refusal, not an exception.** The reference `throw`s
   for a missing employer code or IC. Those are admin data problems with a
   specific fix, so they return `StatutoryFileResult.Refused` with a message
   naming the person, and the controller answers 409 rather than 500.

### Identity is read live, money is not

`StatutoryEmployeeRow` takes its amounts from the payslip SNAPSHOT and its
identifiers from the CURRENT employee profile. That split is deliberate and
matches the reference: what someone was paid is the month's filed figure and
must never move, while an EPF number is a current fact about a person — fixing
a typo should reach the next regeneration rather than being frozen into a bad
submission.

A profile archived or deleted since generation falls back to the payslip's own
snapshot for the name, so the row is still filed rather than silently dropped.

### The readiness guard

`PayrollRunReadiness` lives beside the generators because they define what
"required" means, and `SubmitForApprovalAsync` now calls it as guard 5.

Required: employer name, LHDN E-number, SSM number, PERKESO code; per
employee, a payroll number and an IC (or passport for a foreigner).

**The income tax number is deliberately NOT in the gate.** PCB computes
without one and a new joiner waiting on a TIN must not hold up everyone else's
pay. `PcbCp39Txt` checks it at generation time instead — and only for
employees who actually had tax withheld, so someone with no deduction never
blocks the file.

Wiring the real guard into the tests turned 17 state-machine tests red, which
is the guard working: those fixtures now seed Company Info and an IC, and
three new tests take them away on purpose.

---

## Phase 6c — done

The payroll documents. No migration. 35 new tests.

| File | Holds |
|---|---|
| `Pdf/PayrollPdfShared.cs` | Layout helpers, following `LhdnPdfShared`'s conventions |
| `Pdf/PayrollDocumentModel.cs` | The whole run, ready to render |
| `Pdf/PayslipPdf.cs` | One employee's payslip |
| `Pdf/PayrollSummaryPdf.cs` | The run on one landscape sheet |
| `Pdf/PaymentSchedulePdf.cs` | Who is paid what, into which account |
| `MalaysianBanks.cs` | 43 banks, their BICs, aliases and ECP routing |
| `PayrollBankFileXlsx.cs` | Public Bank ECP disbursement file |

```
GET /payroll/runs/{id}/documents/payslip/{employeeProfileId}
GET /payroll/runs/{id}/documents/payslips          (ZIP of one PDF each)
GET /payroll/runs/{id}/documents/summary
GET /payroll/runs/{id}/documents/payment-schedule
GET /payroll/runs/{id}/documents/bank-file?paymentDate=
```

### Look at the page

**The most important lesson of this slice.** The build was clean and 1,034
tests passed on a payroll summary that was visibly broken — a column
definition patch had silently missed its anchor, so 10 columns were defined
for 11 cells and the rows had shifted into each other. Rendering one page to
PNG made it obvious in a second.

Worse, the same look caught a REAL defect no test could have: the summary had
no SKBBK column, so every row was 90 sen short of reconciling. It now has one,
and `Summary_ColumnsReconcileToNetPayOnEveryRow` pins the invariant.

How to do it again (the harness was thrown away deliberately — it wrote to a
fixed path):

```csharp
// Each renderer exposes Build (the IDocument) as well as Render (the bytes).
foreach (var png in PayslipPdf.Build(model).GenerateImages(
    new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 110 }))
    File.WriteAllBytes($"{dir}/page-{i++}.png", png);
```

`pdftoppm` is NOT installed on this machine and QuestPDF subsets its fonts, so
extracting text from the PDF yields glyph tables rather than words. Rasterise
through QuestPDF; do not try to read the PDF.

### The rules the documents keep

- **A payslip's columns sum to its net pay** — the figure that reaches the
  bank. `PayslipPdf.TotalDeductions` mirrors `PayslipCalculator`'s net-pay
  arithmetic and is tested against it across five deduction combinations.
- **Zakat and CP38 sit inside `TotalDeductions`** AND have their own rows, so
  "Other" nets them off. Counting them twice breaks the reconciliation.
- **The payslip masks the bank account; the payment schedule prints it.** One
  is handed to an employee, the other exists so an approver can verify the
  destination before the money moves.
- **The payment schedule flags who the bank file cannot pay**, where the
  approver is already looking rather than on upload.

### Bank file

`MalaysianBanks.Find` matches free text ("Maybank", "MAYBANK BERHAD",
"Malayan Banking") against 43 banks. An unrecognised name returns null and
`PayrollBankFileXlsx` **refuses the whole file** — silently dropping the row
means someone is not paid and nobody notices until they complain. A missing
account NUMBER is different: there is nothing to pay into, so that row is
skipped and the payment schedule flags it.

Three Public Bank ECP quirks, each of which fails the upload: the payment date
must be a real Excel date (text is "invalid"), every amount is TEXT with
exactly two decimals (numeric drops a trailing zero — 3108.10 becomes 3108.1),
and the footer `TOTAL:` row is validated.

### Deferred to 6d

The **PCB calculation-details PDF** — the "show your working" document that
renders 6a's breakdown (~640 lines in the reference, the largest single
renderer). Its data is already stored on every payslip and exposed on the
DTO, so nothing is blocked; it is the last consumer of `PcbBreakdown`.

⚠ It must DESERIALISE `Payslip.PcbCalculationJson` rather than calling
`PcbCalculator.Explain` again. The snapshot is the month's law; re-running
today's engine over a historical payslip is how a filed figure quietly
changes.

---

## Phase 6d — done

The LHDN MTD §E worksheet. No migration. 10 new tests.

| File | Holds |
|---|---|
| `Pdf/PcbCalculationDetailsPdf.cs` | The worksheet — five numbered sections, each LHDN variable with its official description and amount |
| `Pdf/PcbDetailsModel.cs` | One entry per payslip, carrying the deserialised breakdown |
| `PayrollSnapshotJson.cs` | The ONE serializer configuration, shared by the writer and every reader |

```
GET /payroll/runs/{id}/documents/pcb-details
```

### It reads the snapshot, and there is now only one way to read it

Every figure is deserialised from `Payslip.PcbCalculationJson`. The engine is
never re-run: the snapshot is the month's law.

The first cut of this duplicated the JSON options in the reader, which is the
same drift risk this module keeps designing out — a divergence there would
deserialise the breakdown into a record of zeroes, and a page of zeroes reads
as "no tax was due". `PayrollSnapshotJson.Options` is now the single
definition, and `AStoredBreakdown_ReadsBackIdentical` pins the round trip.
Note the stored discriminator is the enum MEMBER name (`"NonResident"`), not
camelCase — the converter is registered without a naming policy.

### Look at the page — again

The 6c lesson held. The build was clean and the tests passed on a page whose
`Y₂`, `K₂`, `M₂` and `LP₁` subscripts were colliding with the description line
beneath them, and whose "ΣLP & LP₁ Details" heading had been orphaned onto the
previous page from its own row. Rasterising found both in seconds.

Fixing the spacing pushed the AR case to three pages with a near-empty last
one, so the rows were tightened and each short section (2–5 and the ΣLP block)
now renders inside `ShowEntire()` — a heading can no longer be separated from
its rows. `TheLongestWorksheet_DoesNotRunToAThirdPage` pins it.

Two pages per resident is correct and deliberate: the reference claims one A4
page per employee, but the full worksheet does not fit legibly, and
readability wins for a document an auditor reads line by line. Each employee
still starts a new page — `EachEmployeeGetsTheirOwnPages` asserts the count is
an exact multiple, so nobody's figures can bleed onto somebody else's sheet.

The rendered page reconciles end to end on the LHDN April example:
P 47,000 → Yearly Tax 1,320 → PCB(A) 110 → PCB(B) 1,320 → CS 2,077.50 →
PCB(C) 757.50 → **RM 867.50**.

---

## Phase 7 — done

Xero. Migration `20260910...AddPayrollRunXeroSync` **already applied** to the
dev DB. 45 new tests.

### The prerequisite, first

`IXeroClient` had bills and spend-money only. Added:

- `CreateManualJournalAsync` — posts `POSTED` / `NoTax`, sends an
  `Idempotency-Key`, caps tracking at Xero's two refs per line.
- `GetTrackingCategoriesAsync` — the project dimension.

⚠️ **Xero gates manual journals behind a different permission from invoices.**
The same token that posts bills happily returns 401 AuthorizationUnsuccessful
here. The client re-packs that one status with the actual fix — the connected
user needs the Adviser role (or Standard + reports) on a plan that supports
journals; Cashbook and Ledger do not — because otherwise the admin goes
hunting through OAuth scopes, which are not the problem.

| File | Holds |
|---|---|
| `PayrollJournal.cs` | **Pure.** Builds and balances the journal |
| `Entities/PayrollXeroMapping.cs` | The mapping shape and the account slot names |
| `PayrollXeroSyncService.cs` | Load, build, post, record |
| `IPayrollXeroSyncService.cs` | Plus the preview and result shapes |

```
GET  /payroll/runs/{id}/xero/preview
POST /payroll/runs/{id}/xero/sync
```

### The journal must balance

That is the invariant the whole slice is organised around, and why the builder
is pure. Xero rejects an unbalanced journal, and one that somehow posted would
misstate the company's books. Every scenario in `PayrollJournalTests` ends by
asserting the balance, and the fixture DERIVES net pay from its own components
so a test cannot pass by measuring its own arithmetic.

Four things have to be right for it to balance, each of which is its own test:

- **Unpaid leave is netted out of the salary debit**, not credited. It was
  never earned; crediting it as well counts it twice.
- **Benefits in kind are skipped entirely.** They reach the tax bases but never
  gross or net, so a debit for one has no matching credit.
- **CP38 and zakat settle into the PCB payable.** They are withheld alongside
  PCB and remitted with it; leaving them out puts the journal out by exactly
  their total.
- **SKBBK settles into the SOCSO payable** — one PERKESO liability, not two.

If the payslips and this arithmetic ever disagree by more than a sen, the
builder REFUSES. `AnInconsistentPayslip_IsRefusedRatherThanPosted` proves it.

### Posting at most once

Two independent guards, because the failure mode is paying the ledger twice:

1. `PayrollRun.XeroManualJournalId`, uniquely indexed. A second press reads as
   "already posted", not as an error.
2. The Idempotency-Key sent to Xero is `payroll-run-{id}-{hash of the lines}`.
   So even a retry after a timeout — where we never learned the first attempt
   succeeded — returns the original journal. Including the payload in the hash
   means an unchanged retry is idempotent while a genuinely corrected journal
   is a new one.

### An approval is never undone by Xero

`SyncOnApprovalAsync` fires after the approval is committed, only when the org
opted in, and swallows everything: an approval already given must not be
reversed because an accounting integration was unreachable. The failure lands
on the run's own `XeroSyncError` column and in the audit trail.
`OnApproval_SwallowsAnyFailure` asserts no exception escapes.

### Where the port diverges

8. **One journal builder, pure, separate from the posting.** The reference
   builds lines, resolves accounts, calls Xero and writes the database in one
   970-line function, which is why its balance check can only be exercised
   against a live Xero. Splitting the arithmetic out is what makes the
   invariant testable at all.

9. **A refusal names every problem at once.** The reference returns on the
   first unmapped category. An admin fixing four of them one failed post at a
   time is four round trips that one message avoids.

10. **A missing accrual account is only a refusal when the run needs it.** An
    org with no HRDF is never asked to map the HRDF slots.

### Deliberately deferred out of phase 7

- **The project dimension.** `PayrollJournal` takes a project per row and
  buckets `SUM_BY_PROJECT` by it, and the tracking lookup is live — but no
  payslip carries a project yet, so every row lands under `(No project)`. When
  employee→project assignment exists, populate `EmployeeRow.ProjectName` in
  `PayrollXeroSyncService.BuildInputAsync`; nothing else has to change.
- **Claims posting on submit.** `PayrollSettings.SyncClaimsToXeroOnSubmit`
  exists and is unread. The claims module already posts bills on its own; what
  the reference adds is firing that from the payroll submit.
- **Cached report invalidation** (still, from phase 5) — the tables do not
  exist yet.

---

## Phase 8a — done

Staff loans and salary advances. Migration `20260911...AddEmployeeLoans`
**already applied** to the dev DB. 61 new tests.

| File | Holds |
|---|---|
| `PayrollLoans.cs` | **Pure.** Schedules, periods, progress, validation |
| `Entities/EmployeeLoan.cs` | The loan, with its schedule as JSON |
| `EmployeeLoanRepository.cs` · `EmployeeLoanService.cs` | |
| `PayrollLoansController.cs` | |

```
GET    /payroll/loans            GET    /payroll/loans/{id}
POST   /payroll/loans            PUT    /payroll/loans/{id}
POST   /payroll/loans/{id}/cancel
POST   /payroll/loans/{id}/reactivate
DELETE /payroll/loans/{id}
```

### The installments add up to the principal

Repaying a sen more than was borrowed is a complaint; a sen less is a
write-off nobody approved. The last installment absorbs the rounding —
RM 1,000 over 3 months is 333.33 / 333.33 / **333.34** — and
`ValidateSchedule` refuses a hand-edited schedule that does not reconcile.

### Progress is derived, never stored

An installment counts as paid when its period has a SUBMITTED run. The loan
row records no payments at all, which is what makes **reverting a month
un-pay its installment for free** — no compensating write, nothing to get out
of step. `RevertingAMonth_UnpaysItsInstallment` pins it.

That same derivation gates editing: once a loan has started repaying, its
earlier installments are inside filed payslips, so re-terming it would leave
those months deducting an amount the schedule no longer contains. It is fixed
from then on — cancel it and record a new loan for the balance. Deleting one
that has started is refused for the same reason: it would erase the
explanation for deductions already taken.

### A loan does not shrink the statutory bases

`deduct_loan_repayment` carries every `SubjectTo*` flag false. Repaying a
loan is the employee spending money they earned, not earning less, so EPF,
SOCSO, EIS and PCB are all computed on the wage BEFORE it.
`GenerateAsync_ALoanDoesNotShrinkTheStatutoryBases` generates the same run
twice — once without the loan, once with — and asserts only the take-home
moves. (Comparing two different MONTHS would have folded in that month's PCB
annualisation and proved nothing; the first draft of that test did exactly
that and failed for the wrong reason.)

Cancelling stops the deduction from the very next run, and a loan past its
last installment stops on its own without anyone closing it.

---

## Phase 8b — done

The employee's own payslips. No migration. 11 new tests.

| File | Holds |
|---|---|
| `EmployeePayrollService.cs` | The caller-scoped reads |
| `PayslipsController.cs` | `/payslips` — any authenticated user |
| `PayslipMapper.cs` | Extracted from `PayrollRunService` so both surfaces map one way |

```
GET /payslips        GET /payslips/{id}        GET /payslips/{id}/pdf
```

### The boundary is the whole phase

Every read resolves the employee from the TOKEN, never from an id on the
request. There is deliberately no "whose payslips" parameter — someone else's
pay is read through the role-gated `/payroll/runs` surfaces.

**Three failures return the same answer.** No such payslip, not the caller's,
and run-not-submitted are all a 404: distinguishing them tells someone probing
ids that a payslip exists, which is already more than they should learn. The
PDF holds the identical boundary — `ThePdfHoldsTheSameBoundary` checks all
four cases against it, because a gate enforced on the JSON and not the
document is decorative.

Only SUBMITTED runs are visible, for the same reason drafts are hidden
everywhere else: a figure that may still move is worse than no figure yet.

### Two cleanups this slice forced

- `PayslipMapper` — the DTO mapping was private to `PayrollRunService`. Two
  copies is how an employee's payslip ends up disagreeing with the one their
  employer is reading.
- The first cut of the list did one query per distinct run to fetch the
  period and submission date. `GetForEmployeeAsync` now returns the
  payslip/run PAIR from one join — an employee with two years' service was
  24 round trips.

---

## Phase 8c — done

The year-end filings. No migration. 45 new tests.

| File | Holds |
|---|---|
| `PayrollAnnualPayload.cs` | The year, aggregated to one row per employee |
| `PayrollAnnualReports.cs` | The four kinds, their metadata, LHDN's filenames, and the shared field rules |
| `Cp8dTxt.cs` | **Pure.** The M and P upload files |
| `Pdf/FormEaPdf.cs` | One EA per employee |
| `Pdf/FormECp8dPdf.cs` | Form E, with the CP8D schedule behind it |
| `PayrollAnnualReportService.cs` | Aggregates the year and dispatches to the renderers |

```
GET /payroll/annual/reports
GET /payroll/annual/{year}
GET /payroll/annual/{year}/reports/{kind}
```

### Only submitted runs count

A draft month is not remuneration that was PAID. Including one would put a
figure on an employee's tax return that never reached their bank, and would
overstate the employer's own declaration.

### Form E's summary must equal its own CP8D schedule

An officer reconciles the cover page against the table behind it, and a
return where they disagree is rejected. Both sides are summed from the same
rows in the renderer — never from a cached total — and the rasterised check
confirmed it: 130,650.00 / 14,107.50 / 3,507.50 on both pages.

### The CP8D column contract

Pipe-delimited rather than fixed-width, so a short field does not shift the
ones after it — but the COLUMN COUNT and ORDER still are the contract, and a
dropped column re-reads every later value as the wrong field. The tests
therefore index into the split row (`cols[7]`) rather than checking a
substring appears somewhere.

Details that would each fail an upload:

- **Sixteen columns plus a trailing pipe.** Columns 9–13 and 15 are reserved
  by LHDN and must be empty.
- **CRLF on every row including the last** — LHDN's parsers are Windows-era
  and reject a file with no terminator.
- **ASCII, no BOM.** A byte-order mark at the head of the first field is read
  as part of the employer number.
- **Whole-ringgit columns ROUND, they do not truncate.** This declares income;
  rounding every employee down would under-declare the employer's total.
- **A passport does not become an IC.** Coercing one into the IC column files
  a wrong identifier against a real person, so it is left empty.

### Who appears on the P file

Someone with no tax reference AND no PCB has nothing for LHDN to match
against, so they are omitted — an empty row invites a rejection on a record
that should never have been sent. But someone who HAD tax withheld is
reported even without a reference: the withholding happened and LHDN has to
see it.

### The tax category

CP8D column 4, from marital status and children. The one worth knowing:
married with the spouse's status UNKNOWN is category 3, not 2 — category 2
claims a relief nobody has established, the same gate the PCB spouse relief
uses.

### Deliberately deferred out of phase 8c

- **Caching the generated files.** The reference stores them on a
  `PayrollAnnualReport` row and busts the cache on approve/revert. Neither
  table exists here, and generating on demand is correct until an org is
  large enough for it not to be. ⚠ If that cache is ever added, revert and
  approve MUST clear it — see the standing note under phase 5.
- **Part A headcounts are derived from join and leave dates.** LHDN's actual
  definitions have edge cases (directors, employees on unpaid leave at year
  end) this does not model. The figures are a starting point an admin should
  check, not a filing-grade answer.

---

## Gaps found after the plan was written

⚠️ The phase plan was built from the reference's FILE list, and four things
did not map cleanly onto a phase and fell through. They are listed here so
the same mistake is not repeated by reading the table alone.

| What | Reference | Why it matters |
|---|---|---|
| **`EmploymentStint` / `EmployeeTransfer`** | `employment-stint.repository.ts`, `payroll-transfer.service.ts` (838) | Moving an employee BETWEEN organisations with their history. Judged **out of payroll's scope** — it is multi-org employee lifecycle, and payroll only consumes the result. Scope it as its own work, not a payroll phase. The one payroll-side hook, `EmployeeProfile.PrevIncludesPriorThisOrgPeriod`, already exists and is tested. |

## Phase 8f-1 — done

`SalaryChange` and the mid-cycle hints. Migration `20260911...AddSalaryChanges`
**already applied** to the dev DB. 39 new tests.

| File | Holds |
|---|---|
| `Entities/SalaryChange.cs` | The audit row, with BOTH sides recorded |
| `SalaryChangeHints.cs` | **Pure.** The proration arithmetic and the four scenarios |
| `SalaryChangeRepository.cs` · `SalaryChangeService.cs` | |
| `SalaryChangesController.cs` | |

```
GET /payroll/salary-changes/{employeeProfileId}
GET /payroll/runs/{id}/salary-change-hints
```

### ⚠️ Correction to what an earlier session claimed

An earlier note said "the reference prorates across the change". **It does
not.** The reference computes the delta and SUGGESTS a line item the admin
applies; it never moves anyone's pay on its own. That is deliberate industry
parity — PayrollPanda, HReasily, Talenox and Kakitangan all leave the
mid-cycle correction to the admin.

This port does the same, for the reason the reference gives: the admin knows
whether a raise was meant to be backdated and the engine does not. What is
removed is the arithmetic and the chance of forgetting entirely — not the
decision.

### The four scenarios

The hint compares the payslip's SNAPSHOT salary against both sides of the
change, which is what tells it whether the admin saved before or after
generating:

| | Meaning | Suggestion |
|---|---|---|
| `OVERPAID` | Saved BEFORE generating — the run paid the new rate all month | deduct the pre-change days |
| `UNDERPAID` | Saved AFTER generating — the run paid the old rate all month | arrears for the post-change days |
| `MATCHED` | Effective on the 1st, nothing to prorate | none, and no banner |
| `UNKNOWN` | Snapshot matches neither side — a second change, or a hand-edit | none; any figure would be a guess |

The two directions sum to the whole delta, which is its own test.

### Two divisors again

The before/after SPLIT is always calendar days — the 15th is the 15th
whatever basis the org pays on. The org's `WorkingDaysRule` decides only the
DIVISOR. So January with the 26-day rule gives 1,000 × 14 ÷ **26** = 538.46,
not ÷ 31. (The integration test caught this: an org with no settings saved
gets the TWENTY_SIX default, not CALENDAR.)

### Applied once, not every refresh

The suggested line's label carries `[salary-hint:{id}]`. On the next load the
hint sees its own marker among the run's manual lines and stops suggesting
itself, while still reporting the delta so the banner can say what was
applied. Another change's marker does not suppress it.

### It is recorded where the salary is edited

`EmployeeProfileService.SaveAsync` captures the salary BEFORE `Apply` mutates
the tracked entity, then records a change if it moved. A trail nothing
populates is worse than no trail — it reads as "this person has never had a
raise". `ASalaryEdit_WritesTheTrail` runs through the real profile service
rather than calling the payroll service directly, so the hook itself is what
is under test.

No cycle: `EmployeeProfileService → ISalaryChangeService → IDirectoryService →
repositories`. `DirectoryService` depends on repositories only.

The history is read-only over HTTP. A change is written as a side effect of
the salary edit, so the two cannot disagree — an independently writable
history would be a second source of truth for what someone earns.

---

## Phase 8f-2 — done

Saved portal logins and the past-leaver sweep. Migration
`20260911...AddPortalCredentials` **already applied**. 22 new tests.

| File | Holds |
|---|---|
| `Common/SecretBox.cs` | AES-256-GCM at rest, for secrets that must be read BACK |
| `Entities/PayrollPortalCredential.cs` | One saved login per (org, portal) |
| `PortalCredentialService.cs` · repository · controller | |
| `Cron/ArchivePastLeaversBackgroundService.cs` | The daily sweep, plus `PastLeaverArchiver` |

```
GET    /payroll/portal-credentials
GET    /payroll/portal-credentials/{portal}/reveal
PUT    /payroll/portal-credentials/{portal}
DELETE /payroll/portal-credentials/{portal}
```

### ⚠️ `SecretBox` is NOT for authentication

It is REVERSIBLE on purpose — the whole feature is showing an admin their
KWSP password back at filing time. Passwords the app authenticates against
are hashed and never recovered; that is `PasswordHasher`, and the two must
not be confused. `SecretBox` refuses to start in production without
`Secrets:PortalCredentialsKey`, because falling back to a derived key would
encrypt real credentials under a value anyone with the source can reproduce.

A fresh IV per call, so the same password stored for two orgs produces
different blobs — a reader of the raw table cannot tell they match. A blob
that will not decrypt (written under a previous key) reads as "no password"
rather than taking the page down.

### Reading a password is an event

The list MASKS every password and only reports `hasPassword`. Revealing one
is a separate endpoint behind an explicit click, and it is **audited** —
`payroll.portal-credential.reveal` exists precisely because a read here is a
credential disclosure. The audit metadata never contains the value;
`TheAuditTrailNeverContainsThePassword` pins that, since a log that records
the password defeats encrypting it.

### The password field has three meanings

Conflating them is how someone's stored password quietly disappears:

| Sent | Means |
|---|---|
| omitted (`null`) | leave the stored one alone — correcting a login id |
| `""` | clear it |
| anything else | replace it |

### The past-leaver sweep

**No financial effect** — generation already excludes a past-leave-date
employee, so a leaver is not paid either way. This is bookkeeping: without it
the Active employee list fills with departed staff.

The case it exists for is the planned leaver — a leave date set months ahead
on a profile nobody reopens, so no on-save path ever fires.

In-process on the house `BackgroundService` pattern, no external cron or
shared secret. Runs with no request context, so the tenant filter is a no-op
and one pass covers every org — `GetUnarchivedPastLeaversAsync` says
`IgnoreQueryFilters()` explicitly so a future reader does not assume it is
scoped. Someone whose leave date is TODAY stays active: a last working day is
a day they are still employed.

### Deliberately NOT done: employment stints and transfers

`EmploymentStint` and `EmployeeTransfer` (838 lines in the reference) move an
employee BETWEEN organisations, carrying their payroll history. That is
multi-org employee lifecycle, not payroll — payroll only consumes the result.
It is listed here because it was found while auditing this module, but it
should be scoped as its own piece of work rather than smuggled in as a
payroll phase.

The one payroll-side hook already exists:
`EmployeeProfile.PrevIncludesPriorThisOrgPeriod` anticipates a rehire in the
PCB carryover and is tested. Nothing writes the history that would set it.

---

## Phase 8d — done

Seeding a year of history. No migration — `PayrollRunSource` has existed
since phase 3. 36 new tests.

| File | Holds |
|---|---|
| `YtdImportParser.cs` | **Pure.** Reads the sheet; owns the column contract |
| `YtdImportService.cs` | Matching, and writing the months |
| `YtdImportController.cs` | Template · preview · import |

```
GET  /payroll/ytd-import/template/{year}
POST /payroll/ytd-import/{year}/preview
POST /payroll/ytd-import/{year}
```

### Why it exists

PCB annualises against the year to date. An org that switches systems in
July gets **every remaining month's tax wrong** unless January to June are on
file. That is the whole point — this is not a convenience.

### Imported months are SUBMITTED and taken as typed

SUBMITTED because that is the only status `GetYtdByEmployeeAsync` reads: a
history that did not count towards year-to-date would leave PCB exactly as
wrong as having no history at all.

`IMPORTED` marks them as typed rather than computed, and the figures are
stored EXACTLY as given — nothing recomputes EPF or PCB from the salary.
Those months were paid; what this engine would have calculated is beside the
point. `PcbCalculationJson` is deliberately null: inventing a worksheet would
claim a derivation this engine never performed.

### A COMPUTED month is never overwritten

Replacing a real run with spreadsheet figures would destroy the payslips
those figures were reconciled against. Such months are skipped and NAMED in
the result; the preview warns before anything is written. Re-importing an
already-IMPORTED month replaces it, so a corrected sheet does not double the
history.

### Two matching keys, neither guessed

IC first, then name — the IC is reliable and a sheet from another system will
not carry our ids. A name matching more than one employee is NOT resolved,
and an unmatched name is reported BY NAME so the admin can fix the spelling
rather than wonder who was dropped.

### The column contract

Header TEXT is the contract, not position — `ColumnsMayBeInAnyOrder` proves
it. That test caught a real bug: the first draft detected employee-vs-month
rows by **column 0**, so reordering the sheet misread every row. The name
column's index is now read from the header, and it does double duty (a name
on a block's first row, a month on the twelve under it).

An unrecognised header is REPORTED, never ignored — a mistyped "Bonis"
silently dropping a year of bonuses is exactly the failure this must not
have. The generated template parses back cleanly, which
`TheTemplateParsesBackCleanly` pins: if the two drifted, an admin's first
import would fail on a sheet we produced.

---

## Phase 8e — done

Bulk-filling employees' payroll details. No migration. 20 new tests.

| File | Holds |
|---|---|
| `PayrollEmployeeSheet.cs` | The column definitions and their aliases |
| `PayrollEmployeeImportService.cs` | Template, export, import |
| `PayrollEmployeesController.cs` | |

```
GET  /payroll/employees/template
GET  /payroll/employees/export
POST /payroll/employees/import
```

### 2,503 reference lines became ~250

The reference hand-rolls its own spreadsheet parsing, header matching,
example-row detection and per-row error reporting. **This codebase already
has all of that** in `Common/Tabular`, used by Attendance, Leave and Claims.
Identity resolution comes from the shared `EmployeeImportColumns` /
`IEmployeeRowResolver`, so all four importers agree about what "who is this
row about" means and cannot drift apart.

Porting the reference verbatim would have been a fourth private copy of
machinery this app already owns.

### It updates; it does not create accounts

A row naming somebody not on the roster is REPORTED, not invented — an
account carries a login and a role, and neither belongs in a payroll sheet.
An org member with no payroll profile yet does get one, since filling those
in for a roster that has none is the point.

### A blank cell leaves the field alone

That is what makes a partial sheet — "here are everyone's bank details" —
safe to import over a roster that already carries statutory numbers.

### Money is reported, never shrugged off

Every other field shrugs off an unreadable value and keeps what is stored. An
AMOUNT does not: a mistyped salary silently ignored is someone paid the wrong
amount with nothing on screen to say why. A bad or negative amount fails its
row, names the row number, and leaves the stored figure untouched. One bad
row never stops the others.

The export round-trips back through the import, so export → edit → import is
a real workflow rather than two shapes that happen to look similar.

---

## Later phases — the traps

**Phase 6 also owns the readiness guard.** `payroll-readiness.service` in the
reference blocks a submit when Company Info or an employee is missing a field
the generators need — so it is defined BY the generators. `Payslip.StatutoryWarnings`
already records missing TIN / EPF / SOCSO numbers and is its natural input, and
`PayrollRunService.SubmitForApprovalAsync` is where it hooks in.

**Phase 6c reads the breakdown, it does not recompute it.** The LHDN-form PDF
must deserialise `Payslip.PcbCalculationJson` rather than calling the
calculator again — the snapshot is the month's law, and re-running today's
engine over a historical payslip is how a filed figure quietly changes. (The
CP39 TXT needs only the `Pcb` and `Cp38` columns, so it does not read it.)

**Phase 6c's renderers should be pure over `StatutoryRunPayload` too.** The
loader, the row shape and `StatutoryFileResult` are all in place — the payslip
PDF and payment schedule need the same joined identity the TXT files do, and
`StatutoryFileService.LoadAsync` already provides it.

**Phase 8c, LHDN annual forms.** `Modules/LhdnForms/` already renders CP21 /
CP22 / CP22A / TP3 / PCB 2(II) PDFs in C#, and `PayrollCompanyInfo` already
holds the employer filing identity those forms need. Form EA, CP8D and Form E
are the new work — reference at `modules/payroll/domain/annual-reports.ts`
(114 lines) plus `payroll-annual-reports.service.ts` (283) and the
`form-ea-bulk-pdf` / `form-e-cp8d-pdf` / `cp8d-*-txt` renderers.

**Phase 8e, employee import.** 2,503 lines in the reference and by far the
largest single file in the module. Worth scoping hard before starting: much of
it is spreadsheet-shape tolerance for one customer's historical workbook, and
this backend already has `Common/Tabular` doing the same job for other modules.

---

## Known risks

**Decimal-vs-float parity is unverified against a real payslip.** The reference
runs this math in JS floats; this port uses exact decimal. Where they differ the
C# is the *more correct* one, but byte-identical parity with the old system is
not guaranteed at the margins — a `Math.ceil` that float error nudged over a
boundary will land differently. Phase 3 now produces whole payslips, so this is
newly checkable and newly consequential: **spot-check one real employee's real
month against the old system before any of this reaches a payslip an employee
sees.**

**Carried-over gaps in PCB**, all documented at the top of `PcbCalculator.cs`:
TP1 items the employer cannot know (life insurance, lifestyle, parents' medical)
stay employee-declared; Returning Expert Programme / Knowledge Worker / approved
non-citizen C-suite compute as standard residents; `PcbBorneByEmployer` gross-up
is not implemented.

**Tax bands are LHDN's 2024 schedule.** The 2026 MTD spec confirms the formula is
unchanged — only TP1/TP3 items moved — so they are current. If LHDN shifts a
threshold, `PcbTaxBands.ResidentBands2024` is the one place to change.

---

## Validation already banked

Four LHDN PCB 2026 worked examples (spec pages 45–50) reproduce **exactly**:
Jan RM 110.00 · Feb RM 110.00 · Mar RM 110.00 · Apr bonus month RM 110.00 normal
+ RM 757.50 additional = RM 867.50. The March-with-TP1 case also lands on LHDN's
published RM 108.20 — a check the reference app's own suite never ran.

The tests also carry the reference app's production defects forward as
regressions, with the reasoning intact:

- Malaysian citizens at 60+ mis-routed to Part C, over-charging 5.5% employee EPF
- Part F banded instead of flat, over-collecting RM 1
- Voluntary EPF ceiled separately from mandatory (1,612 vs the correct 1,611)
- Employer side on exact percentages while the employee side used the table (RM 6 gap)
- A weekday roster used for s.18A proration
- SKBBK firing for everyone from Jun 2026 regardless of opt-in
- `trunc2(32.55)` returning 32.54 through IEEE 754 drift

**Keep them green.** They are the regression net for every remaining phase.

Phase 3 adds its own regressions to the pile, each with the reasoning in the test
body:

- the money multiplies by the EXACT proration ratio, not the rounded snapshot
  factor (21 sen on one late joiner at 4 dp)
- a bonus joins the EPF tier wage but cannot push the employer across the
  RM 5,000 cliff on its own
- an allowance exempt under its annual ceiling carries only its TAXABLE portion
  into next month's Y — carrying the full amount inflated PCB and compounded all
  year
- two rows in one category share one headroom, within a run and across the year
- unpaid leave comes off gross and is NOT also in the deductions total
- a benefit in kind never reaches gross, yet the tax on it still leaves net
- CP38 is withheld but stays out of `Pcb`, so it cannot suppress next month's X
- a rehire's declared YTD is not double-counted against this org's own

Phase 4 adds its own, in `PayrollRunAdjustmentsTests`, `PayrollRunAdjustmentServiceTests`,
`PayrollRunClaimServiceTests` and the new block in `PayrollRunServiceTests`:

- an amount override keeps the row's CATEGORY, so an overridden travel allowance
  stays out of the EPF base instead of quietly joining it
- `skip` beats a co-present `amount`, and an override amount of ZERO is an
  instruction rather than an absent value
- a stale override index (past the end of the array) is ignored, not thrown on
- malformed JSON in either column reads as empty and the month still pays
- a one-off deduction routes through its category's flags, not a hardcoded
  "subject to everything"
- OT is paid at the POLICY's multipliers, is skipped entirely under `TIME_BANK`
  or `OtEnabled=false`, and falls back to the s.60A floor when there is no
  policy at all
- OT stays out of EPF while still raising SOCSO and EIS
- an attached claim reaches gross and net but no statutory base at all, and its
  line item still carries the claim id
- a claim can be attached to exactly one run, ever — the second attempt names
  the run already holding it
- attach snapshots label and amount, so editing the claim afterwards cannot move
  a generated figure
- adjustments and attachments survive a regeneration; the payslips they produce
  do not
- generation clears the staleness flag its own inputs raised

Phase 5 adds, in `PayrollRunStateMachineTests` and the attendance block of
`PayrollRunServiceTests`:

- a stale draft cannot be submitted, and regenerating clears the block
- a run where anyone takes home zero or less is refused, by name
- February cannot be submitted while January is a draft, NOR when January does
  not exist but earlier months were submitted — while an org's genuine first
  run in any month is allowed
- December 2025 is treated as the month before January 2026, not as later
- approve records the APPROVER, separately from who proposed it
- approval is the moment a month starts counting towards YTD
- reverting January also reverts February and March, and says so — but leaves
  the following year alone
- reverting keeps the payslips, so the draft can actually be fixed
- submit, approve and revert all leave `LastMutatedAt` untouched
- deleting a draft takes its payslips, adjustments and attachments with it, and
  frees the claims rather than consuming them
- BeyondShiftMin never reaches WorkedHours
- no attendance access yields null hours, not zero
- unpaid leave is docked once, off gross AND the statutory bases, never from
  hourly staff



---

## Admin frontend — done

`frontend/src/features/payroll/` — five tabs on the admin payroll surface:
**Payroll runs · Employees · Loans · Annual forms · Settings**.

```
features/payroll/
├── api.ts                       every payroll endpoint, typed
├── lib/payroll-format.ts        rm(), dates, status + loan labels, warning codes → English
├── lib/ui.ts                    the shared Tailwind strings
└── components/
    ├── AdminPayroll.tsx         the tab shell
    ├── PayrollRunsList / RunDetail / RunActions / RunDownloads
    ├── PayrollPayslipsTable / PayslipDrawer / SalaryChangeHints
    ├── PayrollEmployeesTab / PayrollBulkFillPanel
    ├── PayrollLoansTab / LoanForm
    ├── PayrollAnnualTab / YtdImportPanel
    └── PayrollSettingsForm
```

### One backend endpoint was added for it

`GET /payroll/employees` (`PayrollEmployeeDirectoryService`). Nothing else
exposed an **employeeProfileId**: `/employees` returns the USER id, and
payslips, loans and salary changes all key off the profile — so a loan could
not be attached to anyone from the UI. It is read-only; every field on it is
edited on the employee's own profile or through the bulk import, so there is
only ever one place a salary comes from.

`PayrollRunReadiness.EmployeeGaps(employeeCode, idNumber, isLocalOrPr)` was
extracted out of the run loop so that roster and the run's readiness check
share one definition of what "missing" means. Two copies would drift.

### Things the UI is deliberately careful about

- **The readiness check is brought forward.** The Employees tab runs the same
  gap check against LIVE profiles, so a missing IC is fixed in the quiet week
  rather than on the afternoon a submission is refused.
- **No salary on file is a SEPARATE warning** from a statutory gap. One means
  the filing will be rejected; the other means the person is paid nothing and
  the run cannot be submitted. Same banner style, different sentence.
- **Loan terms are not previewed client-side.** The last installment absorbs
  the rounding so the schedule sums to the principal exactly, and a
  client-side guess at that arithmetic would sometimes disagree by a sen.
- **A cancelled loan's remaining amount reads "not collected"**, muted — the
  arithmetic remainder is real but nothing more will ever be deducted, and
  plain "1,000.00" in an Outstanding column reads as active debt.
- **YTD import is two-step and the Import button stays shut** until a clean
  preview has been seen. The endpoint answers **400 carrying the same payload**
  as a success, with `ok: false`; `api.ts` unwraps that rather than throwing,
  because it is the contract and not a failure.
- **Only the status transitions a run can make are rendered.** A disabled
  "Approve" on a draft invites hunting for why it is greyed out.

### Fixed while verifying against the running app

- `shared/lib/api-client.ts` `getErrorMessage` did not understand the
  `{ error: ... }` body shape that **25 backend endpoints** use (73 use
  `message`). Every written refusal from those — a stale run, a zero-net
  submission, a missing employer code — was being thrown away and shown as
  "POST … failed: 409". Pre-existing gap, fixed in the shared client.
- Raw enum codes (`MISSING_INCOME_TAX_NUMBER`) were reaching a tooltip.
  `warningLabel()` in `payroll-format.ts` maps them; unmapped codes still
  degrade to English rather than to a constant.

### Verified live against :5001

Create draft → generate → review payslips → submit guard fires with its real
message → settings save → readiness recomputes (6 → 2 items) → statutory files
unblock → loan created, deducted, and reconciled on the payslip
(6,500 − 715 − 29.75 − 11.90 − 333.33 = 5,410.02), with the schedule summing to
the principal exactly (333.33 + 333.33 + 333.34).

### Not built

- **Per-run adjustments and claim attachment.** The backend (phase 4) is done
  and `PayrollRunAdjustment` / `PayrollRunClaim` have controllers; there is no
  UI for adding a one-off allowance, overtime hours or an attached claim to a
  draft run. This is the largest remaining gap — without it the only way to
  vary a month is to edit the profile.
- **Xero preview / manual sync.** `getXeroPreview` and `syncPayrollToXero` are
  typed in `api.ts` and unused. Approval-time sync works on its own.
- **The hand-varied loan schedule.** `SaveEmployeeLoan.schedule` is typed and
  accepted by the server; the form only offers FIXED and CUSTOM.
- **A salary hint's suggested line cannot be applied from the UI** — it needs
  the adjustments surface above.

---

## Adjustments and claim attachment — done

The last admin gap. Eight endpoints existed with no UI, so a month could only
be varied by editing profiles.

### Two more backend endpoints

- `GET /payroll/adjustment-categories` — the 46-entry catalogue with its
  statutory flags. Served rather than duplicated in the client because the
  calculator DISPATCHES on these codes: a second copy that drifted would
  describe a row's treatment wrongly, or offer a code generation silently
  skips. The group comes from the code prefix, so it cannot fall out of step
  with a hand-maintained list.
- `GET /payroll/runs/{id}/adjustments/{employeeProfileId}/context` — the saved
  row, the profile's recurring allowances, what attendance derived, whether
  typed overtime will be paid at all, and the period's loan installments, in
  one read. Assembled to match `PayrollRunService.BuildInput` exactly: showing
  a preview generation then contradicts is worse than showing nothing.

### Where this diverges from the reference — deliberately

The reference's adjustment form gives MONTHLY staff a **"Worked (% of
expected)"** field and persists it as `worked=<percent>, expected=100`, so the
ratio prorates the salary.

**That field would do nothing here.** `PayslipCalculator` reads `WorkedHours`
at one place only — line 263, `worked × hourlyRate` — which is the HOURLY
branch. For MONTHLY staff the hours are recorded on the payslip and never
scale pay, exactly as this file's "Attendance and unpaid leave" section says:
a monthly salary is day-based, and approved unpaid leave is what docks it.

So the port offers **plain worked/expected hour fields**, labelled as
recorded-not-paid for MONTHLY and as the paid quantity for HOURLY. Shipping
the percentage would have let an admin type 50 expecting half a salary and
get a full one.

The form also **says when typed overtime will be ignored** — `cashOt` is false
when the policy banks OT as time off or disables it, and the reference simply
accepts the number either way.

### Verified live

A RM 3,000 Annual Bonus on Evan Employee: gross 6,500 → 9,500, EPF 715 →
1,045 (11% of 9,500 — bonus feeds EPF), SOCSO and EIS unmoved (the category
says not subject), net 8,413.35. The payslip drawer's "how each line was
treated" agreed with the picker's own "Feeds EPF, PCB". Saving raised the
staleness banner without a reload. PCB stayed 0.00, which is correct and was
checked rather than assumed: P = 14,098.35, M = 5,000, R = 1%, B = −400, so
90.98 − 400 clamps to zero — the org has no submitted 2026 runs, so YTD is
zero and annualisation spans Sept–Dec only.

## Design review against the production app

Audited with the `redesign-existing-projects` skill. Most of it targets
marketing pages (font swaps, grain overlays, parallax, hero imagery) and was
not applied — the font is already Manrope, matching the reference, and the
admin portal is a dense data tool. What was real:

- **`position: fixed` was broken on both drawers.** `CARD` carries
  `backdrop-blur-sm`, and a backdrop-filter establishes a containing block —
  so the payslip drawer and the new adjustment editor sized themselves to the
  card they opened from (1074×302 instead of 1440×1000) with content clipped.
  Both now render through `DrawerPortal` on `document.body`. This had been
  shipped in the earlier slice and missed because the drawer was only ever
  read as text, never looked at.
- **No focus ring on any payroll button.** The reference puts
  `focus-visible:ring-2` on every button, and this app's own `select.tsx` and
  `switch.tsx` already follow it. Added to all three button styles and to
  every bare icon button, along with an `active:scale-[0.98]` pressed state.
- **`window.prompt` for the rejection reason.** Unstyleable, unthemeable, and
  it reads as a browser error — for a note that is kept on the run and shown
  to whoever picks it up. Replaced with an inline form.
- **Escape did not close either drawer.** Click-outside only strands anyone
  working from the keyboard. `DrawerPortal` handles it, and also locks body
  scroll so a wheel gesture past the drawer's end does not scroll the page
  behind it.
- **Bare "Loading…" text** on four tables replaced with `TableSkeleton`, so
  the page does not jump when rows land. Decorative, `aria-hidden`, with an
  `sr-only` status for assistive tech.

Not changed, deliberately: uppercase table headers (conventional for data
tables and already the house style), Title Case category labels (they are the
catalogue's own names, ported verbatim), and the left sidebar.

### The payslips table, rebuilt against the reference

The first cut showed 7 columns and carried a comment justifying it — that
employer-side contributions "do not change what anybody is paid, and eleven
columns is a table nobody reads". That was wrong. The reference's
`payslip-list-panel.tsx` shows **19 columns in three banded groups**, and the
employer band is exactly what a payroll is reconciled against: the cheque to
KWSP is the two halves added together.

Now matching it:

- **Hours and days** — Hrs, Days (paid/in period), OT N, OT R, OT PH. Days and
  the OT columns are blank for HOURLY staff, who are paid for hours with no
  day count to prorate.
- **Employee pays** (cyan) — PCB, EPF, SOCSO, EIS, SKBBK.
- **Employer pays** (orange) — EPF, SOCSO, EIS, HRDF.
- Gross, Net pay and Cost sit outside the bands, bolded, as the three figures
  the bands resolve to.
- The employee cell is **sticky** and carries the arithmetic behind the gross:
  base pay, overtime with the hours that earned it, then every line item
  signed — deductions red and negative, allowances green and positive, a
  benefit in kind tagged "(benefit, not cash)" and unsigned so the column
  still adds up to the gross beside it.
- A **summary block** underneath gives the figures that are actually remitted:
  EPF, SOCSO and EIS with both halves added, PCB including CP38, plus zakat,
  HRDF and BIK when non-zero. Reading those off the bands means adding two
  columns in your head.

The adjustment catalogue is fetched once on the run detail and shared by the
table (to know which categories are non-cash) and the editor, instead of each
fetching its own.

Verified live: 19 columns, bands correct, and both sides reconcile —
6,500 − 715 − 29.75 − 11.90 = 5,743.35, and 6,500 + 780 + 104.15 + 11.90 =
7,396.05. Agency totals: EPF 1,495.00, SOCSO 133.90, EIS 23.80.

---

## Settings — the 16 unbound fields, and the defect behind them

The settings form bound 22 of the 38 fields on `PayrollSettings` +
`PayrollCompanyInfo`. The 16 missing ones were **not** being wiped — the form
spreads the loaded object and strips only `isConfigured`/`updatedAt`, so they
round-tripped — but there was no way to set them.

Checking what actually consumed them found one real defect and a lot of
dormant schema:

| Fields | Read by this port | Read by the reference |
|---|---|---|
| `ecpPayorAccountNo` + 4 payor fields | ✗ | **✓ `pb-ecp-xlsx.ts`** |
| 4 × `declarant*` | ✗ | ✗ (mapped in `annual-shared.ts`, never rendered) |
| 5 × `taxAgent*` | ✗ | ✗ (same) |
| `zakatNumber`, `handphone` | ✗ | ✗ |

### The defect: the bank file ignored the payor account

Public Bank's ECP upload keys on the **filename**, not a cell:
`<10-digit account>PR<DDMMYY><NN>.xlsx`. This port emitted
`bank-payment-2026-09.xlsx` and never read `EcpPayorAccountNo` at all, so the
file it produced would be rejected by the portal with nothing to explain why.
The reference also refuses up front when the account is missing or not exactly
ten digits.

`PayrollBankFileXlsx.Render` now takes the payor account, refuses with the
specific setting to fix when it is absent or the wrong length, and builds
PB's filename. `StatutoryFileService` gained `IPayrollSettingsService` to
supply it. Seven tests added (`PayrollDocumentTests`), 1,333 total.

Verified live: the file 409s with *"No Public Bank payor account number is
set…"* before configuration, and afterwards downloads as
`3161234567PR30092601.xlsx`.

### The settings page

Two new cards, and two fields added to the employer card:

- **Bank disbursement** — payor account (with the 10-digit rule stated),
  BIC, account holder, organisation code, paying bank.
- **Form E declarant and tax agent** — declarant name, position, ID type and
  number; tax agent name, TIN, licence, phone, email. Labelled honestly as
  stored-but-not-yet-printed, because neither this port nor the reference's
  Form E renderer prints them.
- Employer card gained mobile number and zakat registration no.

`syncClaimsToXeroOnSubmit` is deliberately still absent: it is a Claims
concern that happens to live on the payroll settings row, and Claims →
Settings already owns that switch. Exposing it twice would let two screens
disagree.

### Downloads, ported from the reference modal

Read from `components/admin/payroll-downloads-modal.tsx` and
`modules/payroll/domain/reports.ts` on a fresh clone. It is a **dialog**, not
a section on the run page — these files are wanted once, at month end, and
eight cards parked permanently under the payslips pushed the run's own
figures off the screen.

Matched:

- `Download files — {period}` with the run's own subtitle, opened from a
  single button on the run.
- A **Select all (N)** bar with the running count, `none` when empty.
- Four groups in the reference's order — **Reports · Statutory uploads ·
  Payslips · Bank** — with its small-caps headings.
- Rows carry a checkbox, a file icon, the title, the **portal the file
  uploads to** (`→ KWSP i-Akaun (Majikan)`), the description, and their own
  Download button. Titles and descriptions are taken from
  `PAYROLL_REPORT_META`.
- A **payment date** at the head of the Bank group. The bank file writes it
  into the sheet AND into the filename PB parses, so it is chosen rather than
  silently defaulted — it defaults to the last day of the period, which is
  also the server's fallback. `downloadBankFile` already accepted it; nothing
  had ever passed one.
- Errors are held **per file**, because a batch half-succeeds routinely: the
  EPF file comes down while the CP39 refuses for a missing tax number.

Where this port differs, and why:

- **No ZIP bundle.** The reference bundles the selection client-side with
  JSZip, which is not a dependency here; the batch saves sequentially
  instead, so the button reads "Download N selected" rather than "as ZIP".
  Sequential, not parallel — each is a server-side render.
- **PCB Calculation Details is one PDF**, not a ZIP of one per employee. The
  title says so rather than promising the reference's shape.
- **One PERKESO row, not two.** The reference offers v1 and ASSIST 2.0
  separately; this port picks the layout from the PERIOD, so an older month
  files under the rules it was paid under without the admin choosing.
- **One bank row.** Only the PB ECP renderer exists — see the open gap on
  Maybank, CIMB and Hong Leong.

Verified live: the modal opens with all four groups, Select all picks 8, and
the batch fires all eight in order — the bank file going out as
`?paymentDate=2026-09-30`, every response 200. Escape closes it and the
body scroll lock is released.

### Checkboxes

The native control paints itself with the operating system's accent — bright
blue on macOS, barely roundable, and nothing like the app's purple. Payroll
had five of them.

`components/PayrollCheckbox.tsx` now hides the real input (`opacity-0`, still
focusable and still a real checkbox for keyboard and screen readers) and
draws a span in its place: 20px, rounded, `bg-primary` with a lucide tick
when checked, and the focus ring moved onto the visible box via `peer-`.

The settings toggles additionally follow the reference's `Toggle`: the whole
row is a bordered card with the question on the left and the box plus a
"Yes" label on the right, so a setting reads as a decision rather than as a
stray tickbox floating in a card.

Left as plain checkboxes, correctly: the download picker and the "Show
archived" filter are list selections, not settings, and a card row each
would be wrong.

### Overview replaces Employees in the tab bar

The reference's payroll nav is **Overview · Payroll Runs · Annual Tax Forms ·
Loans · Settings** — no Employees item. Matched: `PayrollOverview` is the
first tab and the default, and Employees is gone from the tab bar.

**"Manage employees" hands off to Company/Employee → Manage Employee**, via
the admin shell's existing `onOpen(parent, child)` — the same callback the
executive overview's quick actions use. Payroll does not keep a second place
to edit a salary.

What IS payroll's own — the statutory-gap roster and the bulk-fill sheet —
stayed, renamed **Statutory readiness**, because it is not employee
management. It is reached from the overview (a card, and the blocked-employee
tile) with an explicit "Back to overview".

### The overview is a dashboard, not a glossary

The first cut ported the reference's page more or less literally, including
its long "How each line is calculated" table — nine rows of prose on wage
bases and gazette references. On a page an admin opens every month that is
something to scroll past, not something to read; the statutory rules belong
in this document and in the code comments beside the tables that implement
them.

Rebuilt on the same pattern the **claims and leave dashboards** already use,
so the three admin modules do not each invent their own shape:

- **Three attention tiles**, clickable, each falling back to a sentence
  rather than a bare "0" — runs awaiting approval (with the net pay held up),
  employees who would block a filing, and drafts sitting behind their inputs.
- **Latest run** — status, staleness, headcount, gross / net / cost.
- **Filed so far this year** — net paid plus EPF, SOCSO, EIS and PCB with
  BOTH halves added, since that is what is actually remitted. Approved runs
  only; a draft is not money that moved.
- **Elsewhere in payroll** — loans (with the outstanding balance), employees,
  settings.

`CardHead` and `EmptyState` come from `features/admin/components/DashboardCard`
rather than being re-cut, and the tile matches `AdminClaimsAttention`'s.

Every figure is derived from data the page already loads — runs, the roster,
loans — so nothing new was needed on the backend.

### Files come only from an APPROVED run

A defect, not a polish item. Every document and statutory endpoint served a
**DRAFT** run with 200 — verified live before the fix:

```
files/epf          200
files/pcb          200
documents/summary  200
```

There was no status check anywhere in `StatutoryFileService`, and no test
asserted one. So an admin could pull an EPF CSV or a CP39 off a draft and
upload it, having filed figures that a regenerate would then change —
generation rebuilds every payslip from scratch.

The reference gates on exactly this: `canGenerate` is
"`status = SUBMITTED`", the modal disables every button when it is false,
and the description adds *"Submit + approve the run before generating
files."*

`RefuseUnlessApproved` now guards both funnels — `RenderAsync` for the three
statutory files, and the six document methods after `LoadDocumentAsync`. It
returns the module's usual refusal, so the controller answers **409** with
the reason rather than a 500 or a truncated file.

**Readiness is deliberately exempt.** Its whole job is to be asked before a
run is approved, and routing it through the same guard would have made the
submit check impossible.

Five tests added in `PayrollRunStateMachineTests`, covering the shape of the
rule rather than one endpoint: refused as a draft, still refused while
awaiting approval, produced once approved, and **refused again after a
revert** — a reverted month must stop handing out figures that are no longer
filed.

The section is **hidden outright on a draft** rather than shown as a
disabled card explaining itself. The run's own status badge and action
buttons already say where it is; a panel whose only content is why it is
empty is noise on the screen an admin spends the month in. It appears when
approval makes the files real.

The employee portal is unaffected: `EmployeePayrollService` already only
returns payslips from submitted runs.

### Row actions moved into the sticky name cell

The adjust and download buttons sat in a trailing column, which on a
1,480px-wide table meant scrolling the whole contributions grid sideways to
reach them. They now sit beside the employee's name, inside the sticky
column, for the reason the reference states in its own comment: *"The icon
sits inside the sticky column so it's always reachable without scrolling the
wide totals grid horizontally."* The trailing column is gone.

### The session expired, and the client never noticed

The adjustments drawer reported
`GET …/adjustments/…/context failed: 401` and read as an unbuilt page. It
was not — the endpoint answers 200 with a valid token, and the drawer
renders fine. The access token had simply expired.

`Jwt:AccessTokenMinutes` is **15**. `refresh()` was called once, on app
load, and `api-client.ts` had **no 401 handling at all** — so any tab left
open longer than fifteen minutes answered the next click with a bare
"failed: 401", in every module, not just payroll.

The shared client now refreshes once on a 401 and retries the original
request. Three things that matter in it:

- **Single-flight.** A screen firing six requests at once must not fire six
  refreshes — each rotates the cookie, and the losers would be left holding
  a revoked one. They all await the same promise.
- **`send` is a thunk**, so the retry rebuilds its headers with the NEW
  token rather than replaying the expired one.
- **`/auth/*` is exempt.** Retrying a failed refresh through the refresh
  path is how you get an infinite loop.

If the refresh itself fails — offline, or the cookie really is gone — the
original 401 is returned untouched and the caller sees it, because at that
point the session genuinely is over.

### The duplicated label, and the sizing bug underneath it

A one-off line rendered its category name twice — once in the picker, once
as the value of the label field below it.

`addLine` was copying the category's label into the label field, and a
category change kept rewriting it. But the server already falls back: a
blank label takes the category's own name. So the field is now **empty with
that name as its placeholder** — the default is visible without being
duplicated as a value someone has to clear first. Changing the category no
longer overwrites a label the admin typed.

Looking at it also exposed why the row was stacked over four lines instead
of sitting on one. `${INPUT} h-10 w-32` **was doing nothing**: Tailwind
emits utilities grouped by property, not in the order the classes appear on
the element, so the `h-12 w-full` baked into `INPUT` won every time. The
amount field measured 563×48 where 128×40 was asked for, which is what
forced the wrap.

Seven call sites across payroll had the same silent failure. `INPUT` is now
split so a size cannot conflict with it:

- `INPUT` — the default, `h-12 w-full`
- `INPUT_SM` — `h-10 w-full`, for dense repeating rows
- `TEXTAREA` — no height at all, grows with content

Width comes from a wrapper rather than a class that cannot win. Verified by
computed style rather than by eye: 128×40.

### One-off line items, rebuilt to the reference

Stacked, unlabelled rows are unreadable once there is more than one — a box
could be the display name or the amount, and nothing said whether a row was
an earning or a deduction. Ported the reference's layout:

- **Three labelled fields** per row: Category · Display name · Amount (MYR).
- **Grouped with counts** — `Allowances (1)`, `Deductions (1)`,
  `Expense claims (1)`. An expense claim is an ALLOWANCE-kind row, so
  without its own bucket it hid among the allowances; it is also the one an
  admin is most likely to be hunting for.
- **A statutory strip** under each row, as the reference words it: a kind
  pill, then the bases. For a deduction with `ReducesBase` it reads
  *"Deduction — reduces base · Reduces base for: …"*, because the flags
  there mean the row LOWERS those bases — "Statutory:" would state the
  opposite of what happens. Then the exemption ceiling, cash-neutral,
  TP1, CP38, offsets-PCB, and the amber additional-remuneration note.
- **Three add buttons in the card header** — Allowance, Deduction, Expense
  claim — replacing four buttons at the foot named after catalogue groups.
  Three of the four groups are `Kind = ALLOWANCE`, so one button plus a
  grouped dropdown reaches all of them.
- **The picker is filtered by the row's kind.** An allowance row must not
  be able to become a deduction from the same dropdown; that is what the
  add buttons decide.

The payoff is visible on an expense claim: *"Earning · Statutory: none"* —
it feeds gross and no contribution base, which is exactly the distinction
that was invisible when every row showed the same one-line summary.

### The row action buttons

A bare glyph next to a dense wall of figures reads as decoration rather than
as something clickable. The reference gives its adjust trigger a small
outlined box, and that is what makes it legible:

```
inline-flex h-7 w-7 items-center justify-center rounded-md
border border-border/70 text-muted-foreground
hover:border-primary/40 hover:text-foreground
```

Matched — 28px, bordered, with lucide's vertical `Sliders` at 12px (not
`SlidersHorizontal`, which is a different glyph) and the reference's own
tooltip, "Edit OT / adjustments". The download button beside it takes the
same chrome so the two read as a matched pair rather than one control and
one ornament.

### Run detail, reordered

The adjustments and claims sections sat ABOVE the payslips, on the argument
that they are what the payslips are built from. Wrong priority: the payslips
are what an admin opens the page to read, and two input panels in front of
them pushed the month's actual figures below the fold.

Production orders it the same way — `PayslipsListPanel` at line 198 of the
run page, the claims section at 350 — and reaches adjustments per employee
from the payslip row rather than from a panel above the table.

Now: header and actions · warnings · salary hints · **payslips** ·
adjustments · claims · documents. Each payslip row still has its own adjust
button, so the section below is the way in before a run has been generated
and the full list afterwards.

### The action row moved to the foot

It sat inside the header card, above everything. Production puts the draft
action row at line 605 of the run page — after the payslips (198) and the
claims (350).

The reason is the same one that moved the input panels down: this page is a
review. Warnings, then figures, then the inputs behind them. Approval
freezes figures that feed year-to-date and cascade on a revert, and a commit
button sitting above the thing it commits invites approving first and
reading second.

Three details taken from the reference's row along with the position:

- **Delete is pushed to the far end** (`justify-between`), away from the
  button pressed every time. A destructive action shoulder-to-shoulder with
  a routine one gets hit by muscle memory.
- **Submit is not rendered until payslips exist.** A button that cannot
  possibly apply yet is noise, not guidance.
- **Submit is disabled with the reason on it** — "Regenerate first — these
  payslips are behind their inputs", or "Fix 2 required field(s) above
  before submitting", counted from the readiness result. Previously the
  refusal only arrived after the click, from the server.

### The summary block

It read as a broken table. Each label/value pair carried its own
`border-b`, so the block was a field of half-width rules that looked like
cells which had failed to align — and there was no container holding them,
so they sat loose on the card under the real table's footer.

The reference wraps the same figures in one panel:

```
grid gap-x-8 gap-y-2 rounded-2xl border border-border/60 bg-muted/20
px-4 py-3 text-[12px] sm:grid-cols-2 md:grid-cols-3
```

Matched, with the per-row borders dropped — the tint and the single border
do the containing, which is what the rules were badly standing in for.

Labels shortened to the reference's phrasing too: "Total EPF payment" rather
than "EPF to KWSP (both halves)". The long form squeezed the figure against
its own label in a three-column grid, and "payment" already means the
remittance — which is both halves.

### Run detail, aligned wholesale

Rather than keep adjusting it a piece at a time, the page was diffed against
the reference's run page in full. Three structural differences, not just
styling:

**1. The totals card is gone.** Gross, net and cost were shown in the header
card AND in the table's footer row AND in its summary panel — three copies
of the same three numbers, which is three places for them to disagree. The
reference deleted this card deliberately, and says so in a comment:

> The "Totals" card used to live here, duplicating every number the new
> payslips table already shows in its column headers + summary footer.
> Removed in favour of the single source of truth.

The header is now the reference's: period as an `h1`, `N payroll results on
file` with the generated and approved timestamps, and the status badge on
the right.

**2. Payslips and problems are two tabs.** `PayrollRunTabs` — pill tabs with
counts, the attention one accented amber, both panels kept mounted and
toggled with `hidden` so switching does not throw away a scrolled table.
The tabs collapse to just the payslips when nothing needs attention, so a
clean month never sees a chooser with one real option.

**3. "Needs attention" is one panel, three problems.** `PayrollRunAttention`
keeps them apart because the fix differs:

- **Zero net pay** — the one that blocks submission outright, and only
  visible here since generation produces a payslip regardless. Shown with
  each person's gross and net so it is obvious whether the salary is
  missing or the deductions have eaten it.
- **Missing statutory fields** — the readiness result, as a table, with the
  org-level ones pointed at Settings → Form E.
- **Excluded employees** — often correct (a leaver, a late joiner), so it is
  stated as a fact rather than an error.

Previously these were three amber banners stacked above the figures, seen on
every visit whether or not they applied.

### Both input sections came off the run page

Moving them to the foot was not the fix — they should not have been there.

**One-off adjustments** is gone once a run is generated. Every payslip row
carries its own adjust button, so a list of the same people underneath was
the same thing twice. It still renders when there are NO payslips yet,
because then there are no rows to hang a button on and the list is the only
way in. The reference reaches adjustments exclusively from the row.

**Reimbursed claims** renders only when there is a claim attached or one
available to attach. This org settles claims through Xero, so neither will
ever be true for it, and the card was a permanent explainer for a feature it
does not use. It stays silent while loading too — a skeleton that then
disappears is a flash of a card that was never going to stay.

Neither capability was removed; both are now shown only where they apply.

### The summary was welded to the table

Its wrapper had `px-5 pb-5` and no `pt`, so the panel began the pixel the
table ended. A tinted grid butting straight into the tinted `Total` row read
as one block that had gone wrong rather than as two things.

`pt-5` now matches the header's own padding, so the card has one rhythm top
to bottom: header · table · summary. Measured 45px of separation where there
was none.

It also gained a caption. Two totals blocks stacked with nothing between them
invite the reader to wonder why the numbers differ — the table's footer adds
up its own columns, while the panel is what leaves the bank account for each
agency, which is both halves together. **What gets remitted** says that in
three words.
