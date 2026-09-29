# Launch runbook — known issues, fixes, and how to diagnose

A record of what testing found in the week of 21–28 Sep 2026, what was fixed,
and how to recognise the same problem next time. Written for whoever picks up
a production issue — a person or a Claude session — so the first response is
fast and correct instead of rediscovered.

**Status verified 28 Sep 2026 against `main` @ `b2db88a`,** by re-running each
reproduction on a local build, not by reading code. Re-verify before relying on
a row: §7 says how.

- Live v2: `altomatehr-v2.fusioneta.com.my` · Live v1: `hr.altomate.io`
- v1 source (read-only reference): `/Users/yongyikang/Empty/ClaimGuard`
- Related: [06 functional test plan](06-functional-test-plan.md) ·
  [07 v1 → v2 parity](07-v1-v2-parity.md) · smoke suite in [`qa/`](../qa/README.md)

---

## 1. Open issues that affect users — fix or work around before launch

| # | Issue | What a user sees | Workaround today | Root cause |
|---|---|---|---|---|
| 1 | 🔴 **Approvals deadlock** | Claims and leave stuck `PENDING` forever. The approver gets 403, and the Owner can't approve either (404) | When you seat someone as an approver in a team, **set their role to Supervisor by hand** | Role is a manual field; team seat is separate. v1 re-derives role from the team layer on every membership change (`organization.repository.ts`, `desiredRole`). v2 `TeamService` does not, but `/claims/team` and `/approve` are gated on the role |
| 2 | 🔴 **Double payment** | An employee on `CASH` or `CHEQUE` who still has a bank account on file is also paid by the bank file | Before generating the bank file, **clear the bank account on every cash/cheque employee** | `PayrollBankRows.Select` filters only on net pay > 0 and account present. `PaymentMethod` is loaded onto the row in `StatutoryFileService` and never checked. v1 has the same gap |
| 3 | 🔴 **Time-bank overtime vanishes** | Approved OT under a `TIME_BANK` policy is neither paid nor banked | **Don't use `TIME_BANK`** — use `CASH` | Payroll skips cash OT under `TIME_BANK` (`cashOt` in `PayrollRunService`), assuming time was credited. Nothing ever writes `OrganizationMembership.OtTimeBalanceMin` |
| 4 | 🟠 **Selfie rule does nothing** | "Require selfie" on a policy saves, but on-site clock-in and clock-out with no photo both succeed | None — don't promise customers it's enforced | `EmployeePolicy.RequireSelfie` / `RequireClockOutSelfie` are never read by `AttendanceService`. Only off-site proof and a no-location clock-out (`4588e65`) demand a photo |
| 5 | 🟠 **Same leave booked twice** | Two identical applications both approve; the balance is deducted twice | Approvers check for duplicates. Admins can cancel approved leave to return the days (`05b4e46`) | `LeaveService.ApplyAsync` checks type, dates, working days and balance, but not the employee's existing applications |
| 6 | 🟠 **Zero SOCSO, silently** | An employee with no SOCSO scheme contributes nothing, and run readiness still says OK | After any import, **check every employee has a SOCSO scheme** | A null `SocsoScheme` means no SOCSO. `PayrollProfileReadiness` only flags a scheme with no number. The import now reads a `socsoScheme` column, but a blank cell is still null |
| 7 | 🟡 **Policy OT fields ignored** | OT salary cap, daily OT threshold and "temporary" save and change nothing | — | Read only by `PolicyService` CRUD. The EA 1955 OT salary cap is not applied |
| 8 | 🟡 **Lunch break and OT day length ignored** | Org "Default work schedule → Lunch" changes nothing; OT always assumes an 8-hour day | Set lunch on the **Shift**, which attendance does read | `Organization.LunchBreakMinutes` is never read. `PayslipCalculator`'s `DailyHours` is never set, so `OvertimePay.DefaultDailyHours = 8`. Wrong OT rate for any non-8-hour day (EA 1955 s.60I) |
| 9 | 🟡 **No bulk employee creation** | "Import employees" only updates people who already exist | Create each employee by hand, then import | v1's import wizard (`EmployeeImportDraft`) creates them; v2's payroll import only matches |
| 10 | 🟡 **Settings accept values the bank file refuses** | A 9-digit Public Bank account saves fine and fails on payday | Check the bank settings well before payday | Validation lives at file generation, not save. Two refusal messages also say "Payroll Settings → Company Info" when the bank picker is under General |

