using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Overtime;
using AltomateHR.Api.Modules.Overtime.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Employees;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Overtime;

// The company's overtime types: three built in, any number of its own, and
// what each pays for a policy.
public class OvertimeTypeServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly OvertimeTypeService _types;

    public OvertimeTypeServiceTests()
    {
        _db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"ot-types-{Guid.NewGuid()}").Options,
            new StubCurrentUser());
        var directory = TestDirectory.Over(
            new OrganizationMembershipRepository(_db), new UserRepository(_db), new EmployeeProfileRepository(_db));
        _types = new OvertimeTypeService(
            new OvertimeTypeRepository(_db), new PolicyOvertimeRateRepository(_db),
            new PolicyService(new EmployeePolicyRepository(_db), new PolicyLeaveEntitlementRepository(_db), directory));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task EveryCompany_HasTheThreeBuiltIns_AtTheStatutoryRates()
    {
        var all = await _types.GetAllAsync();

        Assert.Equal(
            [("Normal day", 1.50m), ("Rest day", 2.00m), ("Public holiday", 3.00m)],
            all.Select(t => (t.Name, t.Multiplier)));
        Assert.Equal(3, (await _types.GetAllAsync()).Count);   // made once, not again
    }

    [Fact]
    public async Task ABuiltIn_CanBeRenamed_ButNotRemoved()
    {
        var rest = (await _types.GetAllAsync()).Single(t => t.Kind == OvertimeTypeKind.REST_DAY);

        Assert.True((await _types.UpdateAsync(rest.Id, new() { Name = "Sunday", Multiplier = 2.5m })).Ok);
        var refused = await _types.ArchiveAsync(rest.Id);

        Assert.False(refused.Ok);
        Assert.Contains("built in", refused.Error);
    }

    [Fact]
    public async Task TwoTypes_CantShareAName()
    {
        await _types.CreateAsync(new() { Name = "Night OT", Multiplier = 1.25m });

        var clash = await _types.CreateAsync(new() { Name = "night ot", Multiplier = 1.5m });

        Assert.False(clash.Ok);
        Assert.Contains("already", clash.Error);
    }

    // A type switched off for a policy, or archived, isn't offered — but is
    // still priced, because hours approved for it are owed.
    [Fact]
    public async Task AnOffType_IsntOffered_ButIsStillPriced()
    {
        var night = (await _types.CreateAsync(new() { Name = "Night OT", Multiplier = 1.25m })).Type!;
        var standby = (await _types.CreateAsync(new() { Name = "Standby", Multiplier = 1m })).Type!;
        var policy = new EmployeePolicy { OrganizationId = "org-1", Name = "Workers", OtRateNormalDay = 1.6m };
        _db.EmployeePolicies.Add(policy);
        _db.PolicyOvertimeRates.Add(new PolicyOvertimeRate
            { OrganizationId = "org-1", PolicyId = policy.Id, OvertimeTypeId = night.Id, Enabled = false });
        await _db.SaveChangesAsync();
        await _types.ArchiveAsync(standby.Id);

        var rates = await new OvertimeTypeService(
            new OvertimeTypeRepository(_db), new PolicyOvertimeRateRepository(_db),
            new PolicyService(new EmployeePolicyRepository(_db), new PolicyLeaveEntitlementRepository(_db),
                TestDirectory.Over(new OrganizationMembershipRepository(_db), new UserRepository(_db), new EmployeeProfileRepository(_db))))
            .RatesForPolicyAsync(policy);

        Assert.Equal((1.6m, 2.00m, 3.00m), (rates.Normal, rates.Rest, rates.PublicHoliday));
        Assert.Equal((1.25m, false), (rates.Custom[night.Id].Multiplier, rates.Custom[night.Id].Offered));
        Assert.Equal((1m, false), (rates.Custom[standby.Id].Multiplier, rates.Custom[standby.Id].Offered));
    }
}
