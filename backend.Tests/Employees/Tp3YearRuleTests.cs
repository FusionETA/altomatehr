using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Employees;

// TP3 figures only count for the year they're entered against, so figures
// with no year would be silently ignored by payroll — saving refuses them.
public class Tp3YearRuleTests
{
    [Fact]
    public void FiguresWithoutAYear_AreRefused()
    {
        var problem = EmployeeProfileService.Tp3Problem(new EmployeeProfileDto { PrevPcb = 120m });
        Assert.Contains("enter the year", problem);
    }

    [Fact]
    public void AnItemByItemAmountWithoutAYear_IsRefusedToo()
    {
        var dto = new EmployeeProfileDto
        {
            PrevByCategoryJson = """[{"category":"deduct_tp1_life_insurance","amount":300}]""",
        };
        Assert.Contains("enter the year", EmployeeProfileService.Tp3Problem(dto));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void NoFiguresAndNoYear_IsFine(int? zero)
    {
        var dto = new EmployeeProfileDto { PrevRemuneration = zero is null ? null : 0m, PrevEpf = 0m };
        Assert.Null(EmployeeProfileService.Tp3Problem(dto));
    }

    [Fact]
    public void FiguresWithTheYear_AreFine()
    {
        var dto = new EmployeeProfileDto { PrevEmploymentYear = 2026, PrevRemuneration = 30000m, PrevPcb = 900m };
        Assert.Null(EmployeeProfileService.Tp3Problem(dto));
    }

    [Theory]
    [InlineData(26)]
    [InlineData(20266)]
    public void AYearThatIsntFourDigits_IsRefused(int year)
    {
        var problem = EmployeeProfileService.Tp3Problem(new EmployeeProfileDto { PrevEmploymentYear = year });
        Assert.Contains("4-digit year", problem);
    }

    // ─── Saving: only a TP3 change is checked ───────────────────────────

    // A record carried over (or saved before the rule) with figures but no
    // year: an unrelated save — e.g. an import changing the phone — must go
    // through, or one old record would fail a whole import.
    [Fact]
    public async Task AnUnrelatedSave_OfAnOlderRecordWithoutAYear_StillWorks()
    {
        var (service, db) = Build(new EmployeeProfile
        {
            Id = "emp-1", OrganizationId = "org-1", UserId = "usr-1", PrevPcb = 900m,
        });
        using var _ = db;

        var saved = await service.SaveAsync("usr-1", new EmployeeProfileDto { PrevPcb = 900m, Phone = "0123456789" });

        Assert.NotNull(saved);
        Assert.Equal("0123456789", saved!.Phone);
    }

    [Fact]
    public async Task ChangingTheTp3Figures_WithoutAYear_IsRefused()
    {
        var (service, db) = Build(new EmployeeProfile
        {
            Id = "emp-1", OrganizationId = "org-1", UserId = "usr-1", PrevPcb = 900m,
        });
        using var _ = db;

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveAsync("usr-1", new EmployeeProfileDto { PrevPcb = 950m }));
        Assert.Contains("enter the year", error.Message);
    }

    // EPF number: a changed one must be digits; an old bad one doesn't block
    // an unrelated save.
    [Fact]
    public async Task ChangingTheEpfNumberToAPlaceholder_IsRefused()
    {
        var (service, db) = Build(new EmployeeProfile { Id = "emp-1", OrganizationId = "org-1", UserId = "usr-1" });
        using var _ = db;

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveAsync("usr-1", new EmployeeProfileDto { EpfNumber = "XXXXXX" }));
        Assert.Contains("digits only", error.Message);
    }

    [Fact]
    public async Task AnOlderRecordWithAPlaceholderEpfNumber_CanStillBeSavedForSomethingElse()
    {
        var (service, db) = Build(new EmployeeProfile
        {
            Id = "emp-1", OrganizationId = "org-1", UserId = "usr-1", EpfNumber = "XXXXXX",
        });
        using var _ = db;

        var saved = await service.SaveAsync("usr-1", new EmployeeProfileDto { EpfNumber = "XXXXXX", Phone = "0123" });
        Assert.Equal("0123", saved!.Phone);
    }

    private static (EmployeeProfileService, AppDbContext) Build(EmployeeProfile stored)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"tp3-{Guid.NewGuid()}")
            .Options;
        var db = new AppDbContext(options, new StubCurrentUser());
        db.Users.Add(new User { Id = "usr-1", Email = "staff@x.com", Name = "Staff" });
        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            Id = "mem-1", OrganizationId = "org-1", UserId = "usr-1", Role = "Employee",
        });
        db.EmployeeProfiles.Add(stored);
        db.SaveChanges();

        var directory = TestDirectory.Over(
            new OrganizationMembershipRepository(db),
            new UserRepository(db),
            new EmployeeProfileRepository(db));
        return (new EmployeeProfileService(
            new EmployeeProfileRepository(db), new OrganizationMembershipRepository(db), directory, null!), db);
    }
}
