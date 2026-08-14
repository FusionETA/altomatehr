using AltomateHR.Api.Modules.Claims.Entities;
using static AltomateHR.Api.Tests.Claims.ClaimsTestFactory;

namespace AltomateHR.Api.Tests.Claims;

public class ClaimsServiceApprovalTests
{
    [Fact]
    public async Task ApproveAsync_TransitionsPendingClaimToApproved()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([claim]);

        var result = await service.ApproveAsync("claim-1", "usr-admin", "Admin");

        Assert.True(result.Found);
        Assert.True(result.Transitioned);
        Assert.Equal(ClaimStatus.APPROVED, claim.Status);
        Assert.Null(claim.ReviewNotes);
    }

    [Fact]
    public async Task RejectAsync_TransitionsPendingClaimToRejectedAndStoresReviewNotes()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([claim]);

        var result = await service.RejectAsync("claim-1", "usr-admin", "Admin", "Receipt is unreadable.");

        Assert.True(result.Found);
        Assert.True(result.Transitioned);
        Assert.Equal(ClaimStatus.REJECTED, claim.Status);
        Assert.Equal("Receipt is unreadable.", claim.ReviewNotes);
    }

    [Fact]
    public async Task ApproveAsync_ReturnsNotFoundForMissingClaim()
    {
        var service = CreateService([]);

        var result = await service.ApproveAsync("missing", "usr-admin", "Admin");

        Assert.False(result.Found);
        Assert.False(result.Transitioned);
        Assert.Null(result.Claim);
    }

    [Theory]
    [InlineData(ClaimStatus.APPROVED)]
    [InlineData(ClaimStatus.REJECTED)]
    [InlineData(ClaimStatus.REVIEWED)]
    [InlineData(ClaimStatus.SUBMITTED)]
    public async Task ApproveAsync_DoesNotTransitionNonPendingClaim(ClaimStatus currentStatus)
    {
        var claim = NewClaim("claim-1", "usr-emp", currentStatus);
        var service = CreateService([claim]);

        var result = await service.ApproveAsync("claim-1", "usr-admin", "Admin");

        Assert.True(result.Found);
        Assert.False(result.Transitioned);
        Assert.Equal(currentStatus, claim.Status);
        Assert.Equal("Only pending claims can be approved or rejected.", result.ErrorMessage);
    }

    [Fact]
    public async Task ApproveAsync_AllowsTheCurrentStepApprover()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([claim],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        var result = await service.ApproveAsync("claim-1", "usr-super", "Supervisor");

        Assert.True(result.Transitioned);
        Assert.Equal(ClaimStatus.APPROVED, claim.Status);
    }

    [Fact]
    public async Task ApproveAsync_HidesClaimFromANonCurrentApprover()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([claim],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        // Not the current-step approver → treated as not-found.
        var result = await service.ApproveAsync("claim-1", "usr-other-super", "Supervisor");

        Assert.False(result.Found);
        Assert.Equal(ClaimStatus.PENDING, claim.Status);
    }

    [Fact]
    public async Task ApproveAsync_AdvancesThroughAMultiStepChain()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([claim],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"], ["usr-mgr"]] }));

        var first = await service.ApproveAsync("claim-1", "usr-super", "Supervisor");   // step 0 → advance
        Assert.True(first.Transitioned);
        Assert.Equal(ClaimStatus.PENDING, claim.Status);
        Assert.Equal(1, claim.CurrentStep);

        Assert.False((await service.ApproveAsync("claim-1", "usr-super", "Supervisor")).Found);

        var second = await service.ApproveAsync("claim-1", "usr-mgr", "Supervisor");    // step 1 → final
        Assert.True(second.Transitioned);
        Assert.Equal(ClaimStatus.APPROVED, claim.Status);
    }
}
