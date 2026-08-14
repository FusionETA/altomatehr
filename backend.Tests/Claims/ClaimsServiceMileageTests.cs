using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Accounts.Dtos;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Claims.Dtos;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Organizations.Dtos;
using static AltomateHR.Api.Tests.Claims.ClaimsTestFactory;

namespace AltomateHR.Api.Tests.Claims;

public class ClaimsServiceMileageTests
{
    [Fact]
    public async Task CreateAsync_ComputesMileageAmountAndSnapshotsRate()
    {
        var service = CreateService([]);

        var claim = await service.CreateAsync(MileageDto(), "usr-emp");

        Assert.Equal(ClaimType.MILEAGE, claim.ClaimType);
        Assert.Equal(8.00m, claim.Amount);
        Assert.Equal(10.00m, claim.Distance);
        Assert.Equal(0.8000m, claim.MileageRateUsed);
        Assert.Equal(MileageUnit.KM, claim.MileageUnitUsed);
        Assert.Equal("Office", claim.MileageOriginAddress);
        Assert.Equal("Client site", claim.MileageDestinationAddress);
    }

    [Fact]
    public async Task CreateAsync_UsesOrganizationMileageRateWhenAccountHasNoOverride()
    {
        var service = CreateService(
            [],
            accounts: new FakeChartOfAccountService(new ChartOfAccountDto
            {
                Id = "acct-mileage",
                Code = "6200",
                Name = "Mileage Claims",
                Type = "EXPENSE",
                AllowMileageClaim = true,
            }));

        var claim = await service.CreateAsync(MileageDto(), "usr-emp");

        Assert.Equal(6.00m, claim.Amount);
        Assert.Equal(0.6000m, claim.MileageRateUsed);
    }

    [Fact]
    public async Task CreateAsync_RejectsMileageWhenAccountIsNotMileageEnabled()
    {
        var service = CreateService([]);

        var ex = await Assert.ThrowsAsync<ClaimValidationException>(
            () => service.CreateAsync(MileageDto(chartOfAccountId: "acct-expense"), "usr-emp"));

        Assert.Equal("Select an account configured for mileage claims.", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_RejectsMileageWhenNoRateIsConfigured()
    {
        var service = CreateService(
            [],
            accounts: new FakeChartOfAccountService(new ChartOfAccountDto
            {
                Id = "acct-mileage",
                Code = "6200",
                Name = "Mileage Claims",
                Type = "EXPENSE",
                AllowMileageClaim = true,
            }),
            organizations: new FakeOrganizationService(new OrganizationDto
            {
                Id = "org-demo",
                Name = "AltomateHR",
                DefaultCurrency = "MYR",
                DefaultMileageRate = 0,
                MileageUnit = MileageUnit.KM,
                GeofenceRadiusMeters = 200,
            }));

        var ex = await Assert.ThrowsAsync<ClaimValidationException>(
            () => service.CreateAsync(MileageDto(), "usr-emp"));

        Assert.Equal(
            "Mileage rate is not configured. Ask your admin to set the rate in Mileage claim settings.",
            ex.Message);
    }

    [Fact]
    public async Task CreateAsync_RejectsCompanyMoneyWithoutBankAccount()
    {
        var service = CreateService([]);
        var dto = ExpenseDto();
        dto.PaymentType = PaymentType.COMPANY;
        dto.SpendingAt = "Petronas";

        var ex = await Assert.ThrowsAsync<ClaimValidationException>(
            () => service.CreateAsync(dto, "usr-emp"));

        Assert.Equal("Select the company bank account that paid for this claim.", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_StoresCompanyMoneyFields()
    {
        var service = CreateService([]);
        var dto = ExpenseDto();
        dto.PaymentType = PaymentType.COMPANY;
        dto.PayViaAccountId = "acct-bank";
        dto.SpendingAt = "Petronas";
        dto.SpendingWith = "Client A";

        var claim = await service.CreateAsync(dto, "usr-emp");

        Assert.Equal(PaymentType.COMPANY, claim.PaymentType);
        Assert.Equal("acct-bank", claim.PayViaAccountId);
        Assert.Equal("Petronas", claim.SpendingAt);
        Assert.Equal("Client A", claim.SpendingWith);
    }

    private static CreateClaimDto ExpenseDto() => new()
    {
        Title = "Fuel",
        Description = "Project travel",
        Category = ClaimCategory.TRANSPORT,
        Amount = 25,
        Currency = "MYR",
        SpentAt = DateTime.UtcNow,
        ClaimType = ClaimType.EXPENSE,
        PaymentType = PaymentType.PERSONAL,
        ChartOfAccountId = "acct-expense",
    };

    private static CreateClaimDto MileageDto(string chartOfAccountId = "acct-mileage") => new()
    {
        Title = "Client visit",
        Description = "Travel to client site",
        Category = ClaimCategory.TRANSPORT,
        Currency = "MYR",
        SpentAt = DateTime.UtcNow,
        ClaimType = ClaimType.MILEAGE,
        PaymentType = PaymentType.PERSONAL,
        ChartOfAccountId = chartOfAccountId,
        Distance = 10,
        MileageOriginAddress = "Office",
        MileageDestinationAddress = "Client site",
    };
}
