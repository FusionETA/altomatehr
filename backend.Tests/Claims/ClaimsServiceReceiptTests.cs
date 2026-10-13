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

    // Reading the receipt with AI is optional; attaching it is not.
    [Fact]
    public async Task CreateAsync_RefusesAnExpenseClaimWithoutItsMainReceipt()
    {
        var service = CreateService([]);
        var dto = ExpenseDto();
        dto.ReceiptUrl = "  ";

        var error = await Assert.ThrowsAsync<ClaimValidationException>(() => service.CreateAsync(dto, "usr-emp"));

        Assert.Equal(nameof(CreateClaimDto.ReceiptUrl), error.Field);
    }

    [Fact]
    public async Task UpdateAsync_CanReplaceTheMainReceipt_ButNotRemoveIt()
    {
        var service = CreateService([NewClaim("claim-1", "usr-emp", receiptUrl: "/claims/receipts/old.pdf")]);
        var dto = ExpenseDto();
        dto.ReceiptUrl = null;

        await Assert.ThrowsAsync<ClaimValidationException>(() =>
            service.UpdateAsync("claim-1", dto, "usr-emp", isAdmin: false));

        dto.ReceiptUrl = "/claims/receipts/new.pdf";
        var updated = await service.UpdateAsync("claim-1", dto, "usr-emp", isAdmin: false);
        Assert.Equal("/claims/receipts/new.pdf", updated!.ReceiptUrl);
    }

    [Fact]
    public async Task UpdateAsync_KeepsAnOlderClaimWithoutAReceiptEditable()
    {
        var service = CreateService([NewClaim("claim-1", "usr-emp")]);
        var dto = ExpenseDto();
        dto.ReceiptUrl = null;

        var updated = await service.UpdateAsync("claim-1", dto, "usr-emp", isAdmin: false);

        Assert.Null(updated!.ReceiptUrl);
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
