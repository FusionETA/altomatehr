using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Payroll;

public class PayrollCompanyInfoServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new();
    private readonly FakeAuditService _audit = new();
    private readonly PayrollCompanyInfoService _service;

    public PayrollCompanyInfoServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"payroll-company-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);
        _service = new PayrollCompanyInfoService(new PayrollCompanyInfoRepository(_db), _audit);
    }

    public void Dispose() => _db.Dispose();

    private static SavePayrollCompanyInfoDto Save(
        string? employerName = "Altomate Sdn Bhd",
        string? employerTin = "E1234567890",
        string? country = null) => new()
        {
            EmployerName = employerName,
            EmployerTin = employerTin,
            Country = country,
            PerkesoEmployerCode = "A1234567890",
            EpfEmployerNo = "1234567",
        };

    // ─── Reading before anything is configured ──────────────────────────

    [Fact]
    public async Task AnUnconfiguredOrg_GetsAnEmptyProfile()
    {
        var info = await _service.GetAsync();

        Assert.False(info.IsConfigured);
        Assert.Null(info.EmployerName);
        Assert.Null(info.UpdatedAt);
    }

    [Fact]
    public async Task ReadingDoesNotCreateARow()
    {
        await _service.GetAsync();

        Assert.Empty(await _db.PayrollCompanyInfos.ToListAsync());
    }

    // The filings need to tell "no employer profile" from "an empty one" — CP8D
    // can't be produced without a TIN, and emitting blanks is worse than failing.
    [Fact]
    public async Task GetEntity_IsNullUntilSomethingIsSaved()
    {
        Assert.Null(await _service.GetEntityAsync());

        await _service.SaveAsync(Save());

        Assert.NotNull(await _service.GetEntityAsync());
    }

    // ─── Saving ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TheFirstSave_CreatesExactlyOneRow()
    {
        var saved = await _service.SaveAsync(Save());

        Assert.True(saved.IsConfigured);
        Assert.Equal("Altomate Sdn Bhd", saved.EmployerName);
        Assert.Equal("E1234567890", saved.EmployerTin);

        var rows = await _db.PayrollCompanyInfos.ToListAsync();
        Assert.Single(rows);
        Assert.Equal("org-1", rows[0].OrganizationId);
    }

    [Fact]
    public async Task ASecondSave_UpdatesInPlace()
    {
        await _service.SaveAsync(Save());
        var updated = await _service.SaveAsync(Save(employerName: "Altomate Holdings Bhd"));

        Assert.Equal("Altomate Holdings Bhd", updated.EmployerName);
        Assert.Single(await _db.PayrollCompanyInfos.ToListAsync());
    }

    // Malaysia-only product: a blank country means Malaysia, not "unknown".
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankCountry_DefaultsToMalaysia(string? country)
    {
        var saved = await _service.SaveAsync(Save(country: country));

        Assert.Equal("Malaysia", saved.Country);
    }

    [Fact]
    public async Task AnExplicitCountry_IsKept()
    {
        Assert.Equal("Singapore", (await _service.SaveAsync(Save(country: "Singapore"))).Country);
    }

    // Every statutory body issues its own number; they must round-trip as
    // separate fields rather than collapsing into one.
    [Fact]
    public async Task TheStatutoryRegistrations_RoundTripSeparately()
    {
        var saved = await _service.SaveAsync(new SavePayrollCompanyInfoDto
        {
            EmployerTin = "E1234567890",
            PerkesoEmployerCode = "A9876543210",
            EpfEmployerNo = "7654321",
            HrdfEmployerNo = "H55555",
            ZakatNumber = "Z11111",
        });

        Assert.Equal("E1234567890", saved.EmployerTin);
        Assert.Equal("A9876543210", saved.PerkesoEmployerCode);
        Assert.Equal("7654321", saved.EpfEmployerNo);
        Assert.Equal("H55555", saved.HrdfEmployerNo);
        Assert.Equal("Z11111", saved.ZakatNumber);
    }

    // Clearing a field must actually clear it — a save is a replace, not a
    // merge, or an admin could never remove a stale tax agent.
    [Fact]
    public async Task ClearingAFieldRemovesIt()
    {
        await _service.SaveAsync(new SavePayrollCompanyInfoDto { TaxAgentName = "Old Agent" });
        var cleared = await _service.SaveAsync(new SavePayrollCompanyInfoDto { TaxAgentName = null });

        Assert.Null(cleared.TaxAgentName);
    }

    [Fact]
    public async Task TheDeclarantRoundTrips()
    {
        var saved = await _service.SaveAsync(new SavePayrollCompanyInfoDto
        {
            DeclarantName = "Tan Ah Kow",
            DeclarantIdType = IdType.NRIC,
            DeclarantIdNumber = "900101-14-5555",
            DeclarantPosition = "Finance Director",
        });

        Assert.Equal("Tan Ah Kow", saved.DeclarantName);
        Assert.Equal(IdType.NRIC, saved.DeclarantIdType);
        Assert.Equal("Finance Director", saved.DeclarantPosition);
    }

    // ─── Audit ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EverySave_IsAudited()
    {
        await _service.SaveAsync(Save());
        await _service.SaveAsync(Save(employerName: "Renamed"));

        Assert.Equal(2, _audit.Written.Count(e => e.Action == "payroll.company-info.update"));
    }

    // ─── Tenant isolation ───────────────────────────────────────────────

    [Fact]
    public async Task AnotherOrgSeesItsOwnProfile_NotOurs()
    {
        await _service.SaveAsync(Save());

        _currentUser.OrganizationId = "org-2";
        Assert.False((await _service.GetAsync()).IsConfigured);

        _currentUser.OrganizationId = "org-1";
        Assert.Equal("Altomate Sdn Bhd", (await _service.GetAsync()).EmployerName);
    }
}