Issues 1–3 cost money or block work outright. Fix them before any customer runs a live payroll.

---

## 2. Fixed during the week — don't re-investigate

| Commit | Fixed |
|---|---|
| `8090831` | Policy `canAccessClaims` / `canAccessLeave` now enforced — **verified 28 Sep** |
| `7cfe3f2` | Employee dashboard showed invented figures (RM 4,820.00 / 3 / 8 / 12) — **verified 28 Sep** |
| `e1766e5` | YTD import accepts v1's 3-sheet format; the template matches it |
| `a4f2097` | PCB inflated after a migration: TP3 now gated by year; imported allowances get their exemption ceiling |
| `12d0ec9`, `4166ed0` | An imported run offers payslips only — enforced in the service and hidden in the UI |
| `dcb1a83` | Each reimbursed claim appears by name on the payslip |
| `344272d` | CIMB no longer invents an ID-type code for passport holders |
| `27b55c0` | "Today" is Malaysia's business day, not UTC's |
| `ce69f97` | Admin module grants actually restrict what an admin can reach |
| `a8e2eb6` | Drafts are marked for a re-run when their inputs change |
| `2c0236e` | An approved time correction applies to the shift it corrects |
| `727323a` | Add employee requires an employee ID, so payroll readiness stops failing on it |
| `05ecb2a` | Pending breaks no longer counted twice |
| `38995c6`, `f75ddd3` | Statutory files and run documents match v1; both SOCSO files offered |

---

## 3. Diagnostic playbooks

### "v1 and v2 give different payroll figures"

1. **Compare column by column.** If gross, EPF, SOCSO, EIS and SKBBK all match and only PCB differs, the cause is the *inputs* to PCB — year-to-date or reliefs — not the statutory tables.
2. **Check when the run was generated** against when the fix was deployed. A draft generated before a deploy keeps the old figures until regenerated. `a8e2eb6` now flags these.
3. **Reconcile with arithmetic.** The monthly PCB gap ≈ disputed taxable amount × the employee's top tax rate ÷ months left including this one. Peak Bridges, Sep 2026: Toh's imported travel allowance was 2,500 × 25% ÷ 4 = **156.25**, which was exactly the gap. If the gap reconciles, you've found the cause.
4. **Usual suspects, in order:** imported allowances treated as taxable (travel on official duty is exempt to RM 6,000/yr); TP3 prior employment with no year; marital status or `spouseWorking` unset; SOCSO scheme blank.
5. **Don't assume v1 is right.** v1 still taxes imported exempt allowances, so for migrated employees it over-deducts.

### "YTD import says: The sheet is empty"

The file is v1's 3-sheet format and the server predates `e1766e5`. The old parser reads sheet 1, which is 📕 Instructions. Check what the live site is running (§5). Workaround: use a file in the current single-sheet format.

### "The Import 2026 history button won't enable"

By design: it only enables after **Check the file** returns clean, and choosing a new file clears the check. If the check itself fails, the message says why — unmatched ICs are the common case.

### "Bank file refused" vs "bank rejected the file"

- **Refused by us (409):** read the message. It names the missing setting — payor account, org code, a Public Bank account that isn't 10 digits, a passport holder on CIMB.
- **Rejected by the bank:** check the format's confidence below. No v2 format has yet been confirmed by a real bank upload.

| Format | Built from | Matches v1 |
|---|---|---|
| Maybank M2E TXT | Published spec v4.3 | Yes, except the reference field |
| Public Bank ECP XLSX | Published spec | All values; sheet name `Payroll` → `Payment` |
| HLB Connect First TXT | HLB sample — NRIC path only | Byte-identical |
| HLB ConnectBiz XLSX | HLB template v1.1 | All values |
| CIMB BizChannel TXT | **One sample; 3 positions guessed** | Yes, except reference and header name |

### "Approvals stuck PENDING"

Almost always issue #1. Check the approver's **role**, not just their team seat. The chain routes by seat, but the approve endpoints check role.

### "The same employee was paid twice"

Issue #2. Check their payment method and whether a bank account is still on file.

### "A setting saves but does nothing"

The pattern behind issues 3, 4, 7 and 8. Search for the field outside its own module — CRUD mapping doesn't count as use. Then prove it: toggle the setting and repeat the action. Several were found this way, and one — module access — was fixed.

