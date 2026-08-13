using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Claims.Entities;

namespace AltomateHR.Api.Tests.Claims;

public class ClaimsServiceApprovalTests
{
    [Fact]
    public async Task ApproveAsync_TransitionsPendingClaimToApproved()
    {
        var claim = Claim("claim-1", ClaimStatus.PENDING);
        var service = CreateService([claim]);

        var result = await service.ApproveAsync("claim-1");

        Assert.True(result.Found);
        Assert.True(result.Transitioned);
        Assert.Equal(ClaimStatus.APPROVED, claim.Status);
        Assert.Null(claim.ReviewNotes);
    }

    [Fact]
    public async Task RejectAsync_TransitionsPendingClaimToRejectedAndStoresReviewNotes()
    {
        var claim = Claim("claim-1", ClaimStatus.PENDING);
        var service = CreateService([claim]);

        var result = await service.RejectAsync("claim-1", "Receipt is unreadable.");

        Assert.True(result.Found);
        Assert.True(result.Transitioned);
        Assert.Equal(ClaimStatus.REJECTED, claim.Status);
        Assert.Equal("Receipt is unreadable.", claim.ReviewNotes);
    }

    [Fact]
    public async Task ApproveAsync_ReturnsNotFoundForMissingClaim()
    {
        var service = CreateService([]);

        var result = await service.ApproveAsync("missing");

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
        var claim = Claim("claim-1", currentStatus);
        var service = CreateService([claim]);

        var result = await service.ApproveAsync("claim-1");

        Assert.True(result.Found);
        Assert.False(result.Transitioned);
        Assert.Equal(currentStatus, claim.Status);
        Assert.Equal("Only pending claims can be approved or rejected.", result.ErrorMessage);
    }

    private static ClaimsService CreateService(IEnumerable<Claim> claims) =>
        new(new FakeClaimsRepository(claims), new FakeClaimReceiptStorage());

    private static Claim Claim(string id, ClaimStatus status) => new()
    {
        Id = id,
        ClaimNumber = $"CLM-{id}",
        Title = "Lunch",
        Description = "Team lunch",
        Category = ClaimCategory.MEAL,
        Amount = 12,
        Currency = "MYR",
        SpentAt = DateTime.UtcNow,
        SubmittedAt = DateTime.UtcNow,
        Status = status,
        EmployeeId = "usr-emp",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private sealed class FakeClaimReceiptStorage : IClaimReceiptStorage
    {
        public Task<ClaimReceiptUploadResult> StoreAsync(ClaimReceiptUpload upload) =>
            Task.FromResult(new ClaimReceiptUploadResult("/claims/receipts/receipt.pdf"));

        public Task<ClaimReceiptFileResult?> GetAsync(string fileName) =>
            Task.FromResult<ClaimReceiptFileResult?>(null);
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
