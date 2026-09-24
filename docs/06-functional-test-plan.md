# Functional test plan & pre-deployment report

Black-box testing from a normal user's perspective, across every module, to
answer one question: **after deploying, can people actually use this without
hitting a critical bug?**

Run date: 2026-09-22 · Automated suite: [`qa/`](../qa/README.md) · **233 checks
passing, 1 confirmed defect.**

---

## 1. The flow

This is the order a real customer meets the product in, and the order the
automated suite runs. Each step lists what to confirm in the UI; the API side is
already automated.

| # | Step | Automated | Check by hand in the UI |
|---|---|---|---|
| 1 | **Sign in** — good password, wrong password, unknown email, sign out | ✅ | error message is readable, not a stack trace |
| 2 | **Create a company** and switch into it | ✅ | company switcher in the header shows both |
| 3 | **Company settings** — name, currency, mileage unit, geofence radius | ✅ | values persist after a reload |
| 4 | **Chart of accounts** — add an expense account and a bank account, set a limit, enable mileage | ✅ | the claim form's account picker shows them |
| 5 | **Projects / sites** — address, map pin, geofence points, IP allowlist | ✅ | map renders, CIDR entries save with labels |
| 6 | **Work schedule** — shifts, working days, lunch, default shift | ✅ | default badge shows on the right shift |
| 7 | **Public holidays** | ✅ | holiday shows in the leave calendar |
| 8 | **Leave types** — 8 are created with the company; add a custom one | ✅ | appears in the employee's leave form |
| 9 | **Policies** — module access, geofence/selfie rules, OT rates, leave entitlements; **edit it**; make it default | ✅ | editing an entitlement changes new employees' balances |
| 10 | **Employees** — create, edit, assign policy + shift | ✅ | duplicate email is refused with a clear message |
| 11 | **Employee profile** — IC, demographics, salary, EPF/SOCSO/tax, bank | ✅ | "incomplete" badge clears once all sections are filled |
| 12 | **Import employees** (CSV) | ✅ | ⚠️ see finding #2 — import *updates*, it does not create |
| 13 | **Company structure** — team, layers, seat people, approval chain | ✅ | chain preview shows the right approver |
| 14 | **Claims** — employee submits expense + mileage → supervisor approves/rejects → admin exports | ✅ | receipt upload and preview; mileage auto-calculates |
| 15 | **Leave** — seed entitlements → employee applies → supervisor approves → balances move | ✅ | ⚠️ see finding #1 |
| 16 | **Attendance** — clock in, break, clock out, request a correction, supervisor approves | ✅ | selfie + GPS capture on a real phone |
| 17 | **Overtime** — photo evidence, submit, approve | ✅ | camera capture on a real phone |
| 18 | **Payroll** — company info → run → generate → submit → approve | ✅ | ⚠️ statutory amounts need a finance review, see §4 |
| 19 | **Payroll output** — payslip PDFs, summary, payment schedule, PCB details, EPF/SOCSO/PCB files | ✅ | open each PDF and eyeball the layout |
| 20 | **Employee payslip** — visible only after the run is approved | ✅ | employee can download their own PDF |
| 21 | **Dashboards, activity log, notifications** | ✅ | activity log records who did what |
| 22 | **Permissions** — employee and supervisor blocked from admin screens | ✅ | admin-only nav items are hidden, not just refused |
| 23 | **Tenant isolation** — company B cannot see company A's data | ✅ | — |
| 24 | **Session** — refresh cookie, reload keeps you signed in, logout kills the session | ✅ | reload the tab mid-session |

---

## 2. What passed

The whole onboarding-to-payroll path works end to end. Highlights worth
trusting:

- **Tenant isolation is solid.** A second company sees zero rows from the first,
  and direct-by-id reads across the boundary return 404 rather than leaking.
  An employee of company A cannot switch into company B.
- **Permissions hold.** Employees and supervisors are refused every admin
  endpoint tested (403, not a crash). Owner-only and superadmin-only routes are
  separately enforced.
- **Sessions are done properly.** Refresh token is httpOnly, `Path=/auth`,
  `SameSite=Lax`, and `Secure` in production. Refreshing after logout is
  refused.
- **Login is rate limited** to 5 attempts/minute — brute force is blocked.
- **The activity log is tamper-evident** and the chain verified clean
  (26/26 entries) after the whole test run. It even records failed sign-ins.
- **Validation is consistent and the messages are human.** Bad dates, negative
  amounts, out-of-range rates, malformed times, invalid months, duplicate
  periods — all refused with a sentence a user can act on.
- **Payroll produced real output:** RM5,000 gross → RM4,440.10 net, employer EPF
  RM650, and valid payslip / summary / payment-schedule / PCB PDFs plus EPF,
  SOCSO-EIS and PCB submission files.
