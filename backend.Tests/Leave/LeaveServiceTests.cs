using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Tests.Common;
using AltomateHR.Api.Common;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Dtos;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Teams.Dtos;
using AltomateHR.Api.Modules.Holidays;
using AltomateHR.Api.Modules.Holidays.Dtos;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Organizations.Dtos;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Projects.Dtos;
using AltomateHR.Api.Modules.Xero;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Tests.Claims;   // reuse FakeSupervisionService + FakeApprovalRouter

using AltomateHR.Api.Tests.Support;

namespace AltomateHR.Api.Tests.Leave;

public class LeaveServiceTests
{
    private static readonly int Year = DateTime.UtcNow.Year;

    [Fact]
    public async Task ApplyAsync_ComputesInclusiveDaySpanAndStartsPending()
    {
        // The chain matters to the assertion: an applicant with NO approver has
        // their leave decided on submission, so "starts pending" is only true
        // when somebody is actually above them.
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        var result = await service.ApplyAsync(
            new CreateLeaveApplicationDto
            {
                LeaveTypeId = "t-al",
                StartDate = new DateTime(Year, 9, 1),
                EndDate = new DateTime(Year, 9, 3),
            },
            "usr-emp");

        Assert.True(result.Ok);
        Assert.Equal(3, result.Application!.TotalDays);   // 1st..3rd inclusive
        Assert.Equal(LeaveStatus.PENDING, result.Application.Status);
    }

    // ─── Who a leave type is for (maternity / paternity) ────────────────

    private static LeaveType Maternity() => new()
    {
        Id = "t-mat", Code = "MATERNITY", Name = "Maternity Leave", Paid = true,
        DefaultDays = 98, EligibleGender = Gender.FEMALE,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static CreateLeaveApplicationDto Sept1to3(string typeId) => new()
    {
        LeaveTypeId = typeId,
        StartDate = new DateTime(Year, 9, 1),
        EndDate = new DateTime(Year, 9, 3),
    };

    [Fact]
    public async Task ApplyAsync_RefusesALeaveTypeThatIsNotForTheEmployee()
    {
        var service = MakeService(
            types: [Maternity()],
            profiles: new FakeGenders(new() { ["usr-emp"] = Gender.MALE }));

        var result = await service.ApplyAsync(Sept1to3("t-mat"), "usr-emp");

        Assert.False(result.Ok);
        Assert.Equal("Maternity Leave is only for female employees.", result.Error);
    }

    // Allowed for whom it's for — and for anyone with no gender on file, so
    // missing data never blocks leave someone may be entitled to.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApplyAsync_AllowsItForThemOrWhenGenderIsUnknown(bool female)
    {
        var genders = female ? new Dictionary<string, Gender> { ["usr-emp"] = Gender.FEMALE } : new();
        var service = MakeService(types: [Maternity()], profiles: new FakeGenders(genders));

        var result = await service.ApplyAsync(Sept1to3("t-mat"), "usr-emp");

        Assert.True(result.Ok, result.Error);
    }

    [Fact]
    public async Task GetBalancesAsync_LeavesOutATypeThatIsNotForThem()
    {
        var service = MakeService(
            types: [MakeType("t-al", "ANNUAL", 14), Maternity()],
            profiles: new FakeGenders(new() { ["usr-emp"] = Gender.MALE }));

        var balances = await service.GetBalancesAsync("usr-emp", Year);

        Assert.Equal(["ANNUAL"], balances.Select(b => b.Code));
    }

    // Leave already on record this year stays visible even if the type isn't
    // for them (e.g. their gender was corrected afterwards).
    [Fact]
    public async Task GetBalancesAsync_KeepsATypeTheyAlreadyHaveLeaveOfThisYear()
    {
        var service = MakeService(
            types: [Maternity()],
            apps: [MakeApp("a-1", "usr-emp", "t-mat", 3, LeaveStatus.APPROVED)],
            profiles: new FakeGenders(new() { ["usr-emp"] = Gender.MALE }));

        var balances = await service.GetBalancesAsync("usr-emp", Year);

        var only = Assert.Single(balances);
        Assert.Equal("MATERNITY", only.Code);
        Assert.False(only.Eligible);   // listed, but not offered for a new request
    }

    // A rejected or cancelled request is not leave they have, so it doesn't
    // bring the type back.
    [Theory]
    [InlineData(LeaveStatus.REJECTED)]
    [InlineData(LeaveStatus.CANCELLED)]
    public async Task GetBalancesAsync_ARejectedOrCancelledRequestDoesNotKeepIt(LeaveStatus status)
    {
        var service = MakeService(
            types: [Maternity()],
            apps: [MakeApp("a-1", "usr-emp", "t-mat", 3, status)],
            profiles: new FakeGenders(new() { ["usr-emp"] = Gender.MALE }));

        Assert.Empty(await service.GetBalancesAsync("usr-emp", Year));
    }

    // The admin grid and its export follow the same rule as the employee's own
    // balances, so the two never disagree.
    [Fact]
    public async Task GetOrgBalancesAsync_LeavesOutATypeThatIsNotForThem()
    {
        var service = MakeService(
            types: [MakeType("t-al", "ANNUAL", 14), Maternity()],
            profiles: new FakeGenders(new() { ["usr-emp"] = Gender.MALE }));

        var row = Assert.Single(await service.GetOrgBalancesAsync(Year));

        Assert.Equal(["ANNUAL"], row.Balances.Select(b => b.Code));
    }

    [Fact]
    public async Task EditAsync_RefusesSwitchingToATypeThatIsNotForThem()
    {
        var service = MakeService(
            types: [MakeType("t-al", "ANNUAL", 14), Maternity()],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }),
            profiles: new FakeGenders(new() { ["usr-emp"] = Gender.MALE }));
        var applied = await service.ApplyAsync(Sept1to3("t-al"), "usr-emp");

        var edited = await service.EditAsync(applied.Application!.Id, Sept1to3("t-mat"), "usr-emp");

