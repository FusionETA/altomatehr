using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Claims.Dtos;
using AltomateHR.Api.Modules.Claims.Entities;
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

    [Fact]
    public async Task CreateAsync_StoresPrimaryReceiptAndSeparateSupportingDocuments()
    {
        var service = CreateService([]);

        var claim = await service.CreateAsync(ExpenseDto(), "usr-emp");

        Assert.Equal("/claims/receipts/main-receipt.pdf", claim.ReceiptUrl);
        Assert.Equal(
            ["/claims/receipts/support-a.pdf", "/claims/receipts/support-b.pdf"],
            claim.SupportingDocumentUrls);
    }

    [Fact]
    public async Task GetReceiptForUserAsync_ReturnsSecondSupportingDocumentForOwner()
    {
        var claim = NewClaim("claim-1", "usr-emp", receiptUrl: "/claims/receipts/main-receipt.pdf");
        claim.SupportingDocumentUrls = ["/claims/receipts/support-a.pdf", "/claims/receipts/support-b.pdf"];
        var service = CreateService([claim]);

        var receipt = await service.GetReceiptForUserAsync("support-b.pdf", "usr-emp", isAdmin: false);

        Assert.NotNull(receipt);
        Assert.Equal("support-b.pdf", receipt.DownloadName);
    }

    private static ClaimsService CreateFor(string employeeId) =>
        CreateService([NewClaim("claim-1", employeeId, receiptUrl: "/claims/receipts/receipt.pdf")]);

    private static CreateClaimDto ExpenseDto() => new()
    {
        Title = "Fuel",
        Description = "Project travel",
        Category = ClaimCategory.TRANSPORT,
        Amount = 25,
        Currency = "MYR",
        SpentAt = DateTime.UtcNow,
        ClaimType = ClaimType.EXPENSE,
        PaymentType = PaymentType.PERSONAL,
        ChartOfAccountId = "acct-expense",
        ReceiptUrl = "/claims/receipts/main-receipt.pdf",
        SupportingDocumentUrls =
        [
            "/claims/receipts/support-a.pdf",
            "/claims/receipts/support-b.pdf",
        ],
    };
}
