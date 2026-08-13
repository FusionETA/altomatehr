using AltomateHR.Api.Modules.Claims.Dtos;
using AltomateHR.Api.Modules.Accounts;
using AltomateHR.Api.Modules.Claims.Entities;

namespace AltomateHR.Api.Modules.Claims;

// Business logic: DTO → entity mapping + business rules.
public class ClaimsService : IClaimsService
{
    private readonly IClaimsRepository _repo;
    private readonly IClaimReceiptStorage _receiptStorage;
    private readonly IChartOfAccountService _accounts;

    public ClaimsService(
        IClaimsRepository repo,
        IClaimReceiptStorage receiptStorage,
        IChartOfAccountService accounts)
    {
        _repo = repo;
        _receiptStorage = receiptStorage;
        _accounts = accounts;
    }

    public async Task<IEnumerable<Claim>> GetAllAsync() => await _repo.GetAllAsync();

    public async Task<IEnumerable<Claim>> GetVisibleForUserAsync(string userId, bool isAdmin) =>
        isAdmin
            ? await _repo.GetAllAsync()
            : await _repo.GetByEmployeeIdAsync(userId);

    public Task<Claim?> GetByIdAsync(string id) => _repo.GetByIdAsync(id);

    public async Task<Claim?> GetVisibleByIdAsync(string id, string userId, bool isAdmin)
    {
        var claim = await _repo.GetByIdAsync(id);
        if (claim is null) return null;
        return isAdmin || claim.EmployeeId == userId ? claim : null;
    }

    public async Task<Claim> CreateAsync(CreateClaimDto dto, string employeeId)
    {
        var now = DateTime.UtcNow;

        // If filed against an account with a spend limit, flag over-limit claims.
        // They are still saved (employees may submit over-limit); admins see the flag.
        var exceedsLimit = false;
        if (!string.IsNullOrEmpty(dto.ChartOfAccountId))
        {
            var account = await _accounts.GetByIdAsync(dto.ChartOfAccountId);
            if (account?.LimitAmount is decimal limit && dto.Amount > limit)
                exceedsLimit = true;
        }

        var claim = new Claim
        {
            ClaimNumber = GenerateClaimNumber(),   // server-generated
            Title = dto.Title,
            Description = dto.Description,
            Category = dto.Category,
            Amount = dto.Amount,
            Currency = dto.Currency,
            SpentAt = dto.SpentAt!.Value,
            SubmittedAt = now,
            Status = ClaimStatus.PENDING,          // business rule: new claims start PENDING
            ClaimType = dto.ClaimType,
            PaymentType = dto.PaymentType,
            EmployeeId = employeeId,
            ProjectId = dto.ProjectId,
            ChartOfAccountId = dto.ChartOfAccountId,
            ExceedsLimit = exceedsLimit,
            ReceiptUrl = dto.ReceiptUrl,
            CreatedAt = now,
            UpdatedAt = now,
        };
        return await _repo.AddAsync(claim);
    }

    public async Task<bool> UpdateAsync(string id, CreateClaimDto dto, string userId, bool isAdmin)
    {
        var claim = await _repo.GetByIdAsync(id);
        if (claim is null) return false;
        if (!isAdmin && claim.EmployeeId != userId) return false;

        claim.Title = dto.Title;
        claim.Description = dto.Description;
        claim.Category = dto.Category;
        claim.Amount = dto.Amount;
        claim.Currency = dto.Currency;
        claim.SpentAt = dto.SpentAt!.Value;
        claim.ClaimType = dto.ClaimType;
        claim.PaymentType = dto.PaymentType;
        claim.ReceiptUrl = dto.ReceiptUrl;
        claim.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(claim);
        return true;
    }

    public Task<bool> DeleteAsync(string id) => _repo.DeleteAsync(id);

    public Task<ClaimStatusTransitionResult> ApproveAsync(string id) =>
        TransitionStatusAsync(id, ClaimStatus.APPROVED, reviewNotes: null);

    public Task<ClaimStatusTransitionResult> RejectAsync(string id, string? reviewNotes) =>
        TransitionStatusAsync(id, ClaimStatus.REJECTED, reviewNotes);

    public Task<ClaimReceiptUploadResult> StoreReceiptAsync(ClaimReceiptUpload upload) =>
        _receiptStorage.StoreAsync(upload);

    public async Task<ClaimReceiptFileResult?> GetReceiptForUserAsync(
        string fileName,
        string userId,
        bool isAdmin)
    {
        var receiptUrl = $"/claims/receipts/{fileName}";
        var claim = await _repo.GetByReceiptUrlAsync(receiptUrl);
        if (claim is null)
            return null;

        if (!isAdmin && claim.EmployeeId != userId)
            return null;

        return await _receiptStorage.GetAsync(fileName);
    }

    private static string GenerateClaimNumber() =>
        $"CLM-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";

    private async Task<ClaimStatusTransitionResult> TransitionStatusAsync(
        string id,
        ClaimStatus nextStatus,
        string? reviewNotes)
    {
        var claim = await _repo.GetByIdAsync(id);
        if (claim is null)
            return new ClaimStatusTransitionResult(false, false, null);

        if (claim.Status != ClaimStatus.PENDING)
        {
            return new ClaimStatusTransitionResult(
                true,
                false,
                claim,
                "Only pending claims can be approved or rejected.");
        }

        claim.Status = nextStatus;
        claim.ReviewNotes = reviewNotes;
        claim.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(claim);

        return new ClaimStatusTransitionResult(true, true, claim);
    }
}
