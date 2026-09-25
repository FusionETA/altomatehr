using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Accounts;
using AltomateHR.Api.Modules.Accounts.Dtos;
using AltomateHR.Api.Modules.Accounts.Entities;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Accounts;

// Liability accounts are imported from Xero for the payroll journal's credit
// side only. The claims chart — the claim form, the claims import/export, the
// receipt OCR — all read through this service, so this is where they are kept
// out, and where an edit is stopped from turning one into a claimable expense.
public class ChartOfAccountServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ChartOfAccountService _service;

    public ChartOfAccountServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"accounts-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, new StubCurrentUser());

        // Neither path under test touches Xero.
        _service = new ChartOfAccountService(new ChartOfAccountRepository(_db), null!, new FakeAuditService());

        _db.ChartOfAccounts.AddRange(
            new ChartOfAccount { Id = "exp", OrganizationId = "org-1", Code = "6100", Name = "Travel", Type = ChartOfAccountTypes.Expense, IsSelectable = true },
            new ChartOfAccount { Id = "bank", OrganizationId = "org-1", Code = "1000", Name = "Bank", Type = ChartOfAccountTypes.Bank },
            new ChartOfAccount { Id = "liab", OrganizationId = "org-1", Code = "825", Name = "EPF Payable", Type = ChartOfAccountTypes.Liability });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetAllAsync_LeavesLiabilitiesOutByDefault()
    {
        var ids = (await _service.GetAllAsync()).Select(a => a.Id).ToList();

        Assert.Equal(["bank", "exp"], ids.Order());
    }

    [Fact]
    public async Task GetAllAsync_IncludesLiabilitiesWhenAsked()
    {
        var ids = (await _service.GetAllAsync(includeLiabilities: true)).Select(a => a.Id).ToList();

        Assert.Contains("liab", ids);
    }

    // The save DTO only knows EXPENSE and BANK, so an edit would otherwise
    // re-type a payroll payable as a claimable expense account.
    [Fact]
    public async Task UpdateAsync_KeepsALiabilityALiabilityAndUnselectable()
    {
        var updated = await _service.UpdateAsync("liab", new SaveChartOfAccountDto
        {
            Code = "825",
            Name = "EPF Payable",
            Type = ChartOfAccountTypes.Expense,
            IsSelectable = true,
        });

        Assert.Equal(ChartOfAccountTypes.Liability, updated!.Type);
        Assert.False(updated.IsSelectable);
    }
}
