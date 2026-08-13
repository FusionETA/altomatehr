using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Claims.Entities;

namespace AltomateHR.Api.Tests.Claims;

public class ClaimsServiceReceiptTests
{
    [Fact]
    public async Task GetReceiptForUserAsync_ReturnsReceiptForOwner()
    {
        var service = CreateService(employeeId: "usr-emp");

        var receipt = await service.GetReceiptForUserAsync(
            "receipt.pdf",
            "usr-emp",
            isAdmin: false);

        Assert.NotNull(receipt);
        Assert.Equal("receipt.pdf", receipt.DownloadName);
    }

    [Fact]
    public async Task GetReceiptForUserAsync_ReturnsReceiptForAdmin()
    {
        var service = CreateService(employeeId: "usr-emp");

        var receipt = await service.GetReceiptForUserAsync(
            "receipt.pdf",
            "usr-admin",
            isAdmin: true);

        Assert.NotNull(receipt);
    }

    [Fact]
    public async Task GetReceiptForUserAsync_HidesReceiptFromAnotherEmployee()
    {
        var service = CreateService(employeeId: "usr-emp");

        var receipt = await service.GetReceiptForUserAsync(
            "receipt.pdf",
            "usr-other",
            isAdmin: false);

        Assert.Null(receipt);
    }

    private static ClaimsService CreateService(string employeeId)
    {
        var claim = new Claim
        {
            Id = "claim-1",
            ClaimNumber = "CLM-1",
            Title = "Lunch",
            Description = "Team lunch",
            EmployeeId = employeeId,
            ReceiptUrl = "/claims/receipts/receipt.pdf",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        return new ClaimsService(
            repo: new FakeClaimsRepository([claim]),
            receiptStorage: new FakeClaimReceiptStorage());
    }

    private sealed class FakeClaimReceiptStorage : IClaimReceiptStorage
    {
        public Task<ClaimReceiptUploadResult> StoreAsync(ClaimReceiptUpload upload) =>
            Task.FromResult(new ClaimReceiptUploadResult("/claims/receipts/receipt.pdf"));

        public Task<ClaimReceiptFileResult?> GetAsync(string fileName) =>
            Task.FromResult<ClaimReceiptFileResult?>(
                new ClaimReceiptFileResult(
                    Path.Combine(Path.GetTempPath(), fileName),
                    "application/pdf",
                    fileName));
    }

    private sealed class FakeClaimsRepository : IClaimsRepository
    {
        private readonly List<Claim> _claims;

        public FakeClaimsRepository(IEnumerable<Claim> claims) => _claims = claims.ToList();

        public Task<List<Claim>> GetAllAsync() => Task.FromResult(_claims);

        public Task<List<Claim>> GetByEmployeeIdAsync(string employeeId) =>
            Task.FromResult(_claims.Where(c => c.EmployeeId == employeeId).ToList());

        public Task<Claim?> GetByIdAsync(string id) =>
            Task.FromResult(_claims.FirstOrDefault(c => c.Id == id));

        public Task<Claim?> GetByReceiptUrlAsync(string receiptUrl) =>
            Task.FromResult(_claims.FirstOrDefault(c => c.ReceiptUrl == receiptUrl));

        public Task<Claim> AddAsync(Claim claim)
        {
            _claims.Add(claim);
            return Task.FromResult(claim);
        }

        public Task UpdateAsync(Claim claim) => Task.CompletedTask;

        public Task<bool> DeleteAsync(string id)
        {
            var removed = _claims.RemoveAll(c => c.Id == id) > 0;
            return Task.FromResult(removed);
        }
    }
}
