using AltomateHR.Api.Modules.Claims.Entities;
using static AltomateHR.Api.Tests.Claims.ClaimsTestFactory;

namespace AltomateHR.Api.Tests.Claims;

public class ClaimsServiceApprovalTests
{
    // Single-step chain: usr-emp's claim is approved by usr-approver.
    private static FakeApprovalRouter SingleApprover() =>
        new(new() { ["usr-emp"] = [["usr-approver"]] });

    [Fact]
    public async Task ApproveAsync_TransitionsPendingClaimToApproved()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([claim], router: SingleApprover());

        var result = await service.ApproveAsync("claim-1", "usr-approver");

        Assert.True(result.Found);
        Assert.True(result.Transitioned);
        Assert.Equal(ClaimStatus.APPROVED, claim.Status);
        Assert.Null(claim.ReviewNotes);
    }

    [Fact]
    public async Task RejectAsync_TransitionsPendingClaimToRejectedAndStoresReviewNotes()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([claim], router: SingleApprover());

        var result = await service.RejectAsync("claim-1", "usr-approver", "  Receipt is unreadable.  ");

        Assert.True(result.Found);
        Assert.True(result.Transitioned);
        Assert.Equal(ClaimStatus.REJECTED, claim.Status);
        Assert.Equal("Receipt is unreadable.", claim.ReviewNotes);
    }

    [Fact]
    public async Task RejectAsync_RequiresReviewNotes()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([claim], router: SingleApprover());

        var result = await service.RejectAsync("claim-1", "usr-approver", "   ");

        Assert.True(result.Found);
        Assert.False(result.Transitioned);
        Assert.Equal(ClaimStatus.PENDING, claim.Status);
        Assert.Equal("Enter a rejection remark before rejecting this claim.", result.ErrorMessage);
        Assert.Null(claim.ReviewNotes);
    }

    [Fact]
    public async Task ApproveAsync_ReturnsNotFoundForMissingClaim()
    {
        var service = CreateService([], router: SingleApprover());

        var result = await service.ApproveAsync("missing", "usr-approver");

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
        var service = CreateService([claim], router: SingleApprover());

        var result = await service.ApproveAsync("claim-1", "usr-approver");

        Assert.True(result.Found);
        Assert.False(result.Transitioned);
        Assert.Equal(currentStatus, claim.Status);
        Assert.Equal("Only pending claims can be approved or rejected.", result.ErrorMessage);
    }

    [Fact]
    public async Task ApproveAsync_HidesClaimFromANonCurrentApprover()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([claim], router: SingleApprover());

        // Anyone who isn't the current-step approver — including an admin — is
        // treated as not-found. Approval is by team seat, not role.
        var result = await service.ApproveAsync("claim-1", "usr-admin");

        Assert.False(result.Found);
        Assert.Equal(ClaimStatus.PENDING, claim.Status);
    }

    [Fact]
    public async Task ApproveAsync_AdvancesThroughAMultiStepChain()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([claim],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"], ["usr-mgr"]] }));

        var first = await service.ApproveAsync("claim-1", "usr-super");   // step 0 → advance
        Assert.True(first.Transitioned);
        Assert.Equal(ClaimStatus.PENDING, claim.Status);
        Assert.Equal(1, claim.CurrentStep);

        Assert.False((await service.ApproveAsync("claim-1", "usr-super")).Found);   // no longer current

        var second = await service.ApproveAsync("claim-1", "usr-mgr");    // step 1 → final
        Assert.True(second.Transitioned);
        Assert.Equal(ClaimStatus.APPROVED, claim.Status);
    }
}
