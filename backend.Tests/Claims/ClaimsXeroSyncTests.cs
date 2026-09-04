using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Tests.Support;
using static AltomateHR.Api.Tests.Claims.ClaimsTestFactory;

namespace AltomateHR.Api.Tests.Claims;

// Syncing writes into someone else's accounting system, so the guards are the
// part worth testing: never twice, never before approval, and never silently.
public class ClaimsXeroSyncTests
{
    private static readonly EmployeeIdentity Ahmad =
        new("usr-emp", "ahmad@x.com", "Ahmad Ali", "Employee");

    [Fact]
    public async Task SyncToXeroAsync_BillsAnApprovedClaimAndRecordsTheBillId()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.APPROVED);
        claim.Amount = 245.50m;
        claim.Title = "Client dinner in KL";
        var xero = new FakeXeroBillService();

        var service = CreateService([claim], employees: new FakeEmployeeDirectory(Ahmad), xero: xero);
        var result = await service.SyncToXeroAsync("claim-1", XeroBillStatus.AwaitingPayment);

        Assert.True(result.Ok);
        Assert.False(result.AlreadySynced);
        Assert.Equal(XeroSyncStatus.SYNCED, claim.XeroSyncStatus);
        Assert.Equal("xero-bill-1", claim.XeroBillId);
        Assert.NotNull(claim.XeroSyncedAt);
        Assert.Null(claim.XeroSyncError);

        // The bill is the employee's, referenced by claim number.
        var bill = Assert.Single(xero.Created);
        Assert.Equal("Ahmad Ali", bill.ContactName);
        Assert.Equal(claim.ClaimNumber, bill.Reference);
        Assert.Equal(245.50m, Assert.Single(bill.Lines).Amount);
    }

    [Fact]
    public async Task SyncToXeroAsync_RefusesAClaimThatIsNotApproved()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.PENDING);
        var xero = new FakeXeroBillService();

        var service = CreateService([claim], xero: xero);
        var result = await service.SyncToXeroAsync("claim-1", XeroBillStatus.AwaitingPayment);

        Assert.False(result.Ok);
        // Nothing reached Xero: a bill is a liability the approvers never agreed to.
        Assert.Empty(xero.Created);
        Assert.Equal(XeroSyncStatus.NOT_SYNCED, claim.XeroSyncStatus);
    }

    [Fact]
    public async Task SyncToXeroAsync_NeverBillsTheSameClaimTwice()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.APPROVED);
        var xero = new FakeXeroBillService();
        var service = CreateService([claim], employees: new FakeEmployeeDirectory(Ahmad), xero: xero);

        await service.SyncToXeroAsync("claim-1", XeroBillStatus.AwaitingPayment);
        var second = await service.SyncToXeroAsync("claim-1", XeroBillStatus.AwaitingPayment);

        Assert.True(second.Ok);
        Assert.True(second.AlreadySynced);
        // One press or ten, one bill.
        Assert.Single(xero.Created);
    }

    [Fact]
    public async Task SyncToXeroAsync_RecordsTheFailureOnTheClaimWhenXeroRefuses()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.APPROVED);
        var xero = new FakeXeroBillService(failWith: "Xero returned 401: token expired");

        var service = CreateService([claim], employees: new FakeEmployeeDirectory(Ahmad), xero: xero);
        var result = await service.SyncToXeroAsync("claim-1", XeroBillStatus.AwaitingPayment);

        Assert.False(result.Ok);
        // The reason survives on the claim, so tomorrow's admin can see which
        // ones failed without re-pushing every claim to find out.
        Assert.Equal(XeroSyncStatus.ERROR, claim.XeroSyncStatus);
        Assert.Contains("token expired", claim.XeroSyncError);
        Assert.Null(claim.XeroBillId);
    }

    [Fact]
    public async Task SyncToXeroAsync_CanRetryAfterAFailure()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.APPROVED);
        claim.XeroSyncStatus = XeroSyncStatus.ERROR;
        claim.XeroSyncError = "Xero returned 500";

        var xero = new FakeXeroBillService();
        var service = CreateService([claim], employees: new FakeEmployeeDirectory(Ahmad), xero: xero);
        var result = await service.SyncToXeroAsync("claim-1", XeroBillStatus.AwaitingPayment);

        Assert.True(result.Ok);
        Assert.Equal(XeroSyncStatus.SYNCED, claim.XeroSyncStatus);
        // The stale error is cleared, not left to contradict the new state.
        Assert.Null(claim.XeroSyncError);
    }

    [Theory]
    [InlineData(XeroBillStatus.AwaitingPayment)]
    [InlineData(XeroBillStatus.Draft)]
    public async Task SyncToXeroAsync_PushesTheStageTheAdminChose(XeroBillStatus chosen)
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.APPROVED);
        var xero = new FakeXeroBillService();
        var service = CreateService([claim], employees: new FakeEmployeeDirectory(Ahmad), xero: xero);

        await service.SyncToXeroAsync("claim-1", chosen);

        // The choice reaches Xero rather than being decided for the admin.
        Assert.Equal(chosen, Assert.Single(xero.Created).Status);
    }

    [Fact]
    public async Task BulkSyncToXeroAsync_PushesEachClaimAndReportsPerId()
    {
        var ok = NewClaim("ok", "usr-emp", ClaimStatus.APPROVED);
        var pending = NewClaim("pending", "usr-emp", ClaimStatus.PENDING);
        var xero = new FakeXeroBillService();
        var service = CreateService([ok, pending], employees: new FakeEmployeeDirectory(Ahmad), xero: xero);

        var result = await service.BulkSyncToXeroAsync(["ok", "pending", "ghost"],
            XeroBillStatus.AwaitingPayment);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(2, result.Failed);
        // Only the approved one reached Xero; the rest failed on their own lines.
        Assert.Single(xero.Created);
        Assert.Equal(XeroSyncStatus.SYNCED, ok.XeroSyncStatus);
        Assert.Equal(XeroSyncStatus.NOT_SYNCED, pending.XeroSyncStatus);
    }

    [Fact]
    public async Task BulkSyncToXeroAsync_TreatsAnAlreadyBilledClaimAsFine()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.APPROVED);
        var xero = new FakeXeroBillService();
        var service = CreateService([claim], employees: new FakeEmployeeDirectory(Ahmad), xero: xero);

        await service.BulkSyncToXeroAsync(["claim-1"], XeroBillStatus.AwaitingPayment);
        var second = await service.BulkSyncToXeroAsync(["claim-1"], XeroBillStatus.AwaitingPayment);

        // The end state is already true, so it is not an error — but no second bill.
        Assert.Equal(1, second.Succeeded);
        Assert.Equal(0, second.Failed);
        Assert.Single(xero.Created);
    }

    [Fact]
    public async Task BulkSyncToXeroAsync_KeepsGoingAfterOneClaimFails()
    {
        var first = NewClaim("first", "usr-emp", ClaimStatus.PENDING);   // will be refused
        var second = NewClaim("second", "usr-emp", ClaimStatus.APPROVED);
        var xero = new FakeXeroBillService();
        var service = CreateService([first, second], employees: new FakeEmployeeDirectory(Ahmad), xero: xero);

        var result = await service.BulkSyncToXeroAsync(["first", "second"],
            XeroBillStatus.AwaitingPayment);

        // A refusal early in the list must not strand the ones behind it.
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(XeroSyncStatus.SYNCED, second.XeroSyncStatus);
    }

    // ---- Company-paid: a spend, not a bill ----

    [Fact]
    public async Task SyncToXeroAsync_RecordsACompanyPaidClaimAsSpendMoney()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.APPROVED);
        claim.PaymentType = PaymentType.COMPANY;
        claim.PayViaAccountId = "acct-bank";   // code 1000 in the fake chart
        claim.SpendingAt = "Klinik Dr. Liau";

        var xero = new FakeXeroBillService();
        var service = CreateService([claim], employees: new FakeEmployeeDirectory(Ahmad), xero: xero);

        var result = await service.SyncToXeroAsync("claim-1", XeroBillStatus.AwaitingPayment);

        Assert.True(result.Ok);
        // A spend, never a bill — nobody was out of pocket, so there is no debt.
        Assert.Empty(xero.Created);
        var spend = Assert.Single(xero.Spends);
        // The contact is the merchant, not the employee.
        Assert.Equal("Klinik Dr. Liau", spend.ContactName);
        Assert.Equal(XeroSyncStatus.SYNCED, claim.XeroSyncStatus);
    }

    [Fact]
    public async Task SyncToXeroAsync_RefusesACompanyClaimWithNoAccountToSpendFrom()
    {
        var claim = NewClaim("claim-1", "usr-emp", ClaimStatus.APPROVED);
        claim.PaymentType = PaymentType.COMPANY;
        claim.PayViaAccountId = null;

        var xero = new FakeXeroBillService();
        var service = CreateService([claim], employees: new FakeEmployeeDirectory(Ahmad), xero: xero);

        var result = await service.SyncToXeroAsync("claim-1", XeroBillStatus.AwaitingPayment);

        Assert.False(result.Ok);
        // Guessing the bank account would misstate a balance.
        Assert.Empty(xero.Spends);
        Assert.Equal(XeroSyncStatus.ERROR, claim.XeroSyncStatus);
        Assert.Contains("nothing to spend from", claim.XeroSyncError);
    }

    [Fact]
    public async Task SyncToXeroAsync_ReturnsNotFoundForAnUnknownClaim()
    {
        var service = CreateService([], xero: new FakeXeroBillService());

        var result = await service.SyncToXeroAsync("ghost", XeroBillStatus.AwaitingPayment);

        Assert.False(result.Found);
    }
}
