using AltomateHR.Api.Modules.Claims.Entities;
using static AltomateHR.Api.Tests.Claims.ClaimsTestFactory;

namespace AltomateHR.Api.Tests.Claims;

// The admin claims table offers Approve and Reject on rows the viewer can act
// on. An admin can never be such a viewer — the router leaves administrative
// seats out of every chain (see ApprovalRoutingTests) — so those controls are
// unreachable for the only role that sees that table.
//
// These tests exist because the two halves were built from opposite
// assumptions and both landed: the admin surface was written believing "an
// Admin is a layer in the chain like anyone else", and the router was written
// believing an admin approves nothing. The router wins. Without this file the
// contradiction is invisible — nothing fails, an admin just clicks a button
// that was never going to work.
public class AdminNeverApprovesTests
{
    [Fact]
    public async Task AnAdminViewingTheOrgList_IsNeverOfferedTheDecision()
    {
        // usr-emp's claim routes to usr-approver. The admin is not in the chain
        // — which is what the real router guarantees for any administrative
        // seat, whatever team or layer they sit in.
        var claim = NewClaim("c1", "usr-emp");
        var service = CreateService([claim], router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-approver"]] }));

        var rows = await service.GetAllForOrgAsync("usr-admin");

        Assert.False(rows.Single().CanAct);
        // And it says who it IS waiting on, rather than implying nobody.
        Assert.NotEmpty(rows.Single().AwaitingApprovers);
    }

    [Fact]
    public async Task AnAdminApproving_IsRefused()
    {
        // Belt as well as braces: the button is unreachable, and the call it
        // would have made is refused anyway. Hiding a control is a UI decision;
        // this is the rule.
        var claim = NewClaim("c1", "usr-emp");
        var service = CreateService([claim], router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-approver"]] }));

        var result = await service.ApproveAsync("c1", "usr-admin");

        Assert.False(result.Found);
        Assert.Equal(ClaimStatus.PENDING, claim.Status);
    }

    [Fact]
    public async Task AStepWithNoApproverAtAll_SaysNobodyCanApprove()
    {
        // The state that made the admin surface look necessary: a claim sitting
        // at a step the hierarchy no longer has an approver for. The answer is
        // an empty approver list — which the table renders as "Nobody can
        // approve" — not an admin stepping in to decide it.
        var claim = NewClaim("c1", "usr-emp");
        var service = CreateService([claim], router: new FakeApprovalRouter(new() { ["usr-emp"] = [[]] }));

        var rows = await service.GetAllForOrgAsync("usr-admin");

        Assert.False(rows.Single().CanAct);
        Assert.Empty(rows.Single().AwaitingApprovers);
    }
}