### "Merged, but the live site hasn't changed"

Deploy is **manual**. The GitHub Deploy workflow needs `DROPLET_HOST`, `DROPLET_SSH_KEY` and `DROPLET_USER`, none are set, and it has never run. Someone has to pull `main` on the droplet and rebuild.

### "Local testing shows real companies, or my test org has vanished"

The local backend is on the **shared DigitalOcean database**. `ConnectionStrings:Default` in user-secrets points there, so any backend started without the override uses it — including one started by another session. Check `/auth/orgs`: real names like "Fusioneta Sdn Bhd" mean shared DB. Restart with the override in §5.

### "A PR shows a merge conflict"

Preview it without touching files: `git merge-tree --write-tree --name-only origin/main <branch>`. Resolve by keeping **both** sides' intent — don't just pick one. Then check what git can't see: whether the other side added a new route around something yours protects. Example: `/download` bundles files the imported-run gate covers; it was checked and passes through the gate.

### "Behaviour doesn't match the code I just read"

Stale binary. Rebuild and restart the backend before testing — a stale process once made five working import columns look broken.

---

## 4. Before each customer goes live

- [ ] Every employee's **SOCSO scheme** is set (issue #6)
- [ ] Nobody on cash or cheque still has a bank account on file (issue #2)
- [ ] Every team approver has role **Supervisor** (issue #1)
- [ ] No policy uses `TIME_BANK` (issue #3)
- [ ] Payroll bank, payor account and org code are set and produce a file — generate one from a test run
- [ ] YTD imported, if mid-year; TP3 filled **with a year** for anyone with a previous employer this year
- [ ] Marital status and `spouseWorking` are correct — they drive PCB reliefs
- [ ] First run's PCB compared against v1 or the LHDN calculator for at least one employee

---

## 5. Environment facts

| Fact | Detail |
|---|---|
| Local backend against the local DB | `cd backend && ConnectionStrings__Default="$(dotnet user-secrets list \| sed -n 's/^ConnectionStrings:Local = //p')" Seed__DemoData=true ASPNETCORE_ENVIRONMENT=Development dotnet run --launch-profile http` |
| Seeded logins (local only) | `admin@` / `supervisor@` / `employee@altomate.com`, password `password123` |
| Login rate limit | 5 per minute → HTTP 429. The `qa/` helpers back off automatically |
| Vite dev server | Listens on IPv6 `[::1]:5173` only — `nc 127.0.0.1 5173` reports closed while `curl localhost:5173` works |
| Shared checkout | Several sessions edit this working tree at once. Check `git status` for others' uncommitted work, and never commit files you didn't change. `mock-data/` is untracked and not part of this work |
| zsh | Don't name a shell variable `path` — it's tied to `PATH` and breaks every command after it |
| Deploy | Manual. Merging to `main` changes nothing live (§3) |

---

## 6. Not yet tested — needs a person or an external system

- **Statutory amounts** against the LHDN, KWSP and PERKESO calculators — their sites weren't reachable from the test environment
- **Bank and statutory uploads** to real portals: bank files, EPF i-Akaun, PERKESO ASSIST, LHDN e-PCB. CIMB first — its format is partly guessed
- **EA, Form E, CP8D** against LHDN's validator
- **Real phones:** camera, GPS geofence, PWA install
- **Production config:** secure cookies, CORS, the nginx `/api` rewrite, Redis
- **Xero while connected**, **email** (payslips, password reset), **web push**
- **New this week, untested by anyone:** payslip emails, inbound SSO, partner and onboarding APIs, and the 26-company v1 migration (`44198f1`)

---

## 7. Re-verifying this document

Each open issue in §1 was reproduced end to end on a local build. To re-check on a later `main`: build, start the backend against the local DB (§5), then for each row do what the "What a user sees" column describes and see whether it still happens. The reproduction scripts from this pass follow the `qa/` helpers — `qa/lib.sh` provides `login`, `req` and `reqfile`.

When a row is fixed, move it to §2 with its commit hash and the date you verified it.

**Keep the support knowledge base in step.** Every open issue here has a
user-facing entry in [`kb/`](kb/README.md), which is what customers are told.
When a row changes, update that entry's `status` and `verified` too — and when
testing finds a new issue, add both a row here and an entry there.
