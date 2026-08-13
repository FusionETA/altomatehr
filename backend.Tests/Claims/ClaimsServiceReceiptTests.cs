using AltomateHR.Api.Modules.Claims;
using static AltomateHR.Api.Tests.Claims.ClaimsTestFactory;

namespace AltomateHR.Api.Tests.Claims;

public class ClaimsServiceReceiptTests
{
    [Fact]
    public async Task GetReceiptForUserAsync_ReturnsReceiptForOwner()
    {
        var service = CreateFor("usr-emp");

        var receipt = await service.GetReceiptForUserAsync("receipt.pdf", "usr-emp", isAdmin: false);

        Assert.NotNull(receipt);
        Assert.Equal("receipt.pdf", receipt.DownloadName);
    }

    [Fact]
    public async Task GetReceiptForUserAsync_ReturnsReceiptForAdmin()
    {
        var service = CreateFor("usr-emp");

        var receipt = await service.GetReceiptForUserAsync("receipt.pdf", "usr-admin", isAdmin: true);

        Assert.NotNull(receipt);
    }

    [Fact]
    public async Task GetReceiptForUserAsync_HidesReceiptFromAnotherEmployee()
    {
        var service = CreateFor("usr-emp");

        var receipt = await service.GetReceiptForUserAsync("receipt.pdf", "usr-other", isAdmin: false);

        Assert.Null(receipt);
    }

    private static ClaimsService CreateFor(string employeeId) =>
        CreateService([NewClaim("claim-1", employeeId, receiptUrl: "/claims/receipts/receipt.pdf")]);
}
