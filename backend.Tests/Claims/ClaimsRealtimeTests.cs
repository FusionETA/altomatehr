using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Realtime.Dtos;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Tests.Support;

namespace AltomateHR.Api.Tests.Claims;

// Who gets nudged when a claim moves. The rule under test is that targets come
// from the claim's state AFTER the transition — that's what makes a multi-step
// chain hand off to the next reviewer instead of re-pinging the last one.
public class ClaimsRealtimeTests
{
    // usr-emp files; usr-super reviews step 1, usr-admin step 2.
    private static Dictionary<string, List<List<string>>> TwoStepChain() =>
        new() { ["usr-emp"] = [["usr-super"], ["usr-admin"]] };

    private static Claim Pending(int step = 0)
    {
        var claim = ClaimsTestFactory.NewClaim("clm-1", "usr-emp");
        claim.OrganizationId = "org-demo";
        claim.CurrentStep = step;
        return claim;
    }

    [Fact]
    public async Task ApprovingAMiddleStepNotifiesTheApplicantAndTheNextApprover()
    {
        var realtime = new FakeRealtimeService();
        var service = ClaimsTestFactory.CreateService(
            [Pending()],
            router: new FakeApprovalRouter(TwoStepChain()),
            realtime: realtime);

        var result = await service.ApproveAsync("clm-1", "usr-super");

        Assert.True(result.Transitioned);
        var published = Assert.Single(realtime.Published);
        Assert.Equal("org-demo", published.OrganizationId);
        Assert.Equal(RealtimeScope.CLAIMS, published.Event.Scope);
        Assert.Equal(RealtimeAction.APPROVED, published.Event.Action);
        Assert.Equal("clm-1", published.Event.EntityId);
        Assert.Equal(["usr-emp", "usr-admin"], published.UserIds);
    }

    [Fact]
    public async Task ApprovingTheFinalStepNotifiesOnlyTheApplicant()
    {
        var realtime = new FakeRealtimeService();
        var service = ClaimsTestFactory.CreateService(
            [Pending(step: 1)],
            router: new FakeApprovalRouter(TwoStepChain()),
            realtime: realtime);

        await service.ApproveAsync("clm-1", "usr-admin");

        // APPROVED is terminal, so there is no "next approver" to tell.
        Assert.Equal(["usr-emp"], Assert.Single(realtime.Published).UserIds);
    }

    // Rejection is terminal, so the PENDING check can't discover the approvers —
    // but a peer reviewer at the same step still needs the row to leave their
    // queue. Hence notifyApprovers on the reject path.
    [Fact]
    public async Task RejectingNotifiesTheApplicantAndTheReviewersItLeaves()
    {
        var realtime = new FakeRealtimeService();
        var service = ClaimsTestFactory.CreateService(
            [Pending()],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super", "usr-peer"]] }),
            realtime: realtime);

        await service.RejectAsync("clm-1", "usr-super", "Not covered.");

        var published = Assert.Single(realtime.Published);
        Assert.Equal(RealtimeAction.REJECTED, published.Event.Action);
        Assert.Equal(["usr-emp", "usr-super", "usr-peer"], published.UserIds);
    }

    [Fact]
    public async Task ARefusedTransitionPublishesNothing()
    {
        var realtime = new FakeRealtimeService();
        var alreadyApproved = ClaimsTestFactory.NewClaim("clm-1", "usr-emp", ClaimStatus.APPROVED);
        alreadyApproved.OrganizationId = "org-demo";

        var service = ClaimsTestFactory.CreateService(
            [alreadyApproved],
            router: new FakeApprovalRouter(TwoStepChain()),
            realtime: realtime);

        var result = await service.ApproveAsync("clm-1", "usr-super");

        Assert.False(result.Transitioned);
        Assert.Empty(realtime.Published);
    }

    [Fact]
    public async Task DeletingTellsTheClaimantAndTheReviewersItWasQueuedWith()
    {
        var realtime = new FakeRealtimeService();
        var service = ClaimsTestFactory.CreateService(
            [Pending()],
            router: new FakeApprovalRouter(TwoStepChain()),
            realtime: realtime);

        Assert.True(await service.DeleteAsync("clm-1"));

        var published = Assert.Single(realtime.Published);
        Assert.Equal(RealtimeAction.DELETED, published.Event.Action);
        Assert.Contains("usr-emp", published.UserIds);
        Assert.Contains("usr-super", published.UserIds);
    }

    [Fact]
    public async Task DeletingSomethingThatIsNotTherePublishesNothing()
    {
        var realtime = new FakeRealtimeService();
        var service = ClaimsTestFactory.CreateService([], realtime: realtime);

        Assert.False(await service.DeleteAsync("clm-missing"));
        Assert.Empty(realtime.Published);
    }
}
