# Section: Architecture & Decisions

> Each **"Page:"** below → one OneNote page. These are **ADRs** (Architecture
> Decision Records): *what* we decided, *why*, and the *trade-off*. This is the
> section to read first to understand why the app looks the way it does.

---

# Page: ADR-01 — Decoupled rebuild (strangler-fig)

**Decision:** Rebuild the Next.js/TypeScript monolith as **two decoupled apps** —
a Vite React frontend and an ASP.NET Core backend — one module at a time.

**Why:**
- The monolith mixes UI, business rules, and DB access; hard to test and evolve.
- A hard cutover is risky. A **strangler-fig** migration lets us rebuild module by
  module (Claims → Attendance → Leave → …) while the old app keeps running.
- A separate C# backend can serve web, mobile, and eventually an MCP/API surface
  from one place.

**Trade-off:** two codebases and an HTTP boundary to maintain instead of one repo.
Accepted, because the clean separation and testability are worth it.

**Constraint:** the original monolith is **read-only** — we study it, never modify
it.

---

# Page: ADR-02 — DDD-lite layered backend

**Decision:** Every backend module is **Controller → Service → Repository**, with
strict responsibilities: controllers route + auth, services hold rules + map to
DTOs, repositories are the only place EF Core runs. Cross-module calls go through
the other module's **service**.

**Why:**
- **Testability:** services depend on interfaces, so unit tests inject fakes and
  need no database (this is why we have 41 fast unit tests).
- **Swappability:** the DB lives behind repositories; changing it touches one
  layer.
- **No leaks:** DTO mapping keeps EF entities out of the API surface.

**Trade-off:** more files/boilerplate per feature (interface + impl at each
layer). Accepted; the structure pays for itself in tests and clarity.

**Amended (2026-09-02) — the shared kernel.** The "one documented exception"
this ADR used to record had quietly become the rule: an audit found **21**
services reaching into another module's repository. Fourteen of them were the
same two lookups — *"what is this user's membership in this org"* (17 calls) and
*"who is this user"* — because every module needs to know who the caller is.
That is not modules meddling in each other's business logic; it is a missing
abstraction.

So identity and membership **reads** are now a named shared kernel:
**IDirectoryService** (`Modules/Employees/`). Any module may depend on it.
Employees and Auth keep their repositories private and stay free to change how
they store things.

**Reads only, deliberately.** A module that needs to *create or change* another
module's data has a real domain reason, and that belongs on the owning module's
service where the rules live — not behind a generic lookup.

The remaining seven — reporting and cron reads spread across Dashboard,
Attendance and Leave — were then routed through their owning module's service,
each of which grew the one read method its caller needed
(`IClaimsService.GetAllForOrgAsync`, `IPolicyService.GetAllAcrossOrgsAsync` and
`GetAllPolicyEntitlementsAsync`, `IOvertimeService.GetByEmployeeAsync`,
`IShiftService.GetByIdAsync`, `ITeamService.GetMemberEmployeeIdsAsync`).
`AttendanceService` already injected `IPolicyService` and simply dropped its
duplicate repository.

**21 → 3.** ADR-02 now holds everywhere except three places, each with a reason:

| Service | Reaches into | Why |
|---|---|---|
| `DirectoryService` | `IUserRepository` | It *is* the seam — this is the intended coupling. |
| `EmployeeService` | `IUserRepository` | Creating an employee creates their login; Employees owns that lifecycle. |
| `OrganizationService` | `IOrganizationMembershipRepository` | Creating an org creates the owner's membership, in one transaction. |

The latter two are **writes**, which is why they can't go through the read-only
directory. Neither is settled forever: if Auth grows a service method for
creating a user on an employee's behalf, `EmployeeService` should use it. Listed
here so they stay visible rather than accepted by default.

**The lesson worth keeping:** when a rule is broken 21 times over months and
nobody objects, the code is telling you the rule is wrong — or that something it
needs has no name yet. Amend the ADR or name the thing. An ADR everyone silently
violates is worse than none, because it stops being a signal.

---|---|---|
| `DirectoryService` | `IUserRepository` | It *is* the seam — this is the intended coupling. |
| `EmployeeService` | `IUserRepository` | Creating an employee creates their login; Employees owns that lifecycle. |
| `OrganizationService` | `IOrganizationMembershipRepository` | Creating an org creates the owner's membership, in one transaction. |
| `AttendanceService` | `IEmployeePolicyRepository` | |
| `HoursSummaryService` | `IOvertime` / `IShift` / `ITeamMembershipRepository` | Reporting reads that span modules by nature. |
| `AdminOverviewService` | `IClaims` / `IProjectRepository` | Same — a cross-module dashboard. |
| `LeaveCronService` | `IPolicyLeaveEntitlementRepository` | |

