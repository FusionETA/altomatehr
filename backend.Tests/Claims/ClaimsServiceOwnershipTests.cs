using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Claims.Dtos;
using AltomateHR.Api.Modules.Claims.Entities;

namespace AltomateHR.Api.Tests.Claims;

public class ClaimsServiceOwnershipTests
{
    [Fact]
    public async Task CreateAsync_StoresEmployeeIdFromAuthenticatedUserId()
    {
        var service = CreateService([]);

        var claim = await service.CreateAsync(CreateClaimDto(), employeeId: "usr-emp");

        Assert.Equal("usr-emp", claim.EmployeeId);
    }

    [Fact]
    public async Task GetVisibleForUserAsync_ReturnsOnlyOwnClaimsForEmployee()
    {
        var service = CreateService([
            Claim("own", "usr-emp"),
            Claim("other", "usr-other"),
        ]);

        var claims = await service.GetVisibleForUserAsync("usr-emp", isAdmin: false);

        Assert.Collection(claims, claim => Assert.Equal("own", claim.Id));
    }

    [Fact]
    public async Task GetVisibleForUserAsync_ReturnsAllClaimsForAdmin()
    {
        var service = CreateService([
            Claim("own", "usr-emp"),
            Claim("other", "usr-other"),
        ]);

        var claims = await service.GetVisibleForUserAsync("usr-admin", isAdmin: true);

        Assert.Equal(2, claims.Count());
    }

    [Fact]
    public async Task UpdateAsync_DoesNotAllowEmployeeToUpdateAnotherUsersClaim()
    {
        var service = CreateService([Claim("other", "usr-other")]);

        var updated = await service.UpdateAsync("other", CreateClaimDto(), "usr-emp", isAdmin: false);

        Assert.False(updated);
    }

    [Fact]
    public async Task UpdateAsync_PreservesClaimOwner()
    {
        var claim = Claim("own", "usr-emp");
        var service = CreateService([claim]);

        var updated = await service.UpdateAsync("own", CreateClaimDto(title: "Updated"), "usr-emp", isAdmin: false);

        Assert.True(updated);
        Assert.Equal("usr-emp", claim.EmployeeId);
        Assert.Equal("Updated", claim.Title);
    }

    private static ClaimsService CreateService(IEnumerable<Claim> claims) =>
        new(new FakeClaimsRepository(claims), new FakeClaimReceiptStorage());

    private static Claim Claim(string id, string employeeId) => new()
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
        EmployeeId = employeeId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static CreateClaimDto CreateClaimDto(string title = "Lunch") => new()
    {
        Title = title,
        Description = "Team lunch",
        Category = ClaimCategory.MEAL,
        Amount = 12,
        Currency = "MYR",
        SpentAt = DateTime.UtcNow,
        ClaimType = ClaimType.EXPENSE,
        PaymentType = PaymentType.PERSONAL,
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
