using AltomateHR.Api.Modules.Claims.Entities;
using static AltomateHR.Api.Tests.Claims.ClaimsTestFactory;

namespace AltomateHR.Api.Tests.Claims;

// Bulk approval is the fastest way to move money in this app, so what it
// REFUSES matters more than what it approves.
public class ClaimsBulkApproveTests
{
    private static FakeApprovalRouter SingleApprover() =>
        new(new() { ["usr-emp"] = [["usr-approver"]] });

    [Fact]
    public async Task BulkApproveAsync_ApprovesEveryClaimTheCallerMayDecide()
    {
        var a = NewClaim("claim-a", "usr-emp", ClaimStatus.PENDING);
        var b = NewClaim("claim-b", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([a, b], router: SingleApprover());

        var result = await service.BulkApproveAsync(["claim-a", "claim-b"], "usr-approver");

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(ClaimStatus.APPROVED, a.Status);
        Assert.Equal(ClaimStatus.APPROVED, b.Status);
    }

    [Fact]
    public async Task BulkApproveAsync_RefusesOverLimitClaimsSoTheyGetReadIndividually()
    {
        var ordinary = NewClaim("ordinary", "usr-emp", ClaimStatus.PENDING);
        var overLimit = NewClaim("over-limit", "usr-emp", ClaimStatus.PENDING);
        overLimit.ExceedsLimit = true;

        var service = CreateService([ordinary, overLimit], router: SingleApprover());

        var result = await service.BulkApproveAsync(["ordinary", "over-limit"], "usr-approver");

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Equal(ClaimStatus.APPROVED, ordinary.Status);

        // The whole point: it did not ride along in the batch.
        Assert.Equal(ClaimStatus.PENDING, overLimit.Status);
        var refused = Assert.Single(result.Items, i => !i.Ok);
        Assert.Equal("over-limit", refused.Id);
        Assert.Contains("spend limit", refused.Error);
    }

    [Fact]
    public async Task BulkApproveAsync_FailsOnlyTheClaimsTheCallerCannotDecide()
    {
        var mine = NewClaim("mine", "usr-emp", ClaimStatus.PENDING);
        var someoneElses = NewClaim("theirs", "usr-other", ClaimStatus.PENDING);
        var service = CreateService([mine, someoneElses], router: SingleApprover());

        var result = await service.BulkApproveAsync(["mine", "theirs"], "usr-approver");

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(ClaimStatus.APPROVED, mine.Status);
        Assert.Equal(ClaimStatus.PENDING, someoneElses.Status);
    }

    [Fact]
    public async Task BulkApproveAsync_SkipsClaimsThatWereAlreadyDecided()
    {
        var settled = NewClaim("settled", "usr-emp", ClaimStatus.APPROVED);
        var service = CreateService([settled], router: SingleApprover());

        var result = await service.BulkApproveAsync(["settled"], "usr-approver");

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task BulkApproveAsync_CountsARepeatedIdOnce()
    {
        var claim = NewClaim("claim-a", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([claim], router: SingleApprover());

        var result = await service.BulkApproveAsync(["claim-a", "claim-a"], "usr-approver");

        Assert.Equal(1, result.Succeeded);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task BulkApproveAsync_RefusesTheWholeBatchWhenItIsTooLarge()
    {
        var claim = NewClaim("claim-a", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([claim], router: SingleApprover());

        var ids = Enumerable.Range(0, 201).Select(i => $"id-{i}").ToList();
        var result = await service.BulkApproveAsync(ids, "usr-approver");

        Assert.Equal(0, result.Succeeded);
        // Nothing was touched — an oversized batch is refused whole, not partly applied.
        Assert.Equal(ClaimStatus.PENDING, claim.Status);
    }

    [Fact]
    public async Task BulkApproveAsync_AdvancesTheChainInsteadOfApprovingOnAMultiStepChain()
    {
        var claim = NewClaim("claim-a", "usr-emp", ClaimStatus.PENDING);
        var service = CreateService([claim], router:
            new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-approver"], ["usr-finance"]] }));

        var result = await service.BulkApproveAsync(["claim-a"], "usr-approver");

        Assert.Equal(1, result.Succeeded);
        // Still pending, now waiting on the next layer.
        Assert.Equal(ClaimStatus.PENDING, claim.Status);
        Assert.Equal(1, claim.CurrentStep);
    }
}
