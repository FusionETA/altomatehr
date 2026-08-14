# Section: Back-End

> Each **"Page:"** below → one OneNote page under the **Back-End** section.

---

# Page: Stack & Project Layout

**Stack**

| Concern | Choice |
|---|---|
| Runtime | **ASP.NET Core (.NET 10)**, C# |
| ORM | **EF Core** + **Pomelo** provider for **MySQL/MariaDB** (DigitalOcean) |
| Auth | **JWT** access tokens + opaque **refresh tokens**; **BCrypt** for password hashing |
| Config secrets | **.NET user-secrets** (DB connection string, JWT signing key) — never committed |

Runs at **http://localhost:5001**.

**Project layout — organised by module, not by technical layer:**

```
backend/
├── Program.cs               ← DI registration + middleware pipeline
├── Common/                  ← ICurrentUser, CurrentUser, ITenantScoped
├── Data/
│   ├── AppDbContext.cs      ← DbSets, query filters, StampTenant
│   └── DbSeeder.cs          ← demo org/users/data
└── Modules/
    ├── Auth/                ← User, RefreshToken, JWT, employees, supervision
    ├── Organizations/
    ├── Projects/
    ├── Accounts/            ← chart of accounts, spend limits, mileage
    ├── Claims/
    ├── Attendance/
    ├── Leave/
    ├── Policies/
    └── Teams/               ← hierarchy + approval chain
```

