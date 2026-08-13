using AltomateHR.Api.Modules.Claims.Dtos;
using AltomateHR.Api.Modules.Claims.Entities;
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
    public async Task GetTeamAsync_ReturnsAllClaimsForOrgApprover()
    {
        var service = CreateService([
            NewClaim("own", "usr-emp"),
            NewClaim("other", "usr-other"),
        ]);

        var claims = await service.GetTeamAsync("usr-admin", "Admin");

        Assert.Equal(2, claims.Count());
    }

    [Fact]
    public async Task GetTeamAsync_ReturnsOnlyDirectReportsForSupervisor()
    {
        var supervision = new FakeSupervisionService(
            supervisorOf: new() { ["usr-emp"] = "usr-super" });
        var service = CreateService(
            [NewClaim("own", "usr-emp"), NewClaim("other", "usr-other")],
            supervision);

        var claims = await service.GetTeamAsync("usr-super", "Supervisor");

        Assert.Collection(claims, claim => Assert.Equal("own", claim.Id));
    }

    [Fact]
    public async Task UpdateAsync_DoesNotAllowEmployeeToUpdateAnotherUsersClaim()
    {
        var service = CreateService([NewClaim("other", "usr-other")]);

        var updated = await service.UpdateAsync("other", CreateClaimDto(), "usr-emp", isAdmin: false);

        Assert.False(updated);
    }

    [Fact]
    public async Task UpdateAsync_PreservesClaimOwner()
    {
        var claim = NewClaim("own", "usr-emp");
        var service = CreateService([claim]);

        var updated = await service.UpdateAsync("own", CreateClaimDto(title: "Updated"), "usr-emp", isAdmin: false);

        Assert.True(updated);
        Assert.Equal("usr-emp", claim.EmployeeId);
        Assert.Equal("Updated", claim.Title);
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
    };
}
