using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Onboarding;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Tests.Onboarding;

// Provisioning-time configuration, applied in one call.
//
// Two things here are unusual and both are deliberate: overtime fans out across
// EVERY policy rather than the default, and a partial application is a real
// outcome the caller is told about rather than an error.
public class OnboardingServiceTests
{
    private const string Org = "org-1";

    private static (OnboardingService Svc, Fixture Fx) Make()
    {
        var fx = new Fixture();
        return (new OnboardingService(
            new StubOrgs(fx.Org), new StubPolicies(fx.Policies),
            new StubLeaveTypes(fx.LeaveTypes), new StubPayrollSettings(fx),
            new StubCurrentUser()), fx);
    }

    private sealed class Fixture
    {
        public Organization Org { get; } = new() { Id = OnboardingServiceTests.Org, Name = "Acme" };
        public PayrollSettings? Settings { get; set; }
        public List<EmployeePolicy> Policies { get; } =
        [
            new() { Id = "pol-monthly", Name = "Monthly Workers" },
            new() { Id = "pol-hourly", Name = "Hourly Workers" },
            new() { Id = "pol-old", Name = "Retired", IsArchived = true },
        ];
        public List<LeaveType> LeaveTypes { get; } =
        [
            new() { Id = "lt-annual", Code = "ANNUAL", Name = "Annual Leave", DefaultDays = 8 },
            new() { Id = "lt-medical", Code = "MEDICAL", Name = "Medical Leave", DefaultDays = 14 },
        ];
    }

    // ─── The OT fan-out ─────────────────────────────────────────────────

    // THE reason this endpoint exists. Provisioning seeds two policies; writing
    // overtime to the default alone leaves hourly staff on statutory rates while
    // monthly staff get the client's — wrong payslips, with nothing on screen
    // to say so.
    [Fact]
    public async Task OvertimeAppliesToEveryLivePolicy()
    {
        var (svc, fx) = Make();

        var result = await svc.ApplyAsync(new OnboardingDto
        {
            Overtime = new OnboardingOvertimeDto { NormalDay = 1.75m, RestDay = 2.5m },
        });

        Assert.Contains("overtime", result.Applied);
        Assert.Equal(2, result.PoliciesUpdated.Count);
        Assert.All(fx.Policies.Where(p => !p.IsArchived), p =>
        {
            Assert.Equal(1.75m, p.OtRateNormalDay);
            Assert.Equal(2.5m, p.OtRateRestDay);
        });
    }

    // An archived policy is one nobody is paid under. Rewriting it would
    // resurrect rates for a group that was deliberately retired.
    [Fact]
    public async Task OvertimeLeavesAnArchivedPolicyAlone()
    {
        var (svc, fx) = Make();

        await svc.ApplyAsync(new OnboardingDto
        {
            Overtime = new OnboardingOvertimeDto { NormalDay = 1.75m },
        });

        Assert.Equal(1.50m, fx.Policies.Single(p => p.IsArchived).OtRateNormalDay);
    }

    // ─── Validation runs before any write ───────────────────────────────

    // They write the same setting, so accepting both would need a precedence
    // rule nobody could remember.
    [Fact]
    public async Task RefusesWorkingDaysAndNonWorkingDaysTogether()
    {
        var (svc, fx) = Make();

        var result = await svc.ApplyAsync(new OnboardingDto
        {
            Settings = new OnboardingSettingsDto
            {
                WorkingDays = ["MONDAY"],
                NonWorkingDays = ["SUNDAY"],
            },
            Overtime = new OnboardingOvertimeDto { NormalDay = 9m },
        });

        Assert.NotNull(result.ValidationError);
        Assert.Empty(result.Applied);
        // And nothing ran — the overtime block in the same body is untouched.
        Assert.Equal(1.50m, fx.Policies[0].OtRateNormalDay);
    }