- **Frontend is clean.** Every admin and employee screen rendered with no
  console errors and no failed API calls. Employee portal has no horizontal
  overflow at 375px and uses a proper bottom tab bar.

---

## 3. Findings

### 🔴 #1 — The same leave can be booked twice, and the balance is deducted twice

**Severity: fix before deploy.** `POST /leave` has no overlap check. An employee
can submit the identical date range repeatedly; each one is approvable, and the
balance is deducted for every copy.

Reproduced: two applications for 29–30 Sep 2026, both `APPROVED`,
`takenDays: 4` for 2 calendar days of absence.

`LeaveService.ApplyAsync` validates leave type, date order, half-day rules,
working days and remaining balance — but never checks the employee's existing
applications for the same dates. Payroll and leave reporting then both
double-count.

Fix: in `ApplyAsync` (and `ApplyOnBehalfAsync`), reject when a `PENDING` or
`APPROVED` application for the same employee overlaps the requested range.

### 🟠 #2 — There is no way to bulk-create employees

**Severity: decide before onboarding a real customer.** "Import employees"
(`POST /payroll/employees/import`) only **matches people who already exist** —
an unknown email fails with *"No employee in this organization matches …"*. It
enriches payroll data; it does not onboard anyone.

So a 200-person company must be created one at a time through *Add employee*.
Not a bug — the import does what it says — but it is probably not what the word
"import" leads a customer to expect.

Options: let the import create missing employees (with a default policy and an
invite email), or rename it *"Update payroll details"* in the UI so nobody
plans a migration around it.

### 🟡 #3 — Run readiness passes, then the bank file refuses

A payroll run reports `readiness: ok` and submits and approves happily, but
`documents/bank-file` then returns 409: *"No Public Bank payor account number is
set."* The payor bank account is not part of the readiness check, so the admin
only discovers it at the last step. Add it to readiness.

### 🟡 #4 — A one-layer team silently auto-approves everything

Approval layers are 0-based and an approver must sit *above* the member, so a
team created with `layerCount: 1` can never have an approver. Leave and claims
from that team are then auto-approved on submission (by design — *"no approver
above the applicant"*), with nothing telling the admin their approval workflow
is inert. Worth a guard or a warning in the team editor.

---

## 4. What I could not test — over to you

None of these are blocked by bugs; they need credentials, hardware, or domain
judgement I do not have.

**Needs your sign-off (highest value):**

1. **Are the statutory numbers correct?** I confirmed payroll computes and files
   generate — I did **not** verify EPF, SOCSO, EIS, PCB or HRDF amounts against
   current Malaysian rates and tables. Please have finance check one payslip and
   one of each statutory file by hand. This is the single highest-risk item.
2. **Do the submission files import?** The EPF, SOCSO/EIS and PCB files are in
   `qa/.out/run-*.txt`. Only a trial upload to the real portals proves the
   formats.
3. **Are the PDFs right?** Payslip, summary, payment schedule, PCB details and
   the leave summary are in `qa/.out/`. I confirmed they generate and open; I
   cannot judge whether the layout and wording match what you want to send out.
4. **LHDN annual forms** (EA / E / CP8D) — generated, not validated.

**Needs credentials or external systems:**

5. **Xero** — reported disconnected throughout, so claim→bill sync, payroll sync,
   and account/project import are all untested. Needs a real connected tenant.
6. **Email** — forgot-password and digest emails. The endpoint returns 204 but I
   never saw an inbox. Needs SMTP configured.
7. **Web push notifications** — needs a real browser subscription and VAPID keys.
8. **Appraisify SSO / partner API** — needs the partner app running.

**Needs a real device:**

9. **Selfie, GPS geofence and IP allowlist enforcement.** I tested with these
   policy switches off. Please test with them **on**: clock in outside the
   geofence, off the office network, and without a selfie — each should be
   refused or flagged.
10. **Real phones** — camera capture, PWA install, and behaviour on a flaky
    connection.

**Needs the production environment:**

11. **Everything above was run against local MySQL in `Development` mode.**
    Production differs in ways that break things independently: user-secrets are
    not loaded, the seeder is off, the cookie `Secure` flag turns on (so HTTPS
    must work end to end), CORS applies, and nginx rewrites `/api`. Please
    repeat at least steps 1, 2, 14 and 18 of the flow on the deployed
    environment before opening it to customers.
12. **Load and concurrency** — two supervisors approving the same claim at once,
    a payroll run while people are clocking in.

---

## 5. Housekeeping

The suite created throwaway companies (`QA Test Sdn Bhd …`, `QA Isolation Co …`)
in the **local** MySQL only. Nothing was written to the shared DigitalOcean
database. Delete them whenever you like, or drop and re-seed the local DB.