        Assert.False(edited.Ok);
        Assert.Equal("Maternity Leave is only for female employees.", edited.Error);
    }

    [Fact]
    public void Defaults_MaternityIsForWomen_PaternityForMen_TheRestForEveryone()
    {
        var byCode = LeaveDefaults.All.ToDictionary(d => d.Code, d => d.EligibleGender);

        Assert.Equal(Gender.FEMALE, byCode["MATERNITY"]);
        Assert.Equal(Gender.MALE, byCode["PATERNITY"]);
        Assert.All(byCode.Where(kv => kv.Key is not ("MATERNITY" or "PATERNITY")), kv => Assert.Null(kv.Value));
    }

    // CanEdit drives the employee's Edit button: pending with no approver
    // action yet — the same state EditAsync accepts.
    [Fact]
    public async Task CanEdit_IsTrueWhilePending_AndStaysTrueAfterAnEdit_FalseOnceDecided()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        var applied = await service.ApplyAsync(
            new CreateLeaveApplicationDto
            {
                LeaveTypeId = "t-al",
                StartDate = new DateTime(Year, 9, 1),
                EndDate = new DateTime(Year, 9, 3),
            },
            "usr-emp");
        Assert.True(applied.Application!.CanEdit);

        var edited = await service.EditAsync(
            applied.Application.Id,
            new CreateLeaveApplicationDto
            {
                LeaveTypeId = "t-al",
                StartDate = new DateTime(Year, 9, 2),
                EndDate = new DateTime(Year, 9, 3),
            },
            "usr-emp");
        Assert.True(edited.Ok);
        Assert.Equal(2, edited.Application!.TotalDays);
        Assert.True(edited.Application.CanEdit);

        var approved = await service.ApproveAsync(applied.Application.Id, "usr-super");
        Assert.True(approved.Transitioned);
        Assert.False(approved.Application!.CanEdit);
    }

    [Fact]
    public async Task ApplyAsync_DecidesOnSubmission_WhenNobodyIsAboveTheApplicant()
    {
        // Top of the hierarchy: admins don't approve (see OrgRoles), so there is
        // no step to route to. Left PENDING this application would never surface
        // in a queue and could never be decided by anyone.
        var service = MakeService(types: [MakeType("t-al", "AL", 14)]);   // no chain

        var result = await service.ApplyAsync(
            new CreateLeaveApplicationDto
            {
                LeaveTypeId = "t-al",
                StartDate = new DateTime(Year, 9, 1),
                EndDate = new DateTime(Year, 9, 1),
            },
            "usr-boss");

        Assert.True(result.Ok);
        Assert.Equal(LeaveStatus.APPROVED, result.Application!.Status);
        Assert.NotNull(result.Application.DecidedAt);
    }

    [Fact]
    public async Task ApplyAsync_RejectsWhenEndBeforeStart()
    {
        var service = MakeService(types: [MakeType("t-al", "AL", 14)]);

        var result = await service.ApplyAsync(
            new CreateLeaveApplicationDto
            {
                LeaveTypeId = "t-al",
                StartDate = new DateTime(Year, 9, 5),
                EndDate = new DateTime(Year, 9, 3),
            },
            "usr-emp");

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ApplyAsync_RejectsUnknownOrArchivedType()
    {
        var service = MakeService(types: [MakeType("t-al", "AL", 14, archived: true)]);

        var unknown = await service.ApplyAsync(NewDto("ghost"), "usr-emp");
        var archived = await service.ApplyAsync(NewDto("t-al"), "usr-emp");

        Assert.False(unknown.Ok);
        Assert.False(archived.Ok);
    }

    [Fact]
    public async Task GetBalancesAsync_SubtractsApprovedButNotPending()
    {
        var apps = new[]
        {
            MakeApp("a1", "usr-emp", "t-al", 3, LeaveStatus.APPROVED),
            MakeApp("a2", "usr-emp", "t-al", 2, LeaveStatus.PENDING),
        };
        var service = MakeService(types: [MakeType("t-al", "AL", 14)], apps: apps);

        var al = (await service.GetBalancesAsync("usr-emp", Year)).Single(b => b.Code == "AL");

        Assert.Equal(14, al.EntitlementDays);
        Assert.Equal(3, al.TakenDays);
        Assert.Equal(2, al.PendingDays);
        Assert.Equal(11, al.RemainingDays);   // pending does NOT reduce the balance
    }

    [Fact]
    public async Task GetBalancesForEmployeeAsync_Allows_AnEmployeeToReadTheirOwn()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            memberships: new FakeMembershipRepository("usr-emp"),
            currentUser: new FakeCurrentUser("usr-emp", "Employee"));

        var result = await service.GetBalancesForEmployeeAsync("usr-emp", Year);

        Assert.True(result.Found);
        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task GetBalancesForEmployeeAsync_Refuses_AnEmployeeReadingSomeoneElse()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            memberships: new FakeMembershipRepository("usr-emp", "usr-two"),
            currentUser: new FakeCurrentUser("usr-emp", "Employee"));

        var result = await service.GetBalancesForEmployeeAsync("usr-two", Year);

        Assert.True(result.Found);        // they exist in this org...
        Assert.False(result.Allowed);     // ...but it's not the caller's to read (403)
        Assert.Empty(result.Balances);
    }

    [Fact]
    public async Task GetBalancesAsync_PrefersTheStoredEntitlementRow_OverTheTypeDefault()
    {
        // Type says 14, but the stored row (what the rollover wrote) says 20.
        var row = new LeaveEntitlement
        {
            EmployeeId = "usr-emp", LeaveTypeId = "t-al", Year = Year,
            EntitledDays = 20, AccruedDays = 20,
        };
        var service = MakeService(types: [MakeType("t-al", "AL", 14)], entitlementRows: [row]);

        var al = (await service.GetBalancesAsync("usr-emp", Year)).Single();

        Assert.True(al.IsOpened);
        Assert.Equal(20, al.EntitlementDays);
        Assert.Equal(20, al.RemainingDays);
    }

    [Fact]
    public async Task GetBalancesAsync_ProRated_ShowsOnlyWhatHasAccrued()
    {
        var row = new LeaveEntitlement
        {
            EmployeeId = "usr-emp", LeaveTypeId = "t-al", Year = Year,
            EntitledDays = 12, AccruedDays = 5,
            AccrualMethod = LeaveAccrualMethod.PRO_RATED,
        };
        var service = MakeService(types: [MakeType("t-al", "AL", 12)], entitlementRows: [row]);

        var al = (await service.GetBalancesAsync("usr-emp", Year)).Single();

        Assert.Equal(12, al.EntitlementDays);   // the year's full entitlement...
        Assert.Equal(5, al.AccruedDays);        // ...but only 5 earned so far
        Assert.Equal(5, al.RemainingDays);      // so 5 is what she can apply for
    }

    [Fact]
    public async Task GetBalancesAsync_AddsUnexpiredCarry_ButIgnoresExpiredCarry()
    {
        var live = new LeaveEntitlement
        {
            EmployeeId = "usr-emp", LeaveTypeId = "t-al", Year = Year,
            EntitledDays = 12, AccruedDays = 12, CarriedDays = 3,
        };
        var service = MakeService(types: [MakeType("t-al", "AL", 12)], entitlementRows: [live]);
        Assert.Equal(15, (await service.GetBalancesAsync("usr-emp", Year)).Single().RemainingDays);

        live.CarriedExpired = true;             // the sweep has run
        Assert.Equal(12, (await service.GetBalancesAsync("usr-emp", Year)).Single().RemainingDays);
    }

    [Fact]
    public async Task GetBalancesAsync_FallsBackToTheTypeDefault_WhenTheYearIsNotOpened()
    {
        var service = MakeService(types: [MakeType("t-al", "AL", 14)]);   // no entitlement rows

        var al = (await service.GetBalancesAsync("usr-emp", Year)).Single();

        Assert.False(al.IsOpened);              // projection, not stored state
        Assert.Equal(14, al.EntitlementDays);
        Assert.Equal(14, al.RemainingDays);
    }

    [Fact]
    public async Task GetAttachment_ReturnsTheFile_ForTheApplicationOwner()
    {
        var app = MakeApp("a1", "usr-emp", "t-al", 2, LeaveStatus.APPROVED);
        app.XeroFileId = "file-1";
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)], apps: [app],
            memberships: new FakeMembershipRepository("usr-emp"),
            currentUser: new FakeCurrentUser("usr-emp", "Employee"),
            xero: new FakeXeroService(new XeroFileContent([1, 2, 3], "image/png", "mc.png")));

        var result = await service.GetAttachmentAsync("file-1");

        Assert.True(result.Found);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal("mc.png", result.FileName);
    }

    [Fact]
    public async Task GetAttachment_Is404_ForSomeoneElsesAttachment()
    {
        // Belongs to usr-two; usr-emp is a plain employee, so it must look
        // identical to "no such file" — not a 403 that confirms it exists.
        var app = MakeApp("a1", "usr-two", "t-al", 2, LeaveStatus.APPROVED);
        app.XeroFileId = "file-1";
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)], apps: [app],
            memberships: new FakeMembershipRepository("usr-emp", "usr-two"),
            currentUser: new FakeCurrentUser("usr-emp", "Employee"),
            xero: new FakeXeroService(new XeroFileContent([1], "image/png", "mc.png")));

        var result = await service.GetAttachmentAsync("file-1");

        Assert.False(result.Found);
        Assert.Empty(result.Content);
    }

    [Fact]
    public async Task GetAttachment_Is404_WhenNoApplicationOwnsTheFile()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            xero: new FakeXeroService(new XeroFileContent([1], "image/png", "x.png")));

        // Xero would happily return bytes — but no leave application claims this
        // id, so it must never be served.
        Assert.False((await service.GetAttachmentAsync("file-unknown")).Found);
    }

    [Fact]
    public async Task CancelAsync_RefusesAPPROVEDLeave()
    {
        // Deliberate: undoing approved leave returns days someone already
        // granted, and may be leave the employee has taken. Admin territory.
        var service = MakeService(apps: [MakeApp("a1", "usr-emp", "t-al", 1, LeaveStatus.APPROVED)]);

        var result = await service.CancelAsync("a1", "usr-emp");

        Assert.False(result.Transitioned);
        Assert.Equal("Only pending leave can be cancelled", result.Error);
    }

    [Fact]
    public async Task CancelAsync_IsIdempotent_OnAnAlreadyCancelledRequest()
    {
        var service = MakeService(apps: [MakeApp("a1", "usr-emp", "t-al", 1, LeaveStatus.CANCELLED)]);

        Assert.True((await service.CancelAsync("a1", "usr-emp")).Transitioned);
    }

    [Fact]
    public async Task CancelAsync_RefusesREJECTEDLeave()
    {
        var service = MakeService(apps: [MakeApp("a1", "usr-emp", "t-al", 1, LeaveStatus.REJECTED)]);

        var result = await service.CancelAsync("a1", "usr-emp");

        Assert.False(result.Transitioned);
        Assert.Equal("Only pending leave can be cancelled", result.Error);
    }

    [Fact]
    public async Task Recompute_RederivesAccrual_FromTheJoinDate()
    {
        // Type is PRO_RATED with 12 days; the row was seeded as if they joined
        // in January, but they actually joined 1 July — 6 months' worth.
        var row = new LeaveEntitlement
        {
            EmployeeId = "usr-emp", LeaveTypeId = "t-al", Year = Year,
            EntitledDays = 12, AccruedDays = 12,
        };
        var service = MakeService(
            types: [ProRatedType("t-al", 12)],
            entitlementRows: [row],
            memberships: new FakeMembershipRepository(("usr-emp", new DateTime(Year, 7, 1))));

        var touched = await service.RecomputeProRatedAccrualAsync("usr-emp", Year);

        Assert.Equal(1, touched);
        Assert.True(row.AccruedDays < 12);   // re-derived downward, not left stale
    }

    [Fact]
    public async Task Recompute_SkipsRows_WhereLeaveHasAlreadyBeenTaken()
    {
        // Guard 1. Moving a balance someone has spent against could push them
        // negative, so the row is left alone.
        var row = new LeaveEntitlement
        {
            EmployeeId = "usr-emp", LeaveTypeId = "t-al", Year = Year,
            EntitledDays = 12, AccruedDays = 12,
        };
        var service = MakeService(
            types: [ProRatedType("t-al", 12)],
            apps: [MakeApp("a1", "usr-emp", "t-al", 3, LeaveStatus.APPROVED)],
            entitlementRows: [row],
            memberships: new FakeMembershipRepository(("usr-emp", new DateTime(Year, 7, 1))));

        Assert.Equal(0, await service.RecomputeProRatedAccrualAsync("usr-emp", Year));
        Assert.Equal(12, row.AccruedDays);
    }

    [Fact]
    public async Task Recompute_SkipsRows_WithAPerEmployeeMethodOverride()
    {
        // Guard 2. An admin set this by hand; don't undo them.
        var row = new LeaveEntitlement
        {
            EmployeeId = "usr-emp", LeaveTypeId = "t-al", Year = Year,
            EntitledDays = 12, AccruedDays = 12,
            AccrualMethod = LeaveAccrualMethod.PRO_RATED,
        };
        var service = MakeService(
            types: [ProRatedType("t-al", 12)],
            entitlementRows: [row],
            memberships: new FakeMembershipRepository(("usr-emp", new DateTime(Year, 7, 1))));

        Assert.Equal(0, await service.RecomputeProRatedAccrualAsync("usr-emp", Year));
    }

    [Fact]
    public async Task Recompute_SkipsLumpSumTypes()
    {
        // Guard 4. Nothing to pro-rate.
        var row = new LeaveEntitlement
        {
            EmployeeId = "usr-emp", LeaveTypeId = "t-al", Year = Year,
            EntitledDays = 14, AccruedDays = 14,
        };
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],   // LUMP_SUM
            entitlementRows: [row],
            memberships: new FakeMembershipRepository(("usr-emp", new DateTime(Year, 7, 1))));

        Assert.Equal(0, await service.RecomputeProRatedAccrualAsync("usr-emp", Year));
        Assert.Equal(14, row.AccruedDays);
    }

    [Fact]
    public async Task GetOrgBalancesAsync_ReturnsOneRowPerMember_WithTheirOwnBalances()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            apps: [MakeApp("a1", "usr-emp", "t-al", 3, LeaveStatus.APPROVED)],
            memberships: new FakeMembershipRepository("usr-emp", "usr-two"));

        var rows = (await service.GetOrgBalancesAsync(Year)).ToList();

        Assert.Equal(2, rows.Count);
        // usr-emp took 3 days; usr-two took none — balances must not bleed across employees.
        Assert.Equal(11, rows.Single(r => r.UserId == "usr-emp").Balances.Single().RemainingDays);
        Assert.Equal(14, rows.Single(r => r.UserId == "usr-two").Balances.Single().RemainingDays);
    }

    [Fact]
    public async Task GetBalancesForEmployeeAsync_ReturnsBalances_WhenEmployeeIsInCurrentOrg()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            apps: [MakeApp("a1", "usr-emp", "t-al", 3, LeaveStatus.APPROVED)],
            memberships: new FakeMembershipRepository("usr-emp"));

        var result = await service.GetBalancesForEmployeeAsync("usr-emp", Year);

        Assert.True(result.Found);
        Assert.True(result.Allowed);
        Assert.Equal(Year, result.Year);
        Assert.Equal(11, result.Balances.Single(b => b.Code == "AL").RemainingDays);
    }

    [Fact]
    public async Task GetBalancesForEmployeeAsync_NotFound_WhenEmployeeIsInAnotherOrg()
    {
        // "usr-other" is not a member of the caller's org — must not leak data.
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            memberships: new FakeMembershipRepository("usr-emp"));

        var result = await service.GetBalancesForEmployeeAsync("usr-other", Year);

        Assert.False(result.Found);
        Assert.Empty(result.Balances);
    }

    [Fact]
    public async Task GetBalancesAsync_UsesPolicyEntitlementOverride()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            policy: new FakePolicyService(new Dictionary<string, double> { ["t-al"] = 20 }));

        var al = (await service.GetBalancesAsync("usr-emp", Year)).Single(b => b.Code == "AL");

        Assert.Equal(20, al.EntitlementDays);   // policy override wins over the type default (14)
        Assert.Equal(20, al.RemainingDays);
    }

    [Fact]
    public async Task ApproveAsync_AllowsCurrentStepApprover()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            apps: [MakeApp("a1", "usr-emp", "t-al", 3, LeaveStatus.PENDING)],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        var result = await service.ApproveAsync("a1", "usr-super");

        Assert.True(result.Transitioned);
        Assert.Equal(LeaveStatus.APPROVED, result.Application!.Status);
    }

    [Fact]
    public async Task ApproveAsync_HidesApplicationFromNonCurrentApprover()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            apps: [MakeApp("a1", "usr-emp", "t-al", 3, LeaveStatus.PENDING)],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        var result = await service.ApproveAsync("a1", "usr-other-super");

        // Production returns an explicit refusal here rather than hiding the
        // request. Found=true + Transitioned=false → 403 at the controller.
        Assert.True(result.Found);
        Assert.False(result.Transitioned);
        Assert.Equal("You are not authorized to review this step", result.Error);
    }

    [Fact]
    public async Task ApproveAsync_AdvancesThroughAMultiStepChain()
    {
        var app = MakeApp("a1", "usr-emp", "t-al", 1, LeaveStatus.PENDING);
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            apps: [app],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"], ["usr-mgr"]] }));

        var first = await service.ApproveAsync("a1", "usr-super");   // step 0 → advance
        Assert.True(first.Transitioned);
        Assert.Equal(LeaveStatus.PENDING, first.Application!.Status);
        Assert.Equal(1, app.CurrentStep);

        // The step-0 approver can no longer act — they are not the current step.
        var stale = await service.ApproveAsync("a1", "usr-super");
        Assert.False(stale.Transitioned);
        Assert.Equal("You are not authorized to review this step", stale.Error);

        var second = await service.ApproveAsync("a1", "usr-mgr");    // step 1 → final
        Assert.True(second.Transitioned);
        Assert.Equal(LeaveStatus.APPROVED, second.Application!.Status);
    }

    [Fact]
    public async Task GetTeamAsync_ReturnsOnlyCurrentApproverApplicationsWithEmail()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            apps:
            [
                MakeApp("mine", "usr-emp", "t-al", 1, LeaveStatus.PENDING),
                MakeApp("other", "usr-other", "t-al", 1, LeaveStatus.PENDING),
            ],
            supervision: new FakeSupervisionService(emails: new() { ["usr-emp"] = "employee@altomate.com" }),
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        var team = (await service.GetTeamAsync("usr-super")).ToList();

        Assert.Single(team);
        Assert.Equal("mine", team[0].Id);
        Assert.Equal("employee@altomate.com", team[0].EmployeeEmail);
    }

    [Fact]
    public async Task CancelAsync_OwnerCancelsPending_ButOthersCannot()
    {
        var service = MakeService(apps: [MakeApp("a1", "usr-emp", "t-al", 1, LeaveStatus.PENDING)]);

        var byOther = await service.CancelAsync("a1", "usr-other");
        Assert.False(byOther.Transitioned);
        Assert.Equal("Only the applicant can cancel", byOther.Error);

        var byOwner = await service.CancelAsync("a1", "usr-emp");
        Assert.True(byOwner.Transitioned);
        Assert.Equal(LeaveStatus.CANCELLED, byOwner.Application!.Status);
    }

    // ---- Bulk approve ----
    //
    // Mirrors ClaimsBulkApproveTests: what bulk REFUSES matters more than what
    // it approves, because a refusal that fails silently is a request the
    // approver thinks they signed off.

    [Fact]
    public async Task BulkApproveAsync_ApprovesEveryApplicationTheCallerMayDecide()
    {
        var a = MakeApp("a1", "usr-emp", "t-al", 1, LeaveStatus.PENDING);
        var b = MakeApp("a2", "usr-emp", "t-al", 2, LeaveStatus.PENDING);
        var service = MakeService(
            apps: [a, b],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        var result = await service.BulkApproveAsync(["a1", "a2"], "usr-super");

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(LeaveStatus.APPROVED, a.Status);
        Assert.Equal(LeaveStatus.APPROVED, b.Status);
    }

    [Fact]
    public async Task BulkApproveAsync_FailsOnlyTheApplicationsTheCallerCannotDecide()
    {
        var mine = MakeApp("mine", "usr-emp", "t-al", 1, LeaveStatus.PENDING);
        var other = MakeApp("other", "usr-other", "t-al", 1, LeaveStatus.PENDING);
        var service = MakeService(
            apps: [mine, other],
            router: new FakeApprovalRouter(new()
            {
                ["usr-emp"] = [["usr-super"]],
                ["usr-other"] = [["usr-someone-else"]],
            }));

        var result = await service.BulkApproveAsync(["mine", "other"], "usr-super");

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Equal(LeaveStatus.APPROVED, mine.Status);
        Assert.Equal(LeaveStatus.PENDING, other.Status);
        Assert.False(result.Items.Single(i => i.Id == "other").Ok);
    }

    [Fact]
    public async Task BulkApproveAsync_SkipsApplicationsThatWereAlreadyDecided()
    {
        var decided = MakeApp("done", "usr-emp", "t-al", 1, LeaveStatus.REJECTED);
        var service = MakeService(
            apps: [decided],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        var result = await service.BulkApproveAsync(["done"], "usr-super");

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Equal(LeaveStatus.REJECTED, decided.Status);
    }

    [Fact]
    public async Task BulkApproveAsync_CountsARepeatedIdOnce()
    {
        var app = MakeApp("a1", "usr-emp", "t-al", 1, LeaveStatus.PENDING);
        var service = MakeService(
            apps: [app],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        var result = await service.BulkApproveAsync(["a1", "a1"], "usr-super");

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task BulkApproveAsync_RefusesTheWholeBatchWhenItIsTooLarge()
    {
        var service = MakeService(router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));
        var ids = Enumerable.Range(0, 201).Select(i => $"a{i}").ToList();

        var result = await service.BulkApproveAsync(ids, "usr-super");

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(201, result.Failed);
        Assert.Contains("Too many", result.Items.Single().Error);
    }

    [Fact]
    public async Task BulkApproveAsync_AdvancesTheChainInsteadOfApprovingOnAMultiStepChain()
    {
        var app = MakeApp("a1", "usr-emp", "t-al", 1, LeaveStatus.PENDING);
        var service = MakeService(
            apps: [app],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"], ["usr-boss"]] }));

        var result = await service.BulkApproveAsync(["a1"], "usr-super");

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(LeaveStatus.PENDING, app.Status);   // still waiting on usr-boss
        Assert.Equal(1, app.CurrentStep);
    }

    [Fact]
    public async Task BulkApproveAsync_WritesOneTrailEntryPerApprovedApplication()
    {
        var app = MakeApp("a1", "usr-emp", "t-al", 1, LeaveStatus.PENDING);
        var service = MakeService(
            apps: [app],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        await service.BulkApproveAsync(["a1"], "usr-super");

        // The audit log is the only record of WHO signed off in bulk, so a batch
        // approval that skipped the trail would be indistinguishable from none.
        Assert.Contains("usr-super", app.Approvals);
        Assert.Contains("APPROVED", app.Approvals);
    }

    // ---- Editing pending leave: bug-check findings ----

    private static FakeApprovalRouter PendingChain() =>
        new(new() { ["usr-emp"] = [["usr-super"]] });

    private static CreateLeaveApplicationDto Dto(
        string typeId, string start, string end,
        LeaveDuration duration = LeaveDuration.FULL_DAY, string? reason = null) => new()
    {
        LeaveTypeId = typeId,
        StartDate = DateTime.Parse(start),
        EndDate = DateTime.Parse(end),
        Duration = duration,
        Reason = reason,
    };

    private static LeaveApplication PendingApp(
        string id, string typeId, string start, string end, double days,
        LeaveDuration duration = LeaveDuration.FULL_DAY) => new()
    {
        Id = id,
        EmployeeId = "usr-emp",
        LeaveTypeId = typeId,
        StartDate = DateTime.Parse(start),
        EndDate = DateTime.Parse(end),
        Duration = duration,
        TotalDays = days,
        Status = LeaveStatus.PENDING,
        CurrentStep = 0,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    // Applying stores the Xero file id that POST /leave/attachments returned,
    // and the proxy then serves it — the id is what it resolves by.
    [Fact]
    public async Task Apply_StoresTheXeroFileId_SoTheAttachmentIsServed()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            router: PendingChain(),
            memberships: new FakeMembershipRepository("usr-emp"),
            currentUser: new FakeCurrentUser("usr-emp", "Employee"),
            xero: new FakeXeroService(new XeroFileContent([1], "application/pdf", "mc.pdf")));

        var dto = Dto("t-al", "2026-09-01", "2026-09-01");
        dto.XeroFileId = "xf-1";
        dto.AttachmentUrl = "/leave/files/xf-1/content";
        dto.AttachmentName = "mc.pdf";
        var applied = await service.ApplyAsync(dto, "usr-emp");

        Assert.True(applied.Ok);
        Assert.Equal("/leave/files/xf-1/content", applied.Application!.AttachmentUrl);
        Assert.True((await service.GetAttachmentAsync("xf-1")).Found);
    }

    // A client that sends only the proxy url (as this one used to) must not
    // leave a Xero-hosted file unreachable: the id is read back out of it.
    [Fact]
    public async Task Apply_WithOnlyTheXeroProxyUrl_StillRecordsTheFileId()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            router: PendingChain(),
            memberships: new FakeMembershipRepository("usr-emp"),
            currentUser: new FakeCurrentUser("usr-emp", "Employee"),
            xero: new FakeXeroService(new XeroFileContent([1], "application/pdf", "mc.pdf")));

        var dto = Dto("t-al", "2026-09-01", "2026-09-01");
        dto.AttachmentUrl = "/leave/files/xf-9/content";
        dto.AttachmentName = "mc.pdf";
        Assert.True((await service.ApplyAsync(dto, "usr-emp")).Ok);

        Assert.True((await service.GetAttachmentAsync("xf-9")).Found);
    }

    // Replacing a Xero attachment with another Xero file: url, name AND id all
    // move, so the new file — not the old one — is what's served.
    [Fact]
    public async Task Edit_ReplacingAXeroAttachment_MovesTheFileIdWithTheUrl()
    {
        var app = PendingApp("a1", "t-al", "2026-09-01", "2026-09-01", 1);
        app.XeroFileId = "xf-old";
        app.AttachmentUrl = "/leave/files/xf-old/content";
        app.AttachmentName = "old.pdf";
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)], apps: [app], router: PendingChain(),
            memberships: new FakeMembershipRepository("usr-emp"),
            currentUser: new FakeCurrentUser("usr-emp", "Employee"),
            xero: new FakeXeroService(new XeroFileContent([1], "application/pdf", "new.pdf")));

        var dto = Dto("t-al", "2026-09-01", "2026-09-01");
        dto.XeroFileId = "xf-new";
        dto.AttachmentUrl = "/leave/files/xf-new/content";
        dto.AttachmentName = "new.pdf";
        var edited = await service.EditAsync("a1", dto, "usr-emp");

        Assert.True(edited.Ok);
        Assert.Equal("new.pdf", edited.Application!.AttachmentName);
        Assert.Equal("/leave/files/xf-new/content", edited.Application.AttachmentUrl);
        Assert.Equal("xf-new", app.XeroFileId);
        Assert.True((await service.GetAttachmentAsync("xf-new")).Found);
        Assert.False((await service.GetAttachmentAsync("xf-old")).Found);
    }

    // Replacing a Xero attachment with a LOCAL file clears the id; otherwise
    // the read side would keep preferring the old Xero file.
    [Fact]
    public async Task Edit_ReplacingAXeroAttachmentWithALocalFile_ClearsTheFileId()
    {
        var app = PendingApp("a1", "t-al", "2026-09-01", "2026-09-01", 1);
        app.XeroFileId = "xf-old";
        app.AttachmentUrl = "/leave/files/xf-old/content";
        app.AttachmentName = "old.pdf";
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)], apps: [app], router: PendingChain());

        var dto = Dto("t-al", "2026-09-01", "2026-09-01");
        dto.AttachmentUrl = "/leave/attachments/20260901-abc.pdf";
        dto.AttachmentName = "new.pdf";
        var edited = await service.EditAsync("a1", dto, "usr-emp");

        Assert.True(edited.Ok);
        Assert.Null(app.XeroFileId);
        Assert.Equal("/leave/attachments/20260901-abc.pdf", edited.Application!.AttachmentUrl);
        Assert.Equal("new.pdf", edited.Application.AttachmentName);
    }

    [Fact]
    public async Task Edit_WithoutANewAttachment_KeepsTheOneAlreadyOnIt()
    {
        var app = PendingApp("a1", "t-al", "2026-09-01", "2026-09-01", 1);
        app.XeroFileId = "xf-old";
        app.AttachmentName = "old.pdf";
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)], apps: [app], router: PendingChain());

        var edited = await service.EditAsync("a1", Dto("t-al", "2026-09-02", "2026-09-02"), "usr-emp");

        Assert.True(edited.Ok);
        Assert.Equal("xf-old", app.XeroFileId);
        Assert.Equal("/leave/files/xf-old/content", edited.Application!.AttachmentUrl);
        Assert.Equal("old.pdf", edited.Application.AttachmentName);
    }

    // An imported half day is stored FULL_DAY with 0.5 days; an edit would
    // recompute it to a whole day. Imported rows are therefore HR's to change.
    [Fact]
    public async Task ImportedPendingLeave_IsNotEditable()
    {
        var service = ImportService();
        await service.ImportHistoryAsync(
            LeaveCsv(ImportHeader, "ahmad@x.com,AL,2026-03-02,2026-03-02,0.5,PENDING,"),
            TabularFormat.Csv, "usr-admin");
        var imported = Assert.Single(await service.GetMineAsync("usr-emp"));

        Assert.Equal(LeaveStatus.PENDING, imported.Status);
        Assert.False(imported.CanEdit);

        var edit = await service.EditAsync(
            imported.Id, Dto("lt-al", "2026-03-02", "2026-03-02", reason: "typo"), "usr-emp");
        Assert.False(edit.Ok);
        Assert.Contains("imported", edit.Error);
        Assert.Equal(0.5, Assert.Single(await service.GetMineAsync("usr-emp")).TotalDays);
    }

    // Same rule keyed on the trail marker alone, for a row whose
    // AppliedByAdminId was never carried over.
    [Fact]
    public async Task ARowWithTheImportedTrailMarker_IsNotEditable()
    {
        var app = PendingApp("a1", "t-al", "2026-09-01", "2026-09-01", 0.5);
        app.Approvals = """[{"step":0,"approverId":"usr-admin","decision":"IMPORTED"}]""";
        var service = MakeService(types: [MakeType("t-al", "AL", 14)], apps: [app], router: PendingChain());

        Assert.False(Assert.Single(await service.GetMineAsync("usr-emp")).CanEdit);
        Assert.False((await service.EditAsync("a1", Dto("t-al", "2026-09-01", "2026-09-01"), "usr-emp")).Ok);
    }

    // Type, dates and duration unchanged: the stored days stand, even when
    // today's calendar would count them differently (here a holiday was added
    // on that date after it was filed).
    [Fact]
    public async Task ReasonOnlyEdit_KeepsTheStoredTotalDays()
    {
        var app = PendingApp("a1", "t-al", "2026-09-01", "2026-09-02", 2);
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)], apps: [app], router: PendingChain(),
            holidays: [new HolidayDto { Id = "h1", Date = "2026-09-02", Name = "New holiday" }]);

        var edited = await service.EditAsync(
            "a1", Dto("t-al", "2026-09-01", "2026-09-02", reason: "Family trip"), "usr-emp");

        Assert.True(edited.Ok);
        Assert.Equal(2, edited.Application!.TotalDays);
        Assert.Equal("Family trip", edited.Application.Reason);
    }

    // Moving the dates DOES recount.
    [Fact]
    public async Task EditThatMovesTheDates_RecountsAgainstTheCalendar()
    {
        var app = PendingApp("a1", "t-al", "2026-09-01", "2026-09-02", 2);
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)], apps: [app], router: PendingChain());

        // Fri 4 Sep to Mon 7 Sep: two working days, not four.
        var edited = await service.EditAsync("a1", Dto("t-al", "2026-09-04", "2026-09-07"), "usr-emp");

        Assert.True(edited.Ok);
        Assert.Equal(2, edited.Application!.TotalDays);
        Assert.Equal("2026-09-04", edited.Application.StartDate);
    }

    // 2026-09-05 is a Saturday: a half day there is no leave at all.
    [Fact]
    public async Task HalfDayOnASaturday_IsRefused_OnApplyAndEdit()
    {
        var app = PendingApp("a1", "t-al", "2026-09-02", "2026-09-02", 0.5, LeaveDuration.MORNING);
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)], apps: [app], router: PendingChain());

        var apply = await service.ApplyAsync(
            Dto("t-al", "2026-09-05", "2026-09-05", LeaveDuration.MORNING), "usr-emp");
        Assert.False(apply.Ok);
        Assert.Equal("Selected dates contain no working days", apply.Error);

        var edit = await service.EditAsync(
            "a1", Dto("t-al", "2026-09-05", "2026-09-05", LeaveDuration.MORNING), "usr-emp");
        Assert.False(edit.Ok);
        Assert.Equal("Selected dates contain no working days", edit.Error);

        // A weekday half day still costs half.
        var ok = await service.ApplyAsync(
            Dto("t-al", "2026-09-03", "2026-09-03", LeaveDuration.AFTERNOON), "usr-emp");
        Assert.True(ok.Ok);
        Assert.Equal(0.5, ok.Application!.TotalDays);
    }

    [Fact]
    public async Task HalfDayOnAPublicHoliday_IsRefused()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)], router: PendingChain(),
            holidays: [new HolidayDto { Id = "h1", Date = "2026-09-02", Name = "Holiday" }]);

        var apply = await service.ApplyAsync(
            Dto("t-al", "2026-09-02", "2026-09-02", LeaveDuration.MORNING), "usr-emp");

        Assert.False(apply.Ok);
    }

    // Archiving stops NEW use of a type; a request that already has it can
    // still have its reason or dates changed.
    [Fact]
    public async Task Edit_KeepsAnArchivedTypeTheRequestAlreadyHas()
    {
        var app = PendingApp("a1", "t-old", "2026-09-01", "2026-09-01", 1);
        var service = MakeService(
            types: [MakeType("t-old", "OLD", 14, archived: true), MakeType("t-al", "AL", 14)],
            apps: [app], router: PendingChain());

        var reasonOnly = await service.EditAsync(
            "a1", Dto("t-old", "2026-09-01", "2026-09-01", reason: "now with a reason"), "usr-emp");
        Assert.True(reasonOnly.Ok);

        var moved = await service.EditAsync("a1", Dto("t-old", "2026-09-02", "2026-09-03"), "usr-emp");
        Assert.True(moved.Ok);
        Assert.Equal(2, moved.Application!.TotalDays);
        Assert.Equal("t-old", moved.Application.LeaveTypeId);
    }

    [Fact]
    public async Task Edit_RefusesSwitchingToAnArchivedType()
    {
        var app = PendingApp("a1", "t-al", "2026-09-01", "2026-09-01", 1);
        var service = MakeService(
            types: [MakeType("t-old", "OLD", 14, archived: true), MakeType("t-al", "AL", 14)],
            apps: [app], router: PendingChain());

        var edit = await service.EditAsync("a1", Dto("t-old", "2026-09-01", "2026-09-01"), "usr-emp");

        Assert.False(edit.Ok);
        Assert.Equal("Leave type is archived", edit.Error);
        Assert.Equal("t-al", app.LeaveTypeId);
    }

    // Thu 31 Dec 2026 - Mon 4 Jan 2027 with New Year's Day a holiday: Thu and
    // Mon only. The holiday sits in the END year, which the old start-year
    // lookup never loaded.
    [Fact]
    public async Task AHolidayInTheFollowingYear_IsExcluded_OnApplyAndEdit()
    {
        HolidayDto[] newYear = [new() { Id = "h1", Date = "2027-01-01", Name = "New Year" }];
        var app = PendingApp("a1", "t-al", "2026-12-01", "2026-12-01", 1);
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)], apps: [app], router: PendingChain(), holidays: newYear);

        var applied = await service.ApplyAsync(Dto("t-al", "2026-12-31", "2027-01-04"), "usr-two");
        Assert.True(applied.Ok);
        Assert.Equal(2, applied.Application!.TotalDays);

        var edited = await service.EditAsync("a1", Dto("t-al", "2026-12-31", "2027-01-04"), "usr-emp");
        Assert.True(edited.Ok);
        Assert.Equal(2, edited.Application!.TotalDays);

        Assert.Equal(2, await service.PreviewDaysAsync(
            new DateTime(2026, 12, 31), new DateTime(2027, 1, 4), LeaveDuration.FULL_DAY));
    }

    // The apply form's figure: the server's own count, not calendar days.
    [Theory]
    [InlineData("2026-09-04", "2026-09-07", LeaveDuration.FULL_DAY, 2)]    // Fri-Mon
    [InlineData("2026-09-01", "2026-09-04", LeaveDuration.FULL_DAY, 3)]    // Wed is a holiday
    [InlineData("2026-09-05", "2026-09-06", LeaveDuration.FULL_DAY, 0)]    // weekend only
    [InlineData("2026-09-03", "2026-09-03", LeaveDuration.MORNING, 0.5)]
    [InlineData("2026-09-05", "2026-09-05", LeaveDuration.MORNING, 0)]     // Saturday
    [InlineData("2026-09-03", "2026-09-04", LeaveDuration.MORNING, 0)]     // half day over two dates
    [InlineData("2026-09-04", "2026-09-01", LeaveDuration.FULL_DAY, 0)]    // end before start
    public async Task PreviewDays_CountsWorkingDaysLikeApply(
        string start, string end, LeaveDuration duration, double expected)
    {
        var service = MakeService(
            holidays: [new HolidayDto { Id = "h1", Date = "2026-09-02", Name = "Holiday" }]);

        Assert.Equal(expected, await service.PreviewDaysAsync(
            DateTime.Parse(start), DateTime.Parse(end), duration));
    }

    // An approval that lands between the edit's read and its save wins: the
    // guarded save refuses, and the request stays approved.
    [Fact]
    public async Task Edit_IsRefused_WhenAnApprovalLandsBeforeItSaves()
    {
        var app = PendingApp("a1", "t-al", "2026-09-01", "2026-09-01", 1);
        var repo = new FakeLeaveApplicationRepository([app])
        {
            BeforeEditSave = stored =>
            {
                stored.Status = LeaveStatus.APPROVED;
                stored.CurrentStep = 1;
            },
        };
        var service = MakeService(types: [MakeType("t-al", "AL", 14)], router: PendingChain(), appRepo: repo);

        var edit = await service.EditAsync("a1", Dto("t-al", "2026-09-02", "2026-09-03"), "usr-emp");

        Assert.False(edit.Ok);
        Assert.Equal("Cannot edit — an approver has already reviewed this leave", edit.Error);
        Assert.Equal(0, repo.EditSaves);
        Assert.Equal(LeaveStatus.APPROVED, app.Status);
    }

    [Fact]
    public async Task Edit_SavesThroughTheGuardedWrite()
    {
        var app = PendingApp("a1", "t-al", "2026-09-01", "2026-09-01", 1);
        var repo = new FakeLeaveApplicationRepository([app]);
        var service = MakeService(types: [MakeType("t-al", "AL", 14)], router: PendingChain(), appRepo: repo);

        Assert.True((await service.EditAsync("a1", Dto("t-al", "2026-09-02", "2026-09-03"), "usr-emp")).Ok);
        Assert.Equal(1, repo.EditSaves);
    }

    // --- helpers ---

    private static CreateLeaveApplicationDto NewDto(string typeId) => new()
    {
        LeaveTypeId = typeId,
        StartDate = new DateTime(Year, 9, 1),
        EndDate = new DateTime(Year, 9, 2),
    };

    // ---- Import / export ----

    private static readonly EmployeeIdentity Ahmad =
        new("usr-emp", "ahmad@x.com", "Ahmad Ali", "Employee");

    private static byte[] LeaveCsv(params string[] lines) =>
        System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");

    private const string ImportHeader =
        "*Employee Email,*Leave Type,*Start Date,*End Date,*Days,*Status,Reason";

    private static LeaveService ImportService(
        IEnumerable<LeaveApplication>? apps = null) =>
        MakeService(
            types: [MakeType("lt-al", "AL", 12)],
            apps: apps,
            memberships: new FakeMembershipRepository("usr-emp"),
            employees: new FakeEmployeeDirectory(Ahmad));

    [Fact]
    public async Task Import_CreatesAnApprovedHistoricalApplication()
    {
        var service = ImportService();

        var result = await service.ImportHistoryAsync(
            LeaveCsv(ImportHeader, "ahmad@x.com,AL,2026-03-02,2026-03-03,2,APPROVED,Family"),
            TabularFormat.Csv,
            "usr-admin");

        Assert.Equal(1, result.Imported);
        Assert.Empty(result.Errors);

        var app = Assert.Single(await service.GetMineAsync("usr-emp"));
        Assert.Equal(LeaveStatus.APPROVED, app.Status);
        Assert.Equal(2, app.TotalDays);
        Assert.Equal("2026-03-02", app.StartDate);
    }

    // A leave type the org hasn't set up is reported once, with its row count,
    // so the import screen can offer to add it — and every other row still lands.
    [Fact]
    public async Task Import_GroupsUnknownLeaveTypesForTheScreenToOffer()
    {
        var service = ImportService();

        var result = await service.ImportHistoryAsync(
            LeaveCsv(ImportHeader,
                "ahmad@x.com,Hospitalisation Leave,2026-03-02,2026-03-02,1,APPROVED,",
                "ahmad@x.com,AL,2026-03-04,2026-03-04,1,APPROVED,",
                "ahmad@x.com,hospitalisation leave,2026-03-06,2026-03-06,1,APPROVED,",
                "ahmad@x.com,Replacement Leave,2026-03-09,2026-03-09,1,APPROVED,",
                "ahmad@x.com,,2026-03-10,2026-03-10,1,APPROVED,"),
            TabularFormat.Csv,
            "usr-admin");

        Assert.Equal(1, result.Imported);
        Assert.Equal(4, result.Failed);
        Assert.Collection(result.Missing,
            m => { Assert.Equal("leaveType", m.Column); Assert.Equal("Hospitalisation Leave", m.Value); Assert.Equal(2, m.Rows); },
            m => { Assert.Equal("Replacement Leave", m.Value); Assert.Equal(1, m.Rows); });
    }

    // The source Days figure is trusted rather than recomputed: the org's
    // working-week today may not be the one in force back then.
    [Fact]
    public async Task Import_TrustsTheSourceDayCount_EvenAcrossAWeekend()
    {
        var service = ImportService();

        await service.ImportHistoryAsync(
            LeaveCsv(ImportHeader, "ahmad@x.com,AL,2026-03-06,2026-03-09,1.5,APPROVED,"),
            TabularFormat.Csv,
            "usr-admin");

        Assert.Equal(1.5, Assert.Single(await service.GetMineAsync("usr-emp")).TotalDays);
    }

    // Importing more days than the entitlement allows must still record them —
    // the leave already happened, and refusing wouldn't un-take it.
    [Fact]
    public async Task Import_SkipsTheBalanceCheck()
    {
        var service = ImportService();

        var result = await service.ImportHistoryAsync(
            LeaveCsv(ImportHeader, "ahmad@x.com,AL,2026-03-02,2026-12-31,99,APPROVED,"),
            TabularFormat.Csv,
            "usr-admin");

        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task Import_IsIdempotentOnEmployeeTypeAndExactDates()
    {
        var service = ImportService();
        var file = LeaveCsv(ImportHeader, "ahmad@x.com,AL,2026-03-02,2026-03-03,2,APPROVED,");

        var first = await service.ImportHistoryAsync(file, TabularFormat.Csv, "usr-admin");
        var second = await service.ImportHistoryAsync(file, TabularFormat.Csv, "usr-admin");

        Assert.Equal(1, first.Imported);
        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Skipped);
        Assert.Single(await service.GetMineAsync("usr-emp"));
    }

    [Fact]
    public async Task Import_ResolvesALeaveTypeByNameOrCode()
    {
        var byName = await ImportService().ImportHistoryAsync(
            LeaveCsv(ImportHeader, "ahmad@x.com,AL,2026-03-02,2026-03-02,1,APPROVED,"),
            TabularFormat.Csv, "usr-admin");
        Assert.Equal(1, byName.Imported);

        var unknown = await ImportService().ImportHistoryAsync(
            LeaveCsv(ImportHeader, "ahmad@x.com,Sabbatical,2026-03-02,2026-03-02,1,APPROVED,"),
            TabularFormat.Csv, "usr-admin");
        Assert.Equal(0, unknown.Imported);
        Assert.Contains("Sabbatical", Assert.Single(unknown.Errors).Message);
    }

    [Fact]
    public async Task Import_ReportsBadRowsByTheirSpreadsheetRowNumber()
    {
        var result = await ImportService().ImportHistoryAsync(
            LeaveCsv(ImportHeader,
                "ahmad@x.com,AL,2026-03-05,2026-03-02,2,APPROVED,",     // row 2: end before start
                "ahmad@x.com,AL,2026-03-02,2026-03-03,0,APPROVED,",     // row 3: zero days
                "ahmad@x.com,AL,2026-03-02,2026-03-03,2,MAYBE,",        // row 4: bad status
                "ahmad@x.com,AL,2026-04-02,2026-04-03,2,APPROVED,ok"),  // row 5: fine
            TabularFormat.Csv, "usr-admin");

        Assert.Equal(1, result.Imported);
        Assert.Equal([2, 3, 4], result.Errors.Select(e => e.Row));
    }

    [Fact]
    public async Task Import_RecordsWhoReallyCreatedTheRow()
    {
        var service = ImportService();

        await service.ImportHistoryAsync(
            LeaveCsv(ImportHeader, "ahmad@x.com,AL,2026-03-02,2026-03-03,2,APPROVED,"),
            TabularFormat.Csv, "usr-admin");

        var app = Assert.Single(await service.GetMineAsync("usr-emp"));
        var trail = await service.GetAuditTrailAsync(app.Id);
        var entry = Assert.Single(trail.Entries!);
        Assert.Equal("IMPORTED", entry.Decision);
        Assert.Equal("usr-admin", entry.ApproverId);
    }

    [Fact]
    public async Task Import_SkipsTheTemplatesExampleRow()
    {
        var service = ImportService();
        var template = service.BuildImportTemplate(TabularFormat.Xlsx);

        var result = await service.ImportHistoryAsync(
            template.Content, TabularFormat.Xlsx, "usr-admin");

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task Export_WritesIdentityColumnsOnEveryRow_InBothFormats()
    {
        var service = MakeService(
            types: [MakeType("lt-al", "AL", 12)],
            memberships: new FakeMembershipRepository("usr-emp"),
            currentUser: new FakeCurrentUser("usr-admin", "Admin"),
            employees: new FakeEmployeeDirectory(Ahmad));

        foreach (var format in new[] { TabularFormat.Csv, TabularFormat.Xlsx })
        {
            var export = await service.ExportBalancesAsync("usr-emp", Year, format);
            var rows = TabularReader.Read(export.Content, format);

            Assert.Equal("Employee", rows[0][0]);
            Assert.Equal("Code", rows[0][2]);
            Assert.All(rows.Skip(1), r => Assert.Equal("ahmad@x.com", r[0]));
            Assert.EndsWith($".{format.Extension()}", export.FileName);
        }
    }

    // ---- Admin cancels approved leave ----
    //
    // The correction path for approved leave that is no longer being taken.
    // The days come back because "taken" is summed from APPROVED rows only.

    [Fact]
    public async Task AdminCancel_ApprovedLeave_ReturnsDaysToBalance()
    {
        var apps = new[] { MakeApp("a1", "usr-emp", "t-al", 3, LeaveStatus.APPROVED) };
        var service = MakeService(types: [MakeType("t-al", "AL", 14)], apps: apps);

        var result = await service.AdminCancelAsync("a1", "usr-admin", "Trip called off");

        Assert.True(result.Transitioned);
        Assert.Equal(LeaveStatus.CANCELLED, result.Application!.Status);
        var al = (await service.GetBalancesAsync("usr-emp", Year)).Single();
        Assert.Equal(0, al.TakenDays);
        Assert.Equal(14, al.RemainingDays);
    }

    [Fact]
    public async Task AdminCancel_RecordsItOnTheTrail_AndTellsTheEmployee()
    {
        var app = MakeApp("a1", "usr-emp", "t-al", 2, LeaveStatus.APPROVED);
        var notifications = new FakeNotificationService();
        var service = MakeService(types: [MakeType("t-al", "AL", 14)], apps: [app], notifications: notifications);

        await service.AdminCancelAsync("a1", "usr-admin", "  Trip called off  ");

        Assert.Contains("\"ADMIN_CANCELLED\"", app.Approvals);
        Assert.Contains("Trip called off", app.Approvals);
        var sent = Assert.Single(notifications.Sent);
        Assert.Equal("usr-emp", sent.UserId);
        Assert.Equal("Leave cancelled", sent.Title);
        Assert.Contains("Reason: Trip called off", sent.Body);
    }

    // Rejected leave never took a day, so there is nothing to cancel.
    [Fact]
    public async Task AdminCancel_RefusesRejectedLeave()
    {
        var app = MakeApp("a1", "usr-emp", "t-al", 2, LeaveStatus.REJECTED);
        var service = MakeService(types: [MakeType("t-al", "AL", 14)], apps: [app]);

        var result = await service.AdminCancelAsync("a1", "usr-admin", null);

        Assert.True(result.Found);
        Assert.False(result.Transitioned);
        Assert.Equal(LeaveStatus.REJECTED, app.Status);
    }

    // "Admin can cancel any leave": a pending request too — it leaves the
    // approver's queue, and nobody is told days came back (none were taken).
    [Fact]
    public async Task AdminCancel_APendingRequest_IsCancelledAndLogged()
    {
        var app = MakeApp("a1", "usr-emp", "t-al", 2, LeaveStatus.PENDING);
        var notifications = new FakeNotificationService();
        var audit = new AltomateHR.Api.Tests.Audit.FakeAuditService();
        var service = MakeService(types: [MakeType("t-al", "AL", 14)], apps: [app],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }),
            notifications: notifications, audit: audit);

        var result = await service.AdminCancelAsync("a1", "usr-admin", "Duplicate");

        Assert.True(result.Transitioned);
        Assert.Equal(LeaveStatus.CANCELLED, app.Status);
        Assert.DoesNotContain("back in your balance", Assert.Single(notifications.Sent).Body);
        var logged = Assert.Single(audit.Written);
        Assert.Equal(AltomateHR.Api.Modules.Audit.AuditActions.LeaveAdminCancel, logged.Action);
        Assert.Equal("a1", logged.TargetId);
    }

    // ---- The employee asks to cancel approved leave ----
    //
    // Up the same chain the leave went through, layer by layer; the last
    // layer's approval cancels it and the days come back. Every step is on the
    // leave's trail AND in the activity log.

    private static readonly DateTime Soon = DateTime.UtcNow.Date.AddDays(14);

    private static LeaveApplication FutureApproved(double days = 3) =>
        new()
        {
            Id = "a1", OrganizationId = "org-1", EmployeeId = "usr-emp", LeaveTypeId = "t-al",
            StartDate = Soon, EndDate = Soon.AddDays(days - 1), TotalDays = days,
            Status = LeaveStatus.APPROVED, CurrentStep = 1,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };

    private static (LeaveService Service, FakeNotificationService Notes, AltomateHR.Api.Tests.Audit.FakeAuditService Audit)
        CancelService(LeaveApplication app, List<List<string>>? chain = null)
    {
        var notes = new FakeNotificationService();
        var audit = new AltomateHR.Api.Tests.Audit.FakeAuditService();
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)], apps: [app],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = chain ?? [["usr-super"], ["usr-mgr"]] }),
            notifications: notes, audit: audit);
        return (service, notes, audit);
    }

    private static string[] Trail(LeaveApplication app) =>
        [.. System.Text.Json.JsonDocument.Parse(app.Approvals ?? "[]").RootElement
            .EnumerateArray().Select(e => e.GetProperty("decision").GetString()!)];

    [Fact]
    public async Task Request_GoesToTheFirstLayer_AndTheLeaveStillCounts()
    {
        var app = FutureApproved();
        var (service, notes, audit) = CancelService(app);

        var result = await service.RequestCancellationAsync("a1", "usr-emp", "Trip postponed");

        Assert.True(result.Transitioned, result.Error);
        Assert.Equal(LeaveStatus.APPROVED, app.Status);                    // still approved…
        Assert.Equal(LeaveCancellationStatus.PENDING, app.CancellationStatus);
        Assert.Equal(3, (await service.GetBalancesAsync("usr-emp", Soon.Year)).Single().TakenDays);  // …and still taken
        Assert.Single(await service.GetTeamAsync("usr-super"));             // in the first layer's queue
        Assert.Empty(await service.GetTeamAsync("usr-mgr"));                // not yet the second's
        Assert.Contains(notes.Sent, n => n.UserId == "usr-super" && n.Title == "Leave cancellation to review");
        Assert.Equal(AltomateHR.Api.Modules.Audit.AuditActions.LeaveCancelRequest, Assert.Single(audit.Written).Action);
        Assert.Equal(["CANCELLATION_REQUESTED"], Trail(app));
    }

    [Fact]
    public async Task EachLayerApproves_InTurn_AndTheLastOneCancelsAndReturnsTheDays()
    {
        var app = FutureApproved();
        var (service, notes, audit) = CancelService(app);
        await service.RequestCancellationAsync("a1", "usr-emp", null);

        var first = await service.ApproveCancellationAsync("a1", "usr-super");
        Assert.True(first.Transitioned, first.Error);
        Assert.Equal(LeaveStatus.APPROVED, app.Status);                    // one layer isn't enough
        Assert.Empty(await service.GetTeamAsync("usr-super"));
        Assert.Single(await service.GetTeamAsync("usr-mgr"));

        var last = await service.ApproveCancellationAsync("a1", "usr-mgr");

        Assert.True(last.Transitioned, last.Error);
        Assert.Equal(LeaveStatus.CANCELLED, app.Status);
        Assert.Equal(LeaveCancellationStatus.APPROVED, app.CancellationStatus);
        var balance = (await service.GetBalancesAsync("usr-emp", Soon.Year)).Single();
        Assert.Equal(0, balance.TakenDays);
        Assert.Equal(14, balance.RemainingDays);
        Assert.Contains(notes.Sent, n => n.UserId == "usr-emp" && n.Title == "Leave cancelled"
            && n.Body.Contains("back in your balance"));
        Assert.Equal(["CANCELLATION_REQUESTED", "CANCELLATION_APPROVED", "CANCELLATION_APPROVED"], Trail(app));
        Assert.Equal(
            [AltomateHR.Api.Modules.Audit.AuditActions.LeaveCancelRequest,
             AltomateHR.Api.Modules.Audit.AuditActions.LeaveCancelApprove,
             AltomateHR.Api.Modules.Audit.AuditActions.LeaveCancelApprove,
             AltomateHR.Api.Modules.Audit.AuditActions.LeaveCancelled],
            audit.Written.Select(w => w.Action));
    }

    [Fact]
    public async Task OnlyTheCurrentLayerCanApprove()
    {
        var app = FutureApproved();
        var (service, _, _) = CancelService(app);
        await service.RequestCancellationAsync("a1", "usr-emp", null);

        var skipAhead = await service.ApproveCancellationAsync("a1", "usr-mgr");

        Assert.False(skipAhead.Transitioned);
        Assert.Equal("You are not authorized to review this step", skipAhead.Error);
        Assert.Equal(0, app.CancellationStep);
    }

    [Fact]
    public async Task ARejectedRequest_LeavesTheLeaveStanding_AndTellsTheEmployee()
    {
        var app = FutureApproved();
        var (service, notes, audit) = CancelService(app);
        await service.RequestCancellationAsync("a1", "usr-emp", null);

        var result = await service.RejectCancellationAsync("a1", "usr-super", "We need you that week");

        Assert.True(result.Transitioned, result.Error);
        Assert.Equal(LeaveStatus.APPROVED, app.Status);
        Assert.Equal(LeaveCancellationStatus.REJECTED, app.CancellationStatus);
        Assert.Empty(await service.GetTeamAsync("usr-super"));
        Assert.Contains(notes.Sent, n => n.UserId == "usr-emp" && n.Body.Contains("We need you that week"));
        Assert.Equal(AltomateHR.Api.Modules.Audit.AuditActions.LeaveCancelReject, audit.Written.Last().Action);
    }

    [Fact]
    public async Task WithNobodyAboveThem_TheLeaveIsCancelledAtOnce()
    {
        var app = FutureApproved();
        var (service, _, audit) = CancelService(app, chain: []);

        var result = await service.RequestCancellationAsync("a1", "usr-emp", null);

        Assert.True(result.Transitioned, result.Error);
        Assert.Equal(LeaveStatus.CANCELLED, app.Status);
        Assert.Equal(["CANCELLATION_REQUESTED", "CANCELLATION_AUTO_APPROVED"], Trail(app));
        Assert.Contains(audit.Written, w => w.Action == AltomateHR.Api.Modules.Audit.AuditActions.LeaveCancelled);
    }

    [Fact]
    public async Task LeaveThatHasStarted_CantBeCancelledByTheEmployee()
    {
        var app = FutureApproved();
        app.StartDate = DateTime.UtcNow.Date.AddDays(-1);
        var (service, _, audit) = CancelService(app);

        var result = await service.RequestCancellationAsync("a1", "usr-emp", null);

        Assert.False(result.Transitioned);
        Assert.Contains("already started", result.Error);
        Assert.Null(app.CancellationStatus);
        Assert.Empty(audit.Written);
    }

    [Fact]
    public async Task OnlyTheApplicantCanAsk()
    {
        var app = FutureApproved();
        var (service, _, _) = CancelService(app);

        var result = await service.RequestCancellationAsync("a1", "usr-someone-else", null);

        Assert.False(result.Transitioned);
        Assert.Null(app.CancellationStatus);
    }

    [Fact]
    public async Task TheEmployeeCanWithdrawTheRequest()
    {
        var app = FutureApproved();
        var (service, _, audit) = CancelService(app);
        await service.RequestCancellationAsync("a1", "usr-emp", null);

        var result = await service.WithdrawCancellationAsync("a1", "usr-emp");

        Assert.True(result.Transitioned, result.Error);
        Assert.Equal(LeaveStatus.APPROVED, app.Status);
        Assert.Equal(LeaveCancellationStatus.WITHDRAWN, app.CancellationStatus);
        Assert.Empty(await service.GetTeamAsync("usr-super"));
        Assert.Equal(AltomateHR.Api.Modules.Audit.AuditActions.LeaveCancelWithdraw, audit.Written.Last().Action);
    }

    // An admin can step in while a cancellation is still going up the chain.
    [Fact]
    public async Task AnAdminCanCancelWhileARequestIsUnderReview()
    {
        var app = FutureApproved();
        var (service, _, _) = CancelService(app);
        await service.RequestCancellationAsync("a1", "usr-emp", null);

        var result = await service.AdminCancelAsync("a1", "usr-admin", null);

        Assert.True(result.Transitioned);
        Assert.Equal(LeaveStatus.CANCELLED, app.Status);
        Assert.Empty(await service.GetTeamAsync("usr-super"));
    }

    // The unreachable-approver sweep treats a stranded cancellation the way it
    // treats a stranded request: it goes through.
    [Fact]
    public async Task AStrandedCancellation_IsGrantedByTheReconcileSweep()
    {
        var app = FutureApproved();
        var (service, _, _) = CancelService(app);
        await service.RequestCancellationAsync("a1", "usr-emp", null);
        app.CancellationStep = 5;   // past the top of the chain — nobody left to ask

        var stuck = await service.ReconcileUnreachableApprovalsAsync(apply: true);

        Assert.Equal(1, stuck);
        Assert.Equal(LeaveStatus.CANCELLED, app.Status);
    }

    [Fact]
    public async Task AdminCancel_UnknownId_IsNotFound()
    {
        var result = await MakeService().AdminCancelAsync("nope", "usr-admin", null);

        Assert.False(result.Found);
    }

    [Theory]
    [InlineData(false, 1)]   // unpaid: payroll deducted these days, so drafts need a re-run
    [InlineData(true, 0)]    // paid: no payslip figure depends on it
    public async Task AdminCancel_FlagsPayrollDrafts_OnlyForUnpaidLeave(bool paid, int expectedSweeps)
    {
        var drafts = new RecordingDraftStaleness();
        var service = MakeService(
            types: [MakeType("t-x", "UL", 14, paid: paid)],
            apps: [MakeApp("a1", "usr-emp", "t-x", 2, LeaveStatus.APPROVED)],
            drafts: drafts);

        await service.AdminCancelAsync("a1", "usr-admin", null);

        Assert.Equal(expectedSweeps, drafts.Covering.Count);
    }

    private sealed class RecordingDraftStaleness : AltomateHR.Api.Modules.Payroll.IPayrollDraftStaleness
    {
        public List<(DateTime From, DateTime To)> Covering { get; } = [];
        public Task MarkAllDraftsAsync() => Task.CompletedTask;
        public Task MarkDraftsCoveringAsync(DateTime from, DateTime to)
        {
            Covering.Add((from, to));
            return Task.CompletedTask;
        }
    }

    private static LeaveService MakeService(
        IEnumerable<LeaveType>? types = null,
        IEnumerable<LeaveApplication>? apps = null,
        ISupervisionService? supervision = null,
        IPolicyService? policy = null,
        IApprovalRouter? router = null,
        IOrganizationMembershipRepository? memberships = null,
        ICurrentUser? currentUser = null,
        IEnumerable<LeaveEntitlement>? entitlementRows = null,
        IXeroService? xero = null,
        IEmployeeRowResolver? employees = null,
        FakeNotificationService? notifications = null,
        AltomateHR.Api.Modules.Payroll.IPayrollDraftStaleness? drafts = null,
        AltomateHR.Api.Modules.Audit.IAuditService? audit = null,
        IEnumerable<HolidayDto>? holidays = null,
        ILeaveApplicationRepository? appRepo = null,
        AltomateHR.Api.Modules.Employees.IEmployeeProfileService? profiles = null) =>
        new(
            appRepo ?? new FakeLeaveApplicationRepository(apps ?? []),
            new FakeLeaveTypeRepository(types ?? []),
            supervision ?? new FakeSupervisionService(),
            policy ?? new FakePolicyService(),
            router ?? new FakeApprovalRouter(),
            TestDirectory.Over(memberships ?? new FakeMembershipRepository("usr-emp")),
            currentUser ?? new FakeCurrentUser("usr-admin", "Admin"),
            new FakeEntitlementRepo(entitlementRows ?? []),
            xero ?? new FakeXeroService(),
            new FakeOrganizationService(),
            new FakeHolidayService(holidays),
            new FakeRealtimeService(),
            notifications ?? new FakeNotificationService(),
            employees ?? new FakeEmployeeDirectory(),
            new FakeTeamService(),
            new FakeProjectService(),
            drafts,
            audit,
            profiles);

    private static LeaveType ProRatedType(string id, double days) => new()
    {
        Id = id, OrganizationId = "org-1", Code = "AL", Name = "Annual",
        DefaultDays = days, AccrualMethod = LeaveAccrualMethod.PRO_RATED,
    };

    private static LeaveType MakeType(string id, string code, double days, bool paid = true, bool archived = false) => new()
    {
        Id = id,
        Code = code,
        Name = code,
        Paid = paid,
        DefaultDays = days,
        IsArchived = archived,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static LeaveApplication MakeApp(string id, string emp, string typeId, double days, LeaveStatus status) => new()
    {
        Id = id,
        EmployeeId = emp,
        LeaveTypeId = typeId,
        StartDate = new DateTime(Year, 9, 1),
        EndDate = new DateTime(Year, 9, 1).AddDays(days - 1),
        TotalDays = days,
        Status = status,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    // Only the gender lookup is used by LeaveService.
    private sealed class FakeGenders(Dictionary<string, Gender> genders)
        : AltomateHR.Api.Modules.Employees.IEmployeeProfileService
    {
        public Task<Gender?> GetGenderAsync(string userId) =>
            Task.FromResult<Gender?>(genders.TryGetValue(userId, out var g) ? g : null);
        public Task<IReadOnlyDictionary<string, Gender>> GetGendersAsync() =>
            Task.FromResult<IReadOnlyDictionary<string, Gender>>(genders);
        public Task<AltomateHR.Api.Modules.Employees.Dtos.EmployeeProfileDto?> GetAsync(string userId) =>
            throw new NotSupportedException();
        public Task<AltomateHR.Api.Modules.Employees.Dtos.EmployeeProfileDto?> SaveAsync(
            string userId, AltomateHR.Api.Modules.Employees.Dtos.EmployeeProfileDto dto) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<AltomateHR.Api.Modules.Employees.Dtos.WorkPermitDto>> GetWorkPermitsAsync() =>
            throw new NotSupportedException();
    }

    private sealed class FakeLeaveTypeRepository : ILeaveTypeRepository
    {
        private readonly List<LeaveType> _types;
        public FakeLeaveTypeRepository(IEnumerable<LeaveType> types) => _types = types.ToList();
        public Task<List<LeaveType>> GetAllAsync() => Task.FromResult(_types.ToList());
        public Task<LeaveType?> GetByIdAsync(string id) => Task.FromResult(_types.FirstOrDefault(t => t.Id == id));
        public Task<LeaveType?> GetByCodeAsync(string code) => Task.FromResult(_types.FirstOrDefault(t => t.Code == code));
        public Task<LeaveType> AddAsync(LeaveType type) { _types.Add(type); return Task.FromResult(type); }
        public Task UpdateAsync(LeaveType type) => Task.CompletedTask;
    }

    private sealed class FakeLeaveApplicationRepository : ILeaveApplicationRepository
    {
        private readonly List<LeaveApplication> _apps;
        public FakeLeaveApplicationRepository(IEnumerable<LeaveApplication> apps) => _apps = apps.ToList();
        public Task<LeaveApplication?> GetByXeroFileIdAsync(string fileId) =>
            Task.FromResult(_apps.FirstOrDefault(a => a.XeroFileId == fileId));
        public Task<List<LeaveApplication>> GetAllAsync() => Task.FromResult(_apps.ToList());
        public Task<LeaveApplication?> GetByIdAsync(string id) => Task.FromResult(_apps.FirstOrDefault(a => a.Id == id));
        public Task<List<LeaveApplication>> GetByEmployeeAsync(string employeeId) =>
            Task.FromResult(_apps.Where(a => a.EmployeeId == employeeId).ToList());
        public Task<LeaveApplication> AddAsync(LeaveApplication app) { _apps.Add(app); return Task.FromResult(app); }
        public Task UpdateAsync(LeaveApplication app) => Task.CompletedTask;

        // Runs between the edit's read and its guarded save — where a
        // concurrent approval would land.
        public Action<LeaveApplication>? BeforeEditSave { get; set; }
        public int EditSaves { get; private set; }

        public Task<bool> SaveEditIfUntouchedAsync(LeaveApplication app)
        {
            BeforeEditSave?.Invoke(app);
            var untouched = app.Status == LeaveStatus.PENDING && app.CurrentStep == 0;
            if (untouched) EditSaves++;
            return Task.FromResult(untouched);
        }
    }

    // Only GetForUserInCurrentOrgAsync matters here: the ids passed to the ctor
    // are the "members of the current org"; anything else resolves to null (→ 404).
    private sealed class FakeMembershipRepository : IOrganizationMembershipRepository
    {
        public Task<List<OrganizationMembership>> GetAcrossAllOrgsAsync() => throw new NotSupportedException();

        private readonly Dictionary<string, DateTime?> _members;

        public FakeMembershipRepository(params string[] memberIds) =>
            _members = memberIds.ToDictionary(id => id, _ => (DateTime?)null);

        public FakeMembershipRepository(params (string Id, DateTime? JoinDate)[] members) =>
            _members = members.ToDictionary(m => m.Id, m => m.JoinDate);

        private string[] memberIds => _members.Keys.ToArray();

        public Task<OrganizationMembership?> GetForUserInCurrentOrgAsync(string userId) =>
            Task.FromResult(_members.TryGetValue(userId, out var join)
                ? new OrganizationMembership { UserId = userId, Role = "Employee", JoinDate = join }
                : null);

        public Task<List<OrganizationMembership>> GetByUserAsync(string userId) => throw new NotImplementedException();
        public Task<OrganizationMembership?> GetAsync(string organizationId, string userId) => throw new NotImplementedException();
        public Task<List<OrganizationMembership>> GetForCurrentOrgAsync() =>
            Task.FromResult(memberIds
                .Select(id => new OrganizationMembership { UserId = id, Role = "Employee" })
                .ToList());
        public Task<List<OrganizationMembership>> GetBySupervisorAsync(string supervisorId) => throw new NotImplementedException();
        public Task<int> CountByShiftIdAsync(string shiftId) => Task.FromResult(0);
        public Task AddAsync(OrganizationMembership membership) => throw new NotImplementedException();
        public Task UpdateAsync(OrganizationMembership membership) => throw new NotImplementedException();
    }

    private sealed class FakeEntitlementRepo(IEnumerable<LeaveEntitlement> rows) : ILeaveEntitlementRepository
    {
        public Task<List<LeaveEntitlement>> GetByYearAsync(int year) =>
            Task.FromResult(rows.Where(r => r.Year == year).ToList());
        public Task<List<LeaveEntitlement>> GetForEmployeeYearAsync(string employeeId, int year) =>
            Task.FromResult(rows.Where(r => r.EmployeeId == employeeId && r.Year == year).ToList());
        public Task<List<LeaveEntitlement>> GetCarryExpiringAsync(DateTime asOf) =>
            Task.FromResult(new List<LeaveEntitlement>());
        public Task AddAsync(LeaveEntitlement e) => Task.CompletedTask;
        public Task SaveAsync() => Task.CompletedTask;
    }

    // Leave only ever asks Xero for file content; the sync/connect surface is
    // irrelevant here, so it throws if anything else is touched.
    // Only the name is read, and only by the summary report.
    // No holidays unless a test configures some; GetInRangeAsync honours the
    // range it is asked for, like the real one.
    private sealed class FakeHolidayService(IEnumerable<HolidayDto>? holidays = null) : IHolidayService
    {
        private readonly List<HolidayDto> _holidays = holidays?.ToList() ?? [];

        public Task<IEnumerable<HolidayDto>> GetInRangeAsync(DateTime from, DateTime to) =>
            Task.FromResult<IEnumerable<HolidayDto>>(_holidays
                .Where(h => DateTime.Parse(h.Date) >= from.Date && DateTime.Parse(h.Date) <= to.Date)
                .ToList());
        public Task<IEnumerable<HolidayDto>> GetAllAsync() => throw new NotImplementedException();
        public Task<HolidaySaveResult> CreateAsync(SaveHolidayDto dto) => throw new NotImplementedException();
        public Task<HolidaySaveResult> UpdateAsync(string id, SaveHolidayDto dto) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string id) => throw new NotImplementedException();
        public Task<bool> IsHolidayAsync(DateTime date, string? projectId) => throw new NotImplementedException();
    
    // Importing is an admin action; nothing under test here performs one.
    public Task<HolidayImportResult> ImportAsync(int year, string countryCode) =>
        throw new NotSupportedException();
}

    // None of the existing tests exercise GetTeamBalancesAsync's team-scoped
    // path — they only need the constructor satisfied. No supervised teams,
    // no projects.
    private sealed class FakeTeamService : ITeamService
    {
        public Task<IEnumerable<TeamDto>> GetAllAsync() => Task.FromResult<IEnumerable<TeamDto>>([]);
        public Task<TeamSaveResult> CreateAsync(CreateTeamDto dto) => throw new NotImplementedException();
        public Task<TeamSaveResult> UpdateAsync(string id, SaveTeamDto dto) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string id) => throw new NotImplementedException();
        public Task<TeamSaveResult> AddOrUpdateMemberAsync(string teamId, SaveMembershipDto dto) =>
            throw new NotImplementedException();
        public Task<TeamSaveResult> RemoveMemberAsync(string teamId, string employeeId) =>
            throw new NotImplementedException();
        public Task<IEnumerable<ApprovalStepDto>> GetApprovalChainAsync(
            string employeeId, ApprovalModule module, string? projectId = null) =>
            Task.FromResult<IEnumerable<ApprovalStepDto>>([]);
        public Task<IReadOnlyList<string>> GetMemberEmployeeIdsAsync(string teamId) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<SupervisedTeamDto>> GetSupervisedTeamsAsync(string userId) =>
            Task.FromResult<IReadOnlyList<SupervisedTeamDto>>([]);
        public Task<IReadOnlyList<string>> GetReportEmployeeIdsAsync(string supervisorId) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<string>> GetProjectIdsForMemberAsync(string employeeId) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<LayerApproverOptionsDto>?> GetApproverOptionsAsync(string teamId, string employeeId) =>
            Task.FromResult<IReadOnlyList<LayerApproverOptionsDto>?>(null);
        public Task<ApproverOverrideResult> SetApproverOverrideAsync(
            string teamId, string employeeId, int layer, List<string> approverIds) =>
            throw new NotImplementedException();
        public Task<ApproverOverrideResult> ClearApproverOverrideAsync(string teamId, string employeeId, int layer) =>
            throw new NotImplementedException();
    }

    private sealed class FakeProjectService : IProjectService
    {
        // Read by the attendance clock-in gate; nothing under test here uses them.
        public Task<IReadOnlyList<ProjectGeofencePoint>> GetGeofencePointsAsync(string projectId) =>
            Task.FromResult<IReadOnlyList<ProjectGeofencePoint>>([]);
        public Task<IReadOnlyList<ProjectAllowedIp>> GetAllowedIpsAsync(string projectId) =>
            Task.FromResult<IReadOnlyList<ProjectAllowedIp>>([]);

        public Task<IEnumerable<ProjectDto>> GetForMemberAsync(string userId) =>
            throw new NotSupportedException();
        public Task<IEnumerable<ProjectDto>> GetAllAsync() => Task.FromResult<IEnumerable<ProjectDto>>([]);
        public Task<ProjectDto?> GetByIdAsync(string id) => Task.FromResult<ProjectDto?>(null);
        public Task<ProjectDto> CreateAsync(SaveProjectDto dto) => throw new NotImplementedException();
        public Task<ProjectDto?> UpdateAsync(string id, SaveProjectDto dto) => throw new NotImplementedException();
        public Task<ProjectDto?> SetArchivedAsync(string id, bool archived) => throw new NotImplementedException();
    }

    private sealed class FakeOrganizationService : IOrganizationService
    {
        public Task<IReadOnlyList<AdminAccessDto>> ListAdminsAsync() =>
            Task.FromResult<IReadOnlyList<AdminAccessDto>>(Array.Empty<AdminAccessDto>());
        public Task<AdminAccessDto?> SetAdminModulesAsync(string userId, List<string>? modules) =>
            Task.FromResult<AdminAccessDto?>(null);

        public Task<OrganizationDto?> SetClaimSettingsAsync(
            string organizationId, int cutoffDay, ClaimSettlement settlementRoute,
            XeroBillStatus xeroBillStage) =>
            throw new NotSupportedException();

        public Task<OrganizationDto?> GetByIdAsync(string organizationId) =>
            Task.FromResult<OrganizationDto?>(new OrganizationDto { Id = organizationId, Name = "Test Org" });
        public Task<OrganizationDto> CreateAsync(CreateOrganizationDto dto, string ownerUserId, bool viaSso = false) => throw new NotImplementedException();
        public Task<OrganizationDto?> UpdateAsync(string organizationId, UpdateOrganizationDto dto) => throw new NotImplementedException();
        public Task<OrganizationDto?> UpdatePlanAsync(string organizationId, UpdateOrgPlanDto dto) => throw new NotImplementedException();
    }

    private sealed class FakeXeroService(XeroFileContent? file = null) : IXeroService
    {
        public Task<IReadOnlyList<XeroCurrencyResponse>> GetCurrenciesAsync() =>
            Task.FromResult<IReadOnlyList<XeroCurrencyResponse>>([]);

        public Task<XeroFileContent?> GetFileContentAsync(string fileId) => Task.FromResult(file);
        public Task<XeroConnectUrlDto> CreateConnectUrlAsync(string? r) => throw new NotImplementedException();
        public Task<string> CompleteCallbackAsync(string c, string s) => throw new NotImplementedException();
        public Task<XeroStatusDto> GetStatusAsync() => throw new NotImplementedException();
        public Task DisconnectAsync() => throw new NotImplementedException();
        public Task<XeroSyncAccountsResultDto> SyncAccountsAsync() => throw new NotImplementedException();
        public Task<XeroSyncProjectsResultDto> SyncProjectsAsync() => throw new NotImplementedException();
        public Task<XeroBillResponse> CreateBillAsync(XeroBillRequest b) => throw new NotImplementedException();
        public Task<XeroSpendResponse> CreateSpendAsync(XeroSpendRequest s) => throw new NotImplementedException();
        public Task<bool> IsConnectedAsync() => Task.FromResult(false);
    
    // Payroll posts through these; nothing in this file's scenarios does.
    public Task<XeroManualJournalResponse> CreateManualJournalAsync(XeroManualJournalRequest journal) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<XeroTrackingCategoryResponse>> GetTrackingCategoriesAsync() =>
        Task.FromResult<IReadOnlyList<XeroTrackingCategoryResponse>>([]);
    public Task<XeroProjectTrackingDto> GetProjectTrackingAsync() => throw new NotSupportedException();
    public Task<AltomateHR.Api.Modules.Xero.Dtos.XeroSyncProjectsResultDto?> SetProjectTrackingCategoryAsync(string? categoryId) => throw new NotSupportedException();


    // No connection in these tests, so an attachment would fall back to disk.
    public Task<bool> TryAttachFileAsync(string fileId, string objectId, XeroAttachTarget target) =>
        Task.FromResult(false);

    public Task<XeroUploadedFile?> TryUploadFileAsync(
        string folderName, byte[] content, string fileName, string contentType) =>
        Task.FromResult<XeroUploadedFile?>(null);
}

    private sealed class FakeCurrentUser(string? userId, string? role) : ICurrentUser
    {
    public string? Email => "test@altomate.com";
        public string? UserId => userId;
        public string? Role => role;
        public string? OrganizationId => "org-1";
        public bool IsAdmin => role is "Admin" or "Owner";
        public bool IsAuthenticated => userId is not null;
        public string? IpAddress => null;
    }

    private sealed class FakePolicyService : IPolicyService
    {
        private readonly IReadOnlyDictionary<string, double> _entitlements;
        public FakePolicyService(IReadOnlyDictionary<string, double>? entitlements = null) =>
            _entitlements = entitlements ?? new Dictionary<string, double>();

        public Task<IReadOnlyDictionary<string, double>> GetLeaveEntitlementsAsync(string employeeId) =>
            Task.FromResult(_entitlements);
        public Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>
            GetLeaveEntitlementsForEmployeesAsync(IEnumerable<string> employeeIds) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>(
                employeeIds.Distinct().ToDictionary(id => id, _ => _entitlements));
        public Task<IReadOnlyDictionary<string, EmployeePolicy>>
            GetEffectivePoliciesForEmployeesAsync(IEnumerable<string> employeeIds) =>
            Task.FromResult<IReadOnlyDictionary<string, EmployeePolicy>>(
                new Dictionary<string, EmployeePolicy>());
        public Task<bool> RequiresGeofenceAsync(string employeeId) => Task.FromResult(true);
        public Task<EmployeePolicy?> GetEffectivePolicyAsync(string employeeId) =>
            Task.FromResult<EmployeePolicy?>(null);
        public Task<IEnumerable<PolicyDto>> GetAllAsync() => throw new NotImplementedException();
        public Task<PolicySaveResult> CreateAsync(SavePolicyDto dto) => throw new NotImplementedException();
        public Task<PolicySaveResult> UpdateAsync(string id, SavePolicyDto dto) => throw new NotImplementedException();
        public Task<PolicyDto?> SetArchivedAsync(string id, bool archived) => throw new NotImplementedException();
        public Task<PolicyDto?> SetDefaultAsync(string id) => throw new NotImplementedException();

        public Task<IReadOnlyList<EmployeePolicy>> GetAllAcrossOrgsAsync() =>
            Task.FromResult<IReadOnlyList<EmployeePolicy>>([]);

        public Task<IReadOnlyList<PolicyLeaveEntitlement>> GetAllPolicyEntitlementsAsync() =>
            Task.FromResult<IReadOnlyList<PolicyLeaveEntitlement>>([]);
    
    // Module access is not what these tests are about; full access keeps them
    // on topic.
    public Task<AltomateHR.Api.Modules.Policies.PolicyModuleAccess> GetModuleAccessAsync(string employeeId) =>
        Task.FromResult(AltomateHR.Api.Modules.Policies.PolicyModuleAccess.All);
}
}