    // A null rate levies nothing at all, which looks like a working
    // configuration and is not.
    [Fact]
    public async Task RefusesHrdfContributionWithoutARate()
    {
        var (svc, _) = Make();

        var result = await svc.ApplyAsync(new OnboardingDto
        {
            Calculation = new OnboardingCalculationDto
            {
                Hrdf = new OnboardingHrdfDto { Contribute = true, Rate = null },
            },
        });

        Assert.Contains("rate", result.ValidationError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefusesCarryForwardWithoutAnExpiryMonth()
    {
        var (svc, _) = Make();

        var result = await svc.ApplyAsync(new OnboardingDto
        {
            Leave = new OnboardingLeaveDto
            {
                CarryForward = new() { ["ANNUAL"] = new OnboardingCarryForwardDto { Enabled = true } },
            },
        });

        Assert.Contains("expiryMonth", result.ValidationError!);
    }

    // ─── Settings ───────────────────────────────────────────────────────

    // Stored as the days that ARE worked, so a non-working list is inverted
    // rather than the column meaning two things.
    [Fact]
    public async Task NonWorkingDaysAreStoredAsTheDaysWorked()
    {
        var (svc, fx) = Make();

        await svc.ApplyAsync(new OnboardingDto
        {
            Settings = new OnboardingSettingsDto { NonWorkingDays = ["SATURDAY", "SUNDAY"] },
        });

        Assert.Equal("1,2,3,4,5", fx.Org.WorkingDays);
    }

    // ─── Leave ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveIsKeyedByCodeWithoutRegardToCase()
    {
        var (svc, fx) = Make();

        var result = await svc.ApplyAsync(new OnboardingDto
        {
            Leave = new OnboardingLeaveDto { Entitlements = new() { ["annual"] = 16 } },
        });

        Assert.Contains("ANNUAL", result.LeaveTypesUpdated);
        Assert.Equal(16, fx.LeaveTypes.Single(t => t.Code == "ANNUAL").DefaultDays);
        // And an untouched type keeps its seeded value.
        Assert.Equal(14, fx.LeaveTypes.Single(t => t.Code == "MEDICAL").DefaultDays);
    }

    // Cleared when switched off, so a later re-enable cannot inherit a month
    // nobody chose.
    [Fact]
    public async Task TurningCarryForwardOffClearsItsSettings()
    {
        var (svc, fx) = Make();
        var annual = fx.LeaveTypes.Single(t => t.Code == "ANNUAL");
        annual.CarryForward = true;
        annual.CarryExpiryMonth = 3;
        annual.MaxCarryForwardDays = 5;

        await svc.ApplyAsync(new OnboardingDto
        {
            Leave = new OnboardingLeaveDto
            {
                CarryForward = new() { ["ANNUAL"] = new OnboardingCarryForwardDto { Enabled = false } },
            },
        });

        Assert.False(annual.CarryForward);
        Assert.Null(annual.CarryExpiryMonth);
        Assert.Null(annual.MaxCarryForwardDays);
    }

    // ─── Partial application ────────────────────────────────────────────

    // Not atomic, by design. A block that fails must not abandon the others:
    // they are independent, and the caller's retry re-sends the same body.
    [Fact]
    public async Task ReportsWhichBlocksLandedWhenOneFails()
    {
        var fx = new Fixture();
        var svc = new OnboardingService(
            new ThrowingOrgs(), new StubPolicies(fx.Policies),
            new StubLeaveTypes(fx.LeaveTypes), new StubPayrollSettings(fx),
            new StubCurrentUser());

        var result = await svc.ApplyAsync(new OnboardingDto
        {
            Settings = new OnboardingSettingsDto { NonWorkingDays = ["SUNDAY"] },
            Overtime = new OnboardingOvertimeDto { NormalDay = 1.75m },
        });

        Assert.Contains("settings", result.Failed);
        Assert.Contains("overtime", result.Applied);
        // The independent block really did apply.
        Assert.Equal(1.75m, fx.Policies[0].OtRateNormalDay);
    }

    // ─── Stubs ──────────────────────────────────────────────────────────

    private sealed class StubCurrentUser : ICurrentUser
    {
        public string? UserId => "usr-1";
        public string? OrganizationId => Org;
        public string? Role => "Admin";
        public bool IsAdmin => true;
        public bool IsAuthenticated => true;
        public string? Email => "admin@acme.com";
        public string? IpAddress => null;
    }

    private class StubOrgs(Organization org) : IOrganizationRepository
    {
        public virtual Task<Organization?> GetByIdAsync(string id) => Task.FromResult<Organization?>(org);
        public virtual Task UpdateAsync(Organization organization) => Task.CompletedTask;
        public Task<Organization?> GetFirstAsync() => Task.FromResult<Organization?>(org);
        public Task<List<Organization>> GetAllAsync() => Task.FromResult(new List<Organization> { org });
        public Task AddAsync(Organization organization) => Task.CompletedTask;
        public Task<bool> AnyAsync() => Task.FromResult(true);
    }

    private sealed class ThrowingOrgs() : StubOrgs(new Organization { Id = Org })
    {
        public override Task UpdateAsync(Organization organization) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class StubPolicies(List<EmployeePolicy> policies) : IEmployeePolicyRepository
    {
        public Task<List<EmployeePolicy>> GetAllAsync() => Task.FromResult(policies);
        public Task UpdateAsync(EmployeePolicy policy) => Task.CompletedTask;
        public Task<List<EmployeePolicy>> GetAllAcrossOrgsAsync() => Task.FromResult(policies);
        public Task<EmployeePolicy?> GetByIdAsync(string id) =>
            Task.FromResult(policies.FirstOrDefault(p => p.Id == id));
        public Task<EmployeePolicy?> GetByNameAsync(string name) =>
            Task.FromResult(policies.FirstOrDefault(p => p.Name == name));
        public Task<EmployeePolicy?> GetDefaultAsync() => Task.FromResult(policies.FirstOrDefault());
        public Task<EmployeePolicy> AddAsync(EmployeePolicy policy) { policies.Add(policy); return Task.FromResult(policy); }
        public Task DeleteAsync(EmployeePolicy policy) { policies.Remove(policy); return Task.CompletedTask; }
        public Task ClearDefaultExceptAsync(string id) => Task.CompletedTask;
    }

    private sealed class StubLeaveTypes(List<LeaveType> types) : ILeaveTypeRepository
    {
        public Task<List<LeaveType>> GetAllAsync() => Task.FromResult(types);
        public Task UpdateAsync(LeaveType type) => Task.CompletedTask;
        public Task<LeaveType?> GetByIdAsync(string id) =>
            Task.FromResult(types.FirstOrDefault(t => t.Id == id));
        public Task<LeaveType?> GetByCodeAsync(string code) =>
            Task.FromResult(types.FirstOrDefault(t => t.Code == code));
        public Task<LeaveType> AddAsync(LeaveType type) { types.Add(type); return Task.FromResult(type); }
        public Task DeleteAsync(LeaveType type) { types.Remove(type); return Task.CompletedTask; }
    }

    private sealed class StubPayrollSettings(Fixture fx) : IPayrollSettingsRepository
    {
        public Task<PayrollSettings?> GetAsync() => Task.FromResult(fx.Settings);
        public Task<PayrollSettings> AddAsync(PayrollSettings settings)
        {
            fx.Settings = settings;
            return Task.FromResult(settings);
        }
        public Task UpdateAsync(PayrollSettings settings) { fx.Settings = settings; return Task.CompletedTask; }
    }
}
