# Section: Back-End Testing

> Each **"Page:"** below → one OneNote page under the **Back-End Testing** section.

---

# Page: Approach & Tooling

**Tooling**

| Concern | Choice |
|---|---|
| Test framework | **xUnit** |
| DB for tests | **EF Core InMemory** (when a real DbContext is needed) |
| Integration host | **WebApplicationFactory** (spins the API in-process) |
| Test doubles | **hand-written fakes**, injected via constructor |

Run everything with:

```bash
dotnet test
```

Current status: **41 tests passing.**

**What we test — services, not controllers.** Because all the rules live in the
**service** layer (controllers only route, repositories only run EF), the service
is the thing worth testing. We construct a service with **fake repositories** and
assert on behaviour. No database, no HTTP — fast and deterministic.

**Why this is possible at all:** the layered architecture. A service depends on
*interfaces* (**IClaimsRepository**, **IApprovalRouter**, **ISupervisionService**,
**IPolicyService**), so a test hands it fakes. If services called DbContext
directly, none of this would be unit-testable.

**The shape of a service test:**

```csharp
var service = CreateService(
    [ NewClaim("claim-1", "usr-emp", ClaimStatus.PENDING) ],
    router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-approver"]] }));

var result = await service.ApproveAsync("claim-1", "usr-approver");

Assert.True(result.Transitioned);
Assert.Equal(ClaimStatus.APPROVED, /* the claim */.Status);
```

---

# Page: Test Doubles (fakes)

Shared fakes live in **ClaimsTestDoubles.cs** and are reused across suites
(Leave tests import them too). A "fake" is a real in-memory implementation of an
interface — not a mock library. It behaves like the real thing, minus the
database.

**The main fakes:**

- **FakeClaimsRepository / FakeLeaveApplicationRepository /
  FakeLeaveTypeRepository** — hold a List in memory and implement the repo
  interface (GetById, GetByEmployee, Add, Update, …). Update is a no-op because
  the test already holds the entity reference and asserts on it directly.

- **FakeApprovalRouter** — the most important one for approval tests. It's
  configured with a dictionary mapping *applicant → ordered steps*, each step a
  list of approver ids:
  ```csharp
  // single approver:
  new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] });
  // two-step chain:
  new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"], ["usr-mgr"]] });
  ```
  **CurrentApproversAsync(module, applicant, step)** returns the approvers at that
  step (or empty if out of range); **StepCountAsync** returns the number of steps.
  This lets a test model any chain shape without building real teams.

- **FakeSupervisionService** — supervisor lookups + email resolution
  (GetSupervisorIdAsync, GetReportIdsAsync, GetEmailsAsync).

- **FakePolicyService** — returns configured leave entitlement overrides and a
  geofence flag, so Leave balance tests can assert the policy override wins.

- **FakeChartOfAccountService**, **FakeClaimReceiptStorage** — spend-limit
  lookups and a no-op receipt store.

**ClaimsTestFactory.CreateService(...)** wires all of these with sensible
defaults, so a test only overrides the fake it cares about:

```csharp
CreateService(claims, router: SingleApprover());   // everything else defaulted
```

---

# Page: Coverage Map

What the 41 tests actually assert, by suite.

**ClaimsServiceApprovalTests** — the approval state machine:
- Pending → Approved on approve; Pending → Rejected + stores review notes.
- Missing claim → not found.
- Non-pending claims (Approved/Rejected/Reviewed/Submitted) **don't** transition,
  with the right error message.
- **A non-current approver — even an Admin — is hidden (not found).** Approval is
  by seat, not role.
- **Multi-step chain advances:** step-0 approver advances it, then can no longer
  act; step-1 approver finalizes.

**ClaimsServiceOwnershipTests** — ownership & visibility:
- **CreateAsync** stamps the authenticated user's id as the owner.
- **GetMineAsync** returns only your own claims.
- **GetTeamAsync** returns only claims where you're the **current-step** approver.
- An employee can't update another user's claim; updating preserves the owner.

**LeaveServiceTests** — leave rules:
- Inclusive day-span math (1st–3rd = 3 days); starts **PENDING**.
- Rejects end-before-start and unknown/archived leave types.
- **Balances:** approved reduces balance, **pending does not**; pending shown
  separately.
- **Policy entitlement override wins** over the leave type default (14 → 20).
- Approval: current-step approver allowed; non-current hidden; **multi-step chain
  advances**; team queue returns only current-approver applications **with the
  applicant email resolved**.
- Cancellation: only the owner can cancel their pending application.

**AuthServiceTests** — login, hashing, token issuance, refresh/rotation.

**EmployeeServiceTests** — assigning role / supervisor / policy.

**The through-line:** the tests encode the two rules that matter most — *approval
is by team seat, multi-step* and *pending never spends a balance* — so a future
refactor can't silently break them.