The reporting/dashboard rows are the honest leftovers: a read that aggregates
five modules has no single owner to route through. Revisit if one grows rules of
its own.

**The lesson worth keeping:** when a rule is broken 21 times over months and
nobody objects, the code is telling you the rule is wrong — or that something
it needs has no name yet. Amend the ADR or name the thing. An ADR everyone
silently violates is worse than none, because it stops being a signal.

---

# Page: ADR-03 — Multi-tenancy via global query filters

**Decision:** Enforce org isolation with **EF Core global query filters** on every
**ITenantScoped** entity, driven by the current user's **org** claim, plus
**StampTenant()** to auto-set **OrganizationId** on insert.

**Why:**
- A **single enforcement point** — no query can forget the tenant WHERE clause,
  because it's applied globally by EF.
- Auto-stamping on insert means developers never hand-set **OrganizationId**, so
  they can't set it wrong.

**Trade-off:** global filters are implicit — you must remember they exist when
debugging "why is this row missing?" (answer: the current org filter). Worth it
versus the risk of a cross-tenant data leak from a forgotten clause.

---

# Page: ADR-04 — Policy as the rule bundle

**Decision:** Model per-employee rules as a named **EmployeePolicy** bundle
(module access, attendance enforcement, salary/OT shape, leave entitlement
overrides), resolved via **GetEffectivePolicyAsync** (assigned policy or org
default). Attendance and Leave **ask the policy** rather than hard-coding rules.

**Why:**
- Real HR rules cluster by *type of worker*; a named bundle is reusable and
  self-documenting versus toggling many fields per person.
- Modules stay decoupled from HR policy — attendance just asks "geofence
  required?"; leave asks "how many days?".

**Trade-off:** an extra indirection (employee → policy → rule) and a default-policy
concept to maintain. Accepted; it's how the domain actually works.

---

# Page: ADR-05 — Derived approval chain (not stored per employee)

**Decision:** Derive each employee's approval chain **on the fly** from their
**team's layers** + a per-module approval config, rather than storing an approver
list on each employee. Requests carry a **CurrentStep** and advance through the
derived steps; if an employee is in no team, fall back to their direct supervisor.

**Why:**
- **One edit re-shapes everyone.** Change a team's layers or module config and
  every member's chain updates automatically — no per-employee lists to sync.
- **Per-module chains** on the same team (Leave routes differently from Claims)
  fall out naturally from the config map.
- The supervisor fallback means approvals work **before** any teams are built.

**Trade-off:** **no per-employee approver override yet** — everyone on a team
shares the derived chain. A seam exists in **ApprovalRouter** to add overrides
later. Accepted for now to keep the model simple.

---

# Page: ADR-06 — Approval by team seat, not role

**Decision:** Authorization to approve a request = **being a current-step approver
in the applicant's chain**. Role (Admin/Owner/Supervisor) grants **no** approval
override.

**Why:**
- It matches the **existing production application's** behaviour — admins
  *configure* the system but don't sit in approval flows unless they hold a seat.
- It prevents a privileged role from silently bypassing the intended chain.

**How it's enforced:** approval endpoints use plain **[Authorize]** (any
authenticated user), and the **service** checks the caller is a current-step
approver — otherwise the request is treated as **not found** (404). Verified live
(an Admin approving at step 0 gets 404) and in unit tests
(**ApproveAsync_HidesClaimFromANonCurrentApprover**).

**History:** an earlier version wrongly gave Admin/Owner a blanket override
(**IsOrgApprover**). That was **removed** from the approval path on user
feedback — approval is purely seat-based now.

**Trade-off:** an org with no teams and no supervisors set has no one who can
approve. Mitigated by the supervisor fallback + seeding a supervisor.

---

# Page: Deferred / Not-Yet-Done

Written down so nothing is silently assumed "done."

- **Payroll & payslips** — intentionally **last**. It's money-critical and depends
  on attendance/OT/leave being correct first.
- **Per-employee approver overrides** — chain is team-derived only; the override
  seam exists in **ApprovalRouter** but isn't wired.
- **Multi-team routing** — an employee on multiple teams; current model assumes a
  primary chain.
- **Attendance detail rules** — lateness, breaks, and OT accrual from clock
  events; OT is modeled in policy but not yet computed end-to-end.
- **Per-event attendance approval gates** — attendance can be a module in the
  chain config, but event-level approval isn't wired like Claims/Leave.
- **OT module** — **OtEnabled**/threshold/method live on the policy; the accrual +
  approval flow isn't built.
- **CI/CD automation** — pipeline is proposed, not implemented (see CI-CD
  section).
- **Machine-to-machine auth** — for an eventual MCP/API client hitting the backend
  directly (a service credential rather than a user JWT).

**Also outstanding in the repo right now:** the *admin-approval-removal*
correction (ADR-06) is implemented and tested but **not yet committed**.
