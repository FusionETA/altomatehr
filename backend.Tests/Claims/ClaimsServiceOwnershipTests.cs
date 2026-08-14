using AltomateHR.Api.Modules.Claims.Dtos;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Claims;
using static AltomateHR.Api.Tests.Claims.ClaimsTestFactory;

namespace AltomateHR.Api.Tests.Claims;

public class ClaimsServiceOwnershipTests
{
    [Fact]
    public async Task CreateAsync_StoresEmployeeIdFromAuthenticatedUserId()
    {
        var service = CreateService([]);

        var claim = await service.CreateAsync(CreateClaimDto(), employeeId: "usr-emp");

        Assert.Equal("usr-emp", claim.EmployeeId);
    }

    [Fact]
    public async Task GetMineAsync_ReturnsOnlyOwnClaims()
    {
        var service = CreateService([
            NewClaim("own", "usr-emp"),
            NewClaim("other", "usr-other"),
        ]);

        var claims = await service.GetMineAsync("usr-emp");

        Assert.Collection(claims, claim => Assert.Equal("own", claim.Id));
    }

    [Fact]
    public async Task GetTeamAsync_ReturnsOnlyCurrentStepApproverClaims()
    {
        var service = CreateService(
            [NewClaim("own", "usr-emp"), NewClaim("other", "usr-other")],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        var claims = await service.GetTeamAsync("usr-super");

        Assert.Collection(claims, claim => Assert.Equal("own", claim.Id));
    }

    [Fact]
    public async Task UpdateAsync_DoesNotAllowEmployeeToUpdateAnotherUsersClaim()
    {
        var service = CreateService([NewClaim("other", "usr-other")]);

        var updated = await service.UpdateAsync("other", CreateClaimDto(), "usr-emp", isAdmin: false);

        Assert.Null(updated);
    }

    [Fact]
    public async Task UpdateAsync_PreservesClaimOwner()
    {
        var claim = NewClaim("own", "usr-emp");
        var service = CreateService([claim]);

        var updated = await service.UpdateAsync("own", CreateClaimDto(title: "Updated"), "usr-emp", isAdmin: false);

        Assert.NotNull(updated);
        Assert.Equal("usr-emp", claim.EmployeeId);
        Assert.Equal("Updated", claim.Title);
    }

    [Theory]
    [InlineData(ClaimStatus.APPROVED)]
    [InlineData(ClaimStatus.REVIEWED)]
    [InlineData(ClaimStatus.REJECTED)]
    public async Task UpdateAsync_DoesNotAllowReviewedClaimsToBeEdited(ClaimStatus status)
    {
        var claim = NewClaim("own", "usr-emp", status);
        var service = CreateService([claim]);

        var error = await Assert.ThrowsAsync<ClaimValidationException>(() =>
            service.UpdateAsync("own", CreateClaimDto(title: "Updated"), "usr-emp", isAdmin: false));

        Assert.Equal("This claim has already been reviewed and can no longer be edited.", error.Message);
        Assert.Equal("Lunch", claim.Title);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotAllowClaimTypeToChange()
    {
        var claim = NewClaim("own", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([claim]);
        var dto = CreateClaimDto();
        dto.ClaimType = ClaimType.MILEAGE;

        var error = await Assert.ThrowsAsync<ClaimValidationException>(() =>
            service.UpdateAsync("own", dto, "usr-emp", isAdmin: false));

        Assert.Equal("Claim type cannot be changed after submission.", error.Message);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotAllowPaymentTypeToChange()
    {
        var claim = NewClaim("own", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([claim]);
        var dto = CreateClaimDto();
        dto.PaymentType = PaymentType.COMPANY;

        var error = await Assert.ThrowsAsync<ClaimValidationException>(() =>
            service.UpdateAsync("own", dto, "usr-emp", isAdmin: false));

        Assert.Equal("Payment source cannot be changed after submission.", error.Message);
    }

    private static CreateClaimDto CreateClaimDto(string title = "Lunch") => new()
    {
        Title = title,
        Description = "Team lunch",
        Category = ClaimCategory.MEAL,
        Amount = 12,
        Currency = "MYR",
        SpentAt = DateTime.UtcNow,
        ClaimType = ClaimType.EXPENSE,
        PaymentType = PaymentType.PERSONAL,
        ChartOfAccountId = "acct-expense",
    };
}
