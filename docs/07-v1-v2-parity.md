# v1 → v2 parity comparison

ClaimGuard (`/Users/yongyikang/Empty/ClaimGuard`, Next.js) vs AltomateHR v2
(this repo, .NET + Vite). Module by module: what v1 does, what v2 does, and
whether anything was lost in the rebuild.

**Method.** Source comparison of both codebases, plus live tests against v2
running on localhost with a seeded tenant. Each row is marked:

- **⚙ proven** — reproduced against the running v2 app
- **📖 code** — read from source in both repos, not executed

Findings tie back to Bitrix task **5020 — AltomateHR Testing Feedback**.

---

## Summary

| Severity | Count | Modules |
|---|---|---|
| 🔴 Blocker | 2 | Teams/Roles, Overtime |
| 🟠 Major | 4 | Policy ×3, Employees |
| 🟡 Minor / by design | 5 | Attendance, Overtime, Payroll, Projects, Claims |
| ✅ Parity confirmed | 8 | — |

---

## 🔴 Blockers

| # | Module | v1 (ClaimGuard) | v2 (AltomateHR) | Impact |
|---|---|---|---|---|
| 1 | **Teams / Roles** | `User.role` is re-derived on every team membership change: layer ≥2 in any team → `SUPERVISOR`, else `EMPLOYEE` (`organization.repository.ts:5098`) | No sync. Role is a manual field; team seat is independent | **⚙ proven** — seat a role=`Employee` person as the only approver and the chain routes to them, but `/claims/team` and `/approve` both 403, and the Owner gets 404. Claim stuck `PENDING` with nobody in the org able to clear it. This is the bug already logged as *"Role not change based on hierarchy"* |
| 2 | **Overtime** | `OTSubtype` per request (`LATE_REPLACEMENT` / `OT_OFFSET` / `UNRESOLVED`) decides how approved OT counts toward pay | Policy-level `OtMethod = CASH \| TIME_BANK` instead. Payroll skips cash under `TIME_BANK` on the stated assumption that time was credited — but **nothing ever writes `OtTimeBalanceMin`** (3 references total: entity, DTO, one entity→DTO copy) | **📖 code** — under `TIME_BANK`, approved OT is neither paid nor banked. It vanishes. Check whether any live org has `TIME_BANK` set |

---

## 🟠 Major — settings that save but do nothing

The recurring pattern: an admin-facing switch that persists correctly and is
honoured nowhere. v1 enforces all of these.

| # | Setting | v1 enforcement | v2 | Impact |
|---|---|---|---|---|
| 3 | `canAccessClaims`, `canAccessLeave` | Whole employee portal gated on `moduleAccessForPolicy(policy)` (`layout.tsx`, `employee/page.tsx`) | **Zero references** outside the policy editor, front or back | **⚙ proven** — with both `false`, the employee still lists claims (200), **submits a claim (201)**, reads balances (200) and **applies for leave (200)** |
| 4 | `requireSelfie`, `requireClockOutSelfie` | Enforced in `employee-attendance.service.ts` | **Zero references** outside the policy editor. The only photo check is off-site proof (`OffSiteProofMissing`), a different rule | **⚙ proven** — with both `true`, clock-in with no photo → **200**, clock-out with no photo → **200** |
| 5 | `otSalaryThreshold`, `otDailyThresholdMinutes`, `temporary` | `otDailyThresholdMinutes` drives OT in `attendance-cron.service.ts`; `temporary` used in payroll import + profile | Only read/written by `PolicyService` CRUD. Never consulted | **📖 code** — OT salary cap (EA 1955 covers staff under the threshold) is not applied |
| 6 | **Employees** — bulk onboarding | `EmployeeImportDraft` model backs a multi-step import wizard that **creates** employees | No equivalent. `POST /payroll/employees/import` only **matches existing** people — an unknown email fails *"No employee in this organization matches…"* | **⚙ proven** — a 200-person company must be added one at a time |

---

## 🟡 Minor / arguably by design

| # | Module | v1 | v2 | Note |
|---|---|---|---|---|
| 7 | Policy | `requireGeofence` toggles geofencing per policy | Geofence always enforced from project config; the flag is ignored | Can't turn it **off**. Safer direction than #4, still a lying switch |
| 8 | Payroll | `EmploymentStint` models multiple employment periods (rehire) | Single `PrevIncludesPriorThisOrgPeriod` boolean on the profile | Cruder, but the same YTD double-count is handled |
| 9 | Projects | `EmployeeProjectAssignment` assigns people to projects directly | Project membership is derived from **team** membership | Deliberate (comment: *"so the two can't disagree"*). But you must create a team before anyone can clock in against a project |
| 10 | Attendance | Admin/org can set working hours + OT rates org-wide | Same, plus per-project and per-shift overrides | v2 is richer here |
| 11 | Claims | `ClaimSupportingAttachment` as a table | `SupportingDocumentUrls` JSON on the claim | Equivalent for normal use |

---

## ✅ Parity confirmed — don't chase these

| Area | Finding |
|---|---|
| **Lateness** | v1 sets a bare `LATE` status; v2 computes `LateByMin` and shows "Late 2h 8m". **⚙ proven** (`lateByMin: 123`). v2 is better — the unused `LATE`/`ON_TIME` enum values are vestigial, not missing |
| **On-leave in roll call** | Present in v2, sourced from the leave module rather than the status enum |
| **Dangling approver chains** | v1 has `pruneDanglingApproverChains`; v2 has `TeamsController.HealStrandedApprovalsAsync`, run on the edit that caused it rather than as a sweep. Well reasoned |
| **Attendance edit trail** | v1's `AttendanceEditLog` captures prev→next on approval; v2's approval request carries `originalEventAt` → `eventAt`. Equivalent |
| **Geofence + off-site proof** | Both require a remark/photo when off-site |
| **IP allowlist** | Enforced in v2 (CIDR-aware, with labels and audit) |
| **Auto clock-out** | Enforced in v2 |
| **Leave accrual / carry-forward** | `AccrualMethod` (`LUMP_SUM`/`PRO_RATED`) and `CarryForward` both properly wired in v2 (`LeaveAccrualMath`, `LeaveCronService`) |

---

## Still to compare

Covered deeply: Attendance, Overtime, Policy, Teams/Roles, Claims (access control).
Covered shallowly, worth a second pass: **Leave**, **Payroll**, **Employees**,
**Notifications**, **Audit**, **Xero**, **Dashboard**.

Matches the open items on Bitrix 5020 → *Oscar - System End to End testing*:
Attendance, Leave, Payroll, Employee.