Each module folder holds its **Entities/**, **Dtos/**, its **Service**, its
**Repository**, its **Controller**, and the matching interfaces. Everything about
"claims" lives under **Modules/Claims**. This mirrors the frontend's feature split.

---

# Page: Layered Architecture (Controller → Service → Repository)

This is the single most important backend rule. Every request flows through
**three layers**, and each layer has exactly one job:

```
HTTP request
   │
   ▼
Controller     → routing + auth attributes ONLY. No business logic.
   │             Reads the caller id from the JWT, calls a service.
   ▼
Service        → the rules live here. Validates, orchestrates,
   │             maps Entity → DTO (ToDto). NEVER touches EF/DbContext.
   ▼
Repository     → the ONLY place EF Core / DbContext runs.
   │             CRUD against the database. Returns entities.
   ▼
Database (MySQL)
```

**The hard rules:**

1. Controllers call **services only**.
2. Services call **repositories only** — never DbContext directly.
3. Repositories are the **only** place EF Core is used.
4. A service maps entities to **DTOs** (**ToDto(...)**) before returning — the API
   never leaks raw EF entities to the client.

**Cross-module calls** go through the *other module's service*, not its
repository. Example: **ClaimsService** needs spend limits, so it depends on
**IChartOfAccountService** — not on the accounts repository.

*(One documented pragmatic exception: **PolicyService** reads **IUserRepository**
directly to get a user's **PolicyId**. It's noted in code so it doesn't become a
precedent.)*

**Why bother with three layers?** Each layer is independently testable and
swappable. We unit-test services with **fake repositories** (no database needed —
see the Testing section). If we ever swap MySQL for something else, only the
repository layer changes.

---

# Page: Multi-Tenancy

The app is **multi-tenant**: many organizations share one database, and an
organization must **never** see another org's rows. This is enforced in **one
place** so no query can forget it.

**The pieces:**

- **ITenantScoped** — a marker interface with an **OrganizationId** property.
  Every tenant-owned entity (Claim, LeaveApplication, Team, EmployeePolicy, …)
  implements it.
- **ICurrentUser / CurrentUser** — reads the current request's identity from
  the JWT via **IHttpContextAccessor**. Exposes **UserId**, **OrganizationId**,
  **Role**.
- **EF Core global query filters** (in **AppDbContext.OnModelCreating**) — for
  every **ITenantScoped** entity:

  ```csharp
  builder.Entity<T>().HasQueryFilter(e =>
      _currentUser.OrganizationId == null ||
      e.OrganizationId == _currentUser.OrganizationId);
  ```

  This means **every** query EF generates automatically appends
  **WHERE OrganizationId = @currentOrg**. A developer *cannot* accidentally read
  another org's data — the filter is applied globally.

- **StampTenant()** — on SaveChanges, any newly-added **ITenantScoped** entity
  gets its **OrganizationId** auto-set to the current user's org. You never set it
  by hand on insert.

**The JWT carries an org claim**, which is how **CurrentUser** knows the tenant
for the request. Login issues the token with the user's organization baked in.

**Why filters instead of manual WHERE?** Manual filtering is one forgotten clause
away from a cross-tenant data leak. A global filter is correct by default and
impossible to forget.

---

# Page: Authentication (JWT + Refresh)

**Two tokens, two lifetimes:**

| Token | Lifetime | Storage | Purpose |
|---|---|---|---|
| **Access** (JWT) | 15 min | client memory | proves who you are on each request |
| **Refresh** (opaque) | 7 days | DB row + httpOnly cookie | mints new access tokens |

**Access token (JWT):**
- Issued by **ITokenService.CreateToken(userId, email, role, organizationId)**.
- Claims include the user id, email, **role**, and **org** (organization id).
- Signed with a key stored in user-secrets. Short-lived on purpose.

**Refresh token:**
- A random opaque string, its hash stored in a DB row (per user/device).
- Sent to the browser as an **httpOnly cookie**, **Path=/auth**, **SameSite=Lax**.
- **Rotation:** each refresh consumes the old token and issues a new one, so a
  stolen refresh token has a short useful window and reuse can be detected.

**Passwords:** hashed with **BCrypt**. Plaintext is never stored or logged.

**Endpoints (Auth module):**
- **POST /auth/login** → access token in body + refresh cookie.
- **POST /auth/refresh** → new access token (reads the refresh cookie).
- **POST /auth/logout** → revokes the refresh token, clears the cookie.

**Authorization on endpoints** uses **[Authorize]** and, where a screen is
admin-only (settings), **[Authorize(Roles = "Admin,Owner")]**. **But note:**
approval endpoints deliberately use plain **[Authorize]** — approval permission is
decided by *team seat*, not role (see the Teams page).

---

# Page: Roles & Employee Management

**Roles** (Employee, Supervisor, Admin, Owner) exist and gate *configuration*
screens (only Admin/Owner can edit settings, policies, teams).

**But roles are NOT how approvals are decided.** A person approves a request
because they occupy a **team seat above the applicant**, not because their role is
"Supervisor". An Admin with no seat in the applicant's chain cannot approve. This
was a deliberate correction to match the existing production app.

**The Employee admin surface** (**EmployeeService** / **EmployeesController**)
lets an Admin set, per employee:
- **Role** — menu visibility + settings access.
- **SupervisorId** — the direct supervisor (used as a fallback approver when an
  employee isn't in any team; see Approval Chain).
- **PolicyId** — which policy bundle governs them (null = org default).

**Supervision** (**ISupervisionService**) is the small service that answers:
- **GetSupervisorIdAsync(employeeId)** — who's the direct supervisor?
- **GetReportIdsAsync(supervisorId)** — who reports to me?
- **GetEmailsAsync(userIds)** — resolve ids → emails (used to show applicant email
  in an approver's queue and for notifications).

---

# Page: Module Catalog

A quick reference of the business modules and what each owns.

| Module | Core entity | Responsibilities |
|---|---|---|
| **Organizations** | Organization | name, currency, default mileage rate, **geofence radius** |
| **Projects** | Project | project list + **geofence center coordinates** (lat/lng) |
| **Accounts** | ChartOfAccount | expense categories, **spend limits** (amount/period/scope), **mileage** flags/rates |
| **Claims** | Claim | file/track expense & mileage claims, over-limit flag, approval routing |
| **Attendance** | AttendanceRecord | clock in/out, **geofencing**, off-site remark+photo, KL-day bucketing |
| **Leave** | LeaveType, LeaveApplication | leave types, apply, **balances**, approval routing |
| **Policies** | EmployeePolicy | rule bundles governing attendance enforcement + leave entitlement |
| **Teams** | Team, TeamMembership | hierarchy layers + membership + **derived approval chain** |

**Two modules deserve their own page** because they cut across everything else:
**Policies** and **Teams/Approval chain**. Next two pages.

---

# Page: Policies (deep)

A **policy** is a *named bundle of rules* attached to an employee. Instead of
scattering "does this person need a selfie? how many AL days?" across the code, we
put it in one entity and let other modules ask the policy.

**EmployeePolicy fields (grouped):**

- **Identity/lifecycle:** **Name**, **IsDefault** (one per org), **IsArchived**,
  **Temporary**.
- **Module access:** **CanAccessAttendance**, **CanAccessClaims**,
  **CanAccessLeave**.
- **Attendance enforcement:** **RequireGeofence**, **RequireSelfie**,
  **RequireClockOutSelfie**.
- **Payroll shape:** **SalaryType** (HOURLY | MONTHLY).
- **Overtime:** **OtEnabled**, **OtDailyThresholdMinutes**, **OtMethod** (CASH |
  TIME_BANK).
- **Leave overrides:** a child collection **PolicyLeaveEntitlement** — per leave
  type, how many days *this policy* grants (overrides the leave type's default).

**How an employee resolves to a policy:** **GetEffectivePolicyAsync(employeeId)**
returns the employee's assigned policy (**User.PolicyId**) or, if none, the **org
default** policy.

**How other modules use it (the "retrofit"):**

- **Attendance** calls **RequiresGeofenceAsync(employeeId)** — attendance only
  enforces geofencing if the person's effective policy says so. A field worker's
  policy can turn it off.
- **Leave** calls **GetLeaveEntitlementsAsync(employeeId)** — returns a map of
  leaveTypeId → days, so balances use the policy override when present, else the
  leave type default. (Verified live: AL default 14 → policy override 8.)

**Why a bundle?** Real HR rules cluster by *kind of worker* ("Full-time office",
"Part-time hourly", "Contractor"). A policy is that cluster, named once and
reused, instead of toggling ten fields per employee.

---

# Page: Teams & the Approval Chain (deep)

This is the heart of the rebuild. Approvals are **not** "a supervisor says yes" —
they follow a **team hierarchy**, can be **multi-step**, and differ **per module**.

**The structures:**

- **Team** — belongs to a **Project**. Has:
  - **LayerCount** — how many levels (e.g. 3: Staff / Lead / Manager).
  - **LayerLabels** — JSON array naming each layer.
  - **ModuleApprovalConfig** — JSON map: for each module (CLAIMS, LEAVE, OT,
    ATTENDANCE), which **layers** act as approvers.
- **TeamMembership** — puts an employee on a team at a specific **Layer**.
  Unique per (TeamId, EmployeeId).

**The key idea — the chain is DERIVED, not stored per employee.**
**ApprovalChainService.GetChainAsync(employeeId, module)**:

1. Find the employee's team + their layer.
2. Walk **upward** through the layers above them.
3. For each higher layer, include it as an **approval step** — *unless* the
   module's **ModuleApprovalConfig** excludes that layer (so Leave and Claims can
   have different chains on the same team).
4. **Skip empty layers** (a layer with no members isn't a step).
5. Empty config for a module = that layer auto-approves (no gate).

Result: an ordered list of **steps**, each step = the approver ids at that layer.

**Resolving "who approves right now" —** **ApprovalRouter**:
- **CurrentApproversAsync(module, applicantId, currentStep)** → the approver ids
  for the step the request is currently on.
- **StepCountAsync(module, applicantId)** → how many steps total.
- **Fallback:** if the employee is in **no team**, the chain is a single step =
  their **direct supervisor** (**SupervisorId**). So the system still works before
  teams are set up.

**How a request moves through the chain:** Claim and LeaveApplication each carry a
**CurrentStep** integer.
- On approve, if **CurrentStep + 1 >= stepCount** → **final approval**, status
  becomes **APPROVED**. Otherwise **CurrentStep++** and it stays **PENDING**, now
  visible to the next layer.
- A queue (**GetTeamAsync**) shows an approver only the requests **where they are a
  current-step approver** — so a step-0 approver stops seeing a request once it
  advances to step 1.

**Authorization = being a current-step approver.** No role override. An Admin who
isn't in the chain gets a 404 (treated as not-found), verified live and in tests.

**Endpoints (Teams module):** **GET/POST/PUT/DELETE /teams**, **POST
/teams/{id}/members**, **DELETE /teams/{id}/members/{employeeId}**, and a preview
**GET /teams/chain/{employeeId}?module=CLAIMS**. Team config is Admin/Owner only.

**Why derive instead of store?** One team edit re-shapes every employee's chain
automatically — there's no per-employee approver list to keep in sync. The
trade-off (no per-employee approver override yet) has a seam ready in
**ApprovalRouter** for later.

---

# Page: Data, EF Core & Migrations

**Database:** MySQL/MariaDB on DigitalOcean, via the Pomelo EF Core provider.

**AppDbContext** is the center of the data layer:
- Declares a **DbSet** per entity (Claims, LeaveApplications, Teams,
  EmployeePolicies, PolicyLeaveEntitlements, …).
- Applies the **global query filters** for multi-tenancy (previous page).
- Overrides **SaveChanges/SaveChangesAsync** to run **StampTenant()**.
- Declares **indexes** and **unique constraints** (e.g. (ProjectId, Name) on
  Team, (TeamId, EmployeeId) on TeamMembership).

**EF conventions we use:**
- **Enums stored as strings** via **.HasConversion<string>()** — so the DB shows
  **APPROVED**, not **2**. Readable, and reorderable without breaking data.
- **[NotMapped]** for transient DTO-ish fields that aren't columns (e.g.
  **Claim.EmployeeEmail**, filled in by the service for the approver queue).
- **[Precision]** on money/decimal columns.
- **String GUID primary keys** (e.g. **usr-emp**, **org-altomate**, generated
  ids) — easy to seed and reason about.

**Migrations:**
```bash
dotnet ef migrations add <Name>    # generate a migration from model changes
dotnet ef database update          # apply pending migrations
```
Every schema change (new field, new entity, new index) is a migration checked in
with the code.

---

# Page: Seeding & Demo Accounts

**Data/DbSeeder.cs** builds a working org on an empty database so the app is
usable immediately and tests/manual click-through have real data.

**What it seeds (SeedAsync(...)):**
- **Organization org-altomate** — with a non-zero **geofence radius (200 m)**.
  *(Lesson learned: C# property initializers do NOT set DB column defaults, so an
  early row got radius 0 and geofencing silently passed. Fixed in the seeder + a
  live PUT.)*
- **Users** (password **password123** for all):
  - **usr-admin** — Admin
  - **usr-super** — Supervisor
  - **usr-emp** — Employee, with **SupervisorId = usr-super**
- **Leave types:** AL (14), MC (14), UL (0).
- **Policy:** a default "Full-time" policy (IsDefault, RequireGeofence, MONTHLY).

This gives a complete approval path out of the box: **usr-emp** files →
**usr-super** (as fallback supervisor, or via team seat) approves.

**Demo login:** any seeded email + **password123**.
