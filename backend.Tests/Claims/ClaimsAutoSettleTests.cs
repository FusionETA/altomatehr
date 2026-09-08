using AltomateHR.Api.Modules.Accounts.Dtos;
using AltomateHR.Api.Modules.Claims.Dtos;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Organizations.Dtos;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Tests.Support;
using static AltomateHR.Api.Tests.Claims.ClaimsTestFactory;

namespace AltomateHR.Api.Tests.Claims;

// Approval is the trigger to pay: clearing the last step settles the claim with
// no further human action. The guards are what matter here — an accounting
// failure must never cost the approver their decision.
public class ClaimsAutoSettleTests
{
    private static readonly EmployeeIdentity Ahmad =
        new("usr-emp", "ahmad@x.com", "Ahmad Ali", "Employee");

    private static FakeApprovalRouter SingleApprover() =>
        new(new() { ["usr-emp"] = [["usr-approver"]] });

    private static FakeApprovalRouter TwoStepChain() =>
        new(new() { ["usr-emp"] = [["usr-lead"], ["usr-manager"]] });

    private static CreateClaimDto SimpleDto() => new()
    {
        Title = "Taxi to site",
        Description = "Client meeting",
        Category = ClaimCategory.TRANSPORT,
        Amount = 25.00m,
        SpentAt = DateTime.UtcNow,
        ClaimType = ClaimType.EXPENSE,
        PaymentType = PaymentType.PERSONAL,
        ChartOfAccountId = "acct-expense",
    };

    [Fact]
    public async Task ApprovingTheFinalStep_BillsTheClaimWithoutAnyFurtherAction()
    {
        var claim = NewClaim("claim-1", "usr-emp");
        var xero = new FakeXeroBillService();
        var service = CreateService(
            [claim], router: SingleApprover(), employees: new FakeEmployeeDirectory(Ahmad), xero: xero);

        await service.ApproveAsync("claim-1", "usr-approver");

        Assert.Equal(ClaimStatus.APPROVED, claim.Status);
        Assert.Equal(XeroSyncStatus.SYNCED, claim.XeroSyncStatus);
        Assert.Equal(claim.ClaimNumber, Assert.Single(xero.Created).Reference);
    }

    [Fact]
    public async Task ApprovingAMiddleStep_BillsNothing()
    {
        // Still PENDING with a layer to go — nobody has agreed to pay it yet.
        var claim = NewClaim("claim-1", "usr-emp");
        var xero = new FakeXeroBillService();
        var service = CreateService([claim], router: TwoStepChain(), xero: xero);

        await service.ApproveAsync("claim-1", "usr-lead");

        Assert.Equal(ClaimStatus.PENDING, claim.Status);
        Assert.Empty(xero.Created);
    }

    [Fact]
    public async Task AClaimRoutedToPayroll_IsNeverPushedToXero()
    {
        var claim = NewClaim("claim-1", "usr-emp");
        claim.Settlement = ClaimSettlement.PAYROLL;
        var xero = new FakeXeroBillService();
        var service = CreateService([claim], router: SingleApprover(), xero: xero);

        await service.ApproveAsync("claim-1", "usr-approver");

        Assert.Equal(ClaimStatus.APPROVED, claim.Status);
        Assert.Empty(xero.Created);
        // Not an error either — the payroll export is what collects it.
        Assert.Equal(XeroSyncStatus.NOT_SYNCED, claim.XeroSyncStatus);
    }

    [Fact]
    public async Task WhenXeroIsNotConnected_TheClaimIsStillApprovedAndNotMarkedFailed()
    {
        var claim = NewClaim("claim-1", "usr-emp");
        var service = CreateService(
            [claim], router: SingleApprover(), xero: new FakeXeroBillService(connected: false));

        await service.ApproveAsync("claim-1", "usr-approver");

        Assert.Equal(ClaimStatus.APPROVED, claim.Status);
        // NOT_SYNCED, not ERROR: "Xero was never set up" is not a failed push,
        // and marking it one would bury the real failures.
        Assert.Equal(XeroSyncStatus.NOT_SYNCED, claim.XeroSyncStatus);
        Assert.Null(claim.XeroSyncError);
    }

    [Fact]
    public async Task WhenXeroRefusesTheBill_TheApprovalStillStands()
    {
        var claim = NewClaim("claim-1", "usr-emp");
        var service = CreateService(
            [claim],
            router: SingleApprover(),
            employees: new FakeEmployeeDirectory(Ahmad),
            xero: new FakeXeroBillService(failWith: "Organisation is not subscribed to currency USD"));

        var result = await service.ApproveAsync("claim-1", "usr-approver");

        // The whole point: the supervisor's decision is not lost to an
        // accounting problem. It is approved, and the failure is recorded on the
        // claim for an admin to retry.
        Assert.True(result.Transitioned);
        Assert.Equal(ClaimStatus.APPROVED, claim.Status);
        Assert.Equal(XeroSyncStatus.ERROR, claim.XeroSyncStatus);
        Assert.Contains("currency USD", claim.XeroSyncError);
    }

    [Fact]
    public async Task BulkApproving_SettlesEveryClaimThatClearedItsChain()
    {
        var first = NewClaim("claim-1", "usr-emp");
        var second = NewClaim("claim-2", "usr-emp");
        var xero = new FakeXeroBillService();
        var service = CreateService(
            [first, second],
            router: SingleApprover(),
            employees: new FakeEmployeeDirectory(Ahmad),
            xero: xero);

        await service.BulkApproveAsync(["claim-1", "claim-2"], "usr-approver");

        Assert.Equal(ClaimStatus.APPROVED, first.Status);
        Assert.Equal(ClaimStatus.APPROVED, second.Status);
        Assert.Equal(2, xero.Created.Count);
    }

    // A claim can only become a Xero bill if its account exists in Xero, so an
    // auto-settle test that goes all the way through needs a synced account.
    private static FakeChartOfAccountService XeroSyncedAccounts() =>
        new(new ChartOfAccountDto
        {
            Id = "acct-expense",
            Code = "6100",
            Name = "Travel Expenses",
            Type = "EXPENSE",
            IsSelectable = true,
            XeroAccountId = "xero-acct-1",
        });

    [Fact]
    public async Task AClaimApprovedOnSubmission_IsSettledToo()
    {
        // Nobody above the claimant → approved at submit. It would be the one
        // approved claim still waiting on a manual push.
        var xero = new FakeXeroBillService();
        var service = CreateService(
            [],
            router: new FakeApprovalRouter(),
            accounts: XeroSyncedAccounts(),
            employees: new FakeEmployeeDirectory(Ahmad),
            xero: xero);

        var claim = await service.CreateAsync(SimpleDto(), "usr-emp");

        Assert.Equal(ClaimStatus.APPROVED, claim.Status);
        Assert.Single(xero.Created);
    }

    [Fact]
    public async Task AnAccountThatIsNotInXero_FailsTheSyncButKeepsTheApproval()
    {
        // The default account has no XeroAccountId — its code would be rejected
        // by Xero, so the sync refuses BEFORE sending anything. The approval and
        // the reason both survive, which is what the Retry acts on.
        var xero = new FakeXeroBillService();
        var service = CreateService(
            [],
            router: new FakeApprovalRouter(),
            employees: new FakeEmployeeDirectory(Ahmad),
            xero: xero);

        var claim = await service.CreateAsync(SimpleDto(), "usr-emp");

        Assert.Equal(ClaimStatus.APPROVED, claim.Status);
        Assert.Empty(xero.Created);
        Assert.Equal(XeroSyncStatus.ERROR, claim.XeroSyncStatus);
        Assert.Contains("doesn't exist in Xero", claim.XeroSyncError);
    }

    [Fact]
    public async Task AutoSettleUsesTheOrgsConfiguredXeroStage()
    {
        // Draft parks the bill in the accountant's queue instead of making it a
        // live payable. The setting has to reach the automatic push, or picking
        // Draft would be silently ignored for every approval.
        var xero = new FakeXeroBillService();
        var service = CreateService(
            [],
            router: new FakeApprovalRouter(),
            accounts: XeroSyncedAccounts(),
            organizations: OrgWithStage("Draft"),
            employees: new FakeEmployeeDirectory(Ahmad),
            xero: xero);

        await service.CreateAsync(SimpleDto(), "usr-emp");

        Assert.Equal(XeroBillStatus.Draft, Assert.Single(xero.Created).Status);
    }

    [Fact]
    public async Task AnExplicitStageOnThePushOverridesTheSetting()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.APPROVED);
        var xero = new FakeXeroBillService();
        var service = CreateService(
            [claim],
            organizations: OrgWithStage("Draft"),
            employees: new FakeEmployeeDirectory(Ahmad),
            xero: xero);

        await service.SyncToXeroAsync("claim-1", XeroBillStatus.AwaitingPayment);

        Assert.Equal(XeroBillStatus.AwaitingPayment, Assert.Single(xero.Created).Status);
    }

    [Fact]
    public async Task APushThatNamesNoStage_FallsBackToTheSetting()
    {
        // The regression this guards: SyncClaimToXeroDto.Status used to default
        // to AwaitingPayment, so a request with an empty body was
        // indistinguishable from one asking for a live payable — and quietly
        // overrode the org's setting.
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.APPROVED);
        var xero = new FakeXeroBillService();
        var service = CreateService(
            [claim],
            organizations: OrgWithStage("Draft"),
            employees: new FakeEmployeeDirectory(Ahmad),
            xero: xero);

        await service.SyncToXeroAsync("claim-1");

        Assert.Equal(XeroBillStatus.Draft, Assert.Single(xero.Created).Status);
    }

    private static FakeOrganizationService OrgWithStage(string stage) =>
        new(new OrganizationDto
        {
            Id = "org-demo",
            Name = "AltomateHR",
            DefaultCurrency = "MYR",
            ClaimSettlementRoute = "XERO_BILL",
            XeroBillStage = stage,
        });

    [Fact]
    public async Task AClaimIsDenominatedInTheOrgsCurrency_NotTheClients()
    {
        // The bug this replaced: Claim.Currency and CreateClaimDto.Currency both
        // defaulted to "USD" while orgs default to MYR, so a claim created
        // without an explicit currency was a USD claim — which Xero refused.
        var service = CreateService(
            [],
            organizations: new FakeOrganizationService(new OrganizationDto
            {
                Id = "org-demo", Name = "AltomateHR", DefaultCurrency = "MYR",
            }));

        var claim = await service.CreateAsync(SimpleDto(), "usr-emp");

        Assert.Equal("MYR", claim.Currency);
    }
}
