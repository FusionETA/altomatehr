using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Claims.Dtos;
using AltomateHR.Api.Modules.Accounts;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Modules.Claims;

// Business logic: DTO → entity mapping + business rules.
public class ClaimsService : IClaimsService
{
    private const ApprovalModule Module = ApprovalModule.CLAIMS;

    private readonly IClaimsRepository _repo;
    private readonly IClaimReceiptStorage _receiptStorage;
    private readonly IChartOfAccountService _accounts;
    private readonly ISupervisionService _supervision;
    private readonly IApprovalRouter _router;
    private readonly IOrganizationService _organizations;
    private readonly ICurrentUser _currentUser;

    public ClaimsService(
        IClaimsRepository repo,
        IClaimReceiptStorage receiptStorage,
        IChartOfAccountService accounts,
        ISupervisionService supervision,
        IApprovalRouter router,
        IOrganizationService organizations,
        ICurrentUser currentUser)
    {
        _repo = repo;
        _receiptStorage = receiptStorage;
        _accounts = accounts;
        _supervision = supervision;
        _router = router;
        _organizations = organizations;
        _currentUser = currentUser;
    }

    // The caller's own claims.
    public async Task<IEnumerable<Claim>> GetMineAsync(string userId) =>
        await _repo.GetByEmployeeIdAsync(userId);

    // Claims the caller can act on: an org approver sees the whole org; a
    // supervisor sees only their direct reports. Each row is labelled with the
    // applicant's email so the approver knows who filed it.
    public async Task<IEnumerable<Claim>> GetTeamAsync(string userId)
    {
        var all = await _repo.GetAllAsync();
        var claims = new List<Claim>();
        foreach (var c in all.Where(c => c.Status == ClaimStatus.PENDING))
        {
            var approvers = await _router.CurrentApproversAsync(Module, c.EmployeeId, c.CurrentStep);
            if (approvers.Contains(userId)) claims.Add(c);
        }

        var emails = await _supervision.GetEmailsAsync(claims.Select(c => c.EmployeeId).Distinct());
        foreach (var c in claims)
            c.EmployeeEmail = emails.GetValueOrDefault(c.EmployeeId);
        return claims;
    }

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
        var prepared = await PrepareClaimValuesAsync(dto, employeeId);
        var supportingDocuments = NormalizeSupportingDocuments(dto);
        var receiptUrl = Clean(dto.ReceiptUrl);

        var claim = new Claim
        {
            ClaimNumber = GenerateClaimNumber(),   // server-generated
            Title = dto.Title,
            Description = dto.Description,
            Category = dto.Category,
            Amount = prepared.Amount,
            Currency = dto.Currency,
            SpentAt = dto.SpentAt!.Value,
            SubmittedAt = now,
            Status = ClaimStatus.PENDING,          // business rule: new claims start PENDING
            ClaimType = dto.ClaimType,
            PaymentType = dto.PaymentType,
            PayViaAccountId = prepared.PayViaAccountId,
            SpendingWith = Clean(dto.SpendingWith),
            SpendingAt = Clean(dto.SpendingAt),
            EmployeeId = employeeId,
            ProjectId = dto.ProjectId,
            ChartOfAccountId = dto.ChartOfAccountId,
            ExceedsLimit = prepared.ExceedsLimit,
            Distance = prepared.Distance,
            MileageOriginAddress = prepared.MileageOriginAddress,
            MileageDestinationAddress = prepared.MileageDestinationAddress,
            MileageRateUsed = prepared.MileageRateUsed,
            MileageUnitUsed = prepared.MileageUnitUsed,
            ReceiptUrl = receiptUrl,
            SupportingDocumentUrls = supportingDocuments,
            CreatedAt = now,
            UpdatedAt = now,
        };
        return await _repo.AddAsync(claim);
    }

    public async Task<Claim?> UpdateAsync(string id, CreateClaimDto dto, string userId, bool isAdmin)
    {
        var claim = await _repo.GetByIdAsync(id);
        if (claim is null) return null;
        if (!isAdmin && claim.EmployeeId != userId) return null;
        if (claim.Status is not (ClaimStatus.SUBMITTED or ClaimStatus.PENDING))
            throw new ClaimValidationException("This claim has already been reviewed and can no longer be edited.");
        if (dto.ClaimType != claim.ClaimType)
            throw new ClaimValidationException("Claim type cannot be changed after submission.", nameof(dto.ClaimType));
        if (dto.PaymentType != claim.PaymentType)
            throw new ClaimValidationException("Payment source cannot be changed after submission.", nameof(dto.PaymentType));

        var supportingDocuments = NormalizeSupportingDocuments(dto);
        var receiptUrl = Clean(dto.ReceiptUrl);

        claim.Title = dto.Title;
        claim.Description = dto.Description;
        claim.Category = dto.Category;
        var prepared = await PrepareClaimValuesAsync(dto, claim.EmployeeId);
        claim.Amount = prepared.Amount;
        claim.Currency = dto.Currency;
        claim.SpentAt = dto.SpentAt!.Value;
        claim.ClaimType = dto.ClaimType;
        claim.PaymentType = dto.PaymentType;
        claim.PayViaAccountId = prepared.PayViaAccountId;
        claim.SpendingWith = Clean(dto.SpendingWith);
        claim.SpendingAt = Clean(dto.SpendingAt);
        claim.ChartOfAccountId = dto.ChartOfAccountId;
        claim.ProjectId = dto.ProjectId;
        claim.ExceedsLimit = prepared.ExceedsLimit;
        claim.Distance = prepared.Distance;
        claim.MileageOriginAddress = prepared.MileageOriginAddress;
        claim.MileageDestinationAddress = prepared.MileageDestinationAddress;
        claim.MileageRateUsed = prepared.MileageRateUsed;
        claim.MileageUnitUsed = prepared.MileageUnitUsed;
        claim.ReceiptUrl = receiptUrl;
        claim.SupportingDocumentUrls = supportingDocuments;
        claim.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(claim);
        return claim;
    }

    public Task<bool> DeleteAsync(string id) => _repo.DeleteAsync(id);

    public async Task<ClaimStatusTransitionResult> ApproveAsync(string id, string approverId)
    {
        var (claim, error) = await AuthorizeAsync(id, approverId);
        if (error is not null) return error;

        var stepCount = await _router.StepCountAsync(Module, claim!.EmployeeId);
        var isFinal = claim.CurrentStep + 1 >= stepCount;
        if (isFinal)
            claim.Status = ClaimStatus.APPROVED;
        else
            claim.CurrentStep += 1;   // advance to the next step; stays PENDING

        claim.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(claim);
        return new ClaimStatusTransitionResult(true, true, claim);
    }

    public async Task<ClaimStatusTransitionResult> RejectAsync(string id, string approverId, string? reviewNotes)
    {
        var (claim, error) = await AuthorizeAsync(id, approverId);
        if (error is not null) return error;

        var cleanedReviewNotes = Clean(reviewNotes);
        if (cleanedReviewNotes is null)
        {
            return new ClaimStatusTransitionResult(
                true,
                false,
                null,
                "Enter a rejection remark before rejecting this claim.");
        }

        claim!.Status = ClaimStatus.REJECTED;
        claim.ReviewNotes = cleanedReviewNotes;
        claim.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(claim);
        return new ClaimStatusTransitionResult(true, true, claim);
    }

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

    private async Task<PreparedClaimValues> PrepareClaimValuesAsync(CreateClaimDto dto, string employeeId)
    {
        if (string.IsNullOrWhiteSpace(dto.ChartOfAccountId))
            throw new ClaimValidationException("Select the chart of account for this claim.", nameof(dto.ChartOfAccountId));

        if (dto.PaymentType == PaymentType.COMPANY)
        {
            if (string.IsNullOrWhiteSpace(dto.PayViaAccountId))
                throw new ClaimValidationException("Select the company bank account that paid for this claim.", nameof(dto.PayViaAccountId));

            if (string.IsNullOrWhiteSpace(dto.SpendingAt))
                throw new ClaimValidationException("Enter the merchant / vendor name from the receipt.", nameof(dto.SpendingAt));
        }

        var account = await _accounts.GetByIdAsync(dto.ChartOfAccountId);
        if (account is null || account.IsArchived)
            throw new ClaimValidationException("Please choose one of the enabled chart of account options.", nameof(dto.ChartOfAccountId));

        if (dto.ClaimType == ClaimType.MILEAGE && !account.AllowMileageClaim)
            throw new ClaimValidationException("Select an account configured for mileage claims.", nameof(dto.ChartOfAccountId));

        if (dto.ClaimType == ClaimType.EXPENSE && !account.IsSelectable)
            throw new ClaimValidationException("Select an enabled chart of account option.", nameof(dto.ChartOfAccountId));

        string? payViaAccountId = null;
        if (dto.PaymentType == PaymentType.COMPANY)
        {
            var bankAccount = await _accounts.GetByIdAsync(dto.PayViaAccountId!);
            if (bankAccount is null || bankAccount.IsArchived || bankAccount.Type != "BANK")
            {
                throw new ClaimValidationException(
                    "Select a bank account enabled by your admin for company-money claims.",
                    nameof(dto.PayViaAccountId));
            }

            payViaAccountId = bankAccount.Id;
        }

        decimal amount;
        decimal? distance = null;
        string? mileageOriginAddress = null;
        string? mileageDestinationAddress = null;
        decimal? mileageRateUsed = null;
        MileageUnit? mileageUnitUsed = null;

        if (dto.ClaimType == ClaimType.MILEAGE)
        {
            if (dto.Distance is null || dto.Distance <= 0)
                throw new ClaimValidationException("Distance must be greater than zero.", nameof(dto.Distance));

            if (string.IsNullOrWhiteSpace(dto.MileageOriginAddress))
                throw new ClaimValidationException("Enter the trip origin.", nameof(dto.MileageOriginAddress));

            if (string.IsNullOrWhiteSpace(dto.MileageDestinationAddress))
                throw new ClaimValidationException("Enter the trip destination.", nameof(dto.MileageDestinationAddress));

            var orgId = _currentUser.OrganizationId;
            if (string.IsNullOrEmpty(orgId))
                throw new ClaimValidationException("Your account is not assigned to an organization yet.");

            var org = await _organizations.GetByIdAsync(orgId);
            if (org is null)
                throw new ClaimValidationException("Your organization settings could not be found.");

            var resolvedRate = ResolveMileageRate(account.MileageRate, org.DefaultMileageRate);
            if (resolvedRate is null)
            {
                throw new ClaimValidationException(
                    "Mileage rate is not configured. Ask your admin to set the rate in Mileage claim settings.",
                    nameof(dto.ChartOfAccountId));
            }

            amount = ComputeMileageAmount(dto.Distance.Value, resolvedRate.Value);
            if (amount <= 0)
                throw new ClaimValidationException("Mileage amount must be greater than zero.", nameof(dto.Distance));

            distance = decimal.Round(dto.Distance.Value, 2, MidpointRounding.AwayFromZero);
            mileageOriginAddress = dto.MileageOriginAddress.Trim();
            mileageDestinationAddress = dto.MileageDestinationAddress.Trim();
            mileageRateUsed = decimal.Round(resolvedRate.Value, 4, MidpointRounding.AwayFromZero);
            mileageUnitUsed = org.MileageUnit;
        }
        else
        {
            if (dto.Amount is null || dto.Amount <= 0)
                throw new ClaimValidationException("Amount must be greater than zero.", nameof(dto.Amount));

            amount = dto.Amount.Value;
        }

        var exceedsLimit = account.LimitAmount is decimal limit && amount > limit;
        return new PreparedClaimValues(
            amount,
            exceedsLimit,
            payViaAccountId,
            distance,
            mileageOriginAddress,
            mileageDestinationAddress,
            mileageRateUsed,
            mileageUnitUsed);
    }

    private static decimal? ResolveMileageRate(decimal? accountRate, decimal orgDefaultRate)
    {
        if (accountRate is decimal account && account > 0) return account;
        return orgDefaultRate > 0 ? orgDefaultRate : null;
    }

    private static decimal ComputeMileageAmount(decimal distance, decimal rate) =>
        decimal.Round(distance * rate, 2, MidpointRounding.AwayFromZero);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> NormalizeSupportingDocuments(CreateClaimDto dto)
    {
        var urls = new List<string>();

        if (dto.SupportingDocumentUrls is not null)
        {
            urls.AddRange(dto.SupportingDocumentUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url.Trim()));
        }

        urls = urls.Distinct(StringComparer.Ordinal).ToList();
        if (urls.Count > 10)
            throw new ClaimValidationException("Attach no more than 10 supporting documents.", nameof(dto.SupportingDocumentUrls));

        if (urls.Any(url => !url.StartsWith("/claims/receipts/", StringComparison.Ordinal)) ||
            (!string.IsNullOrWhiteSpace(dto.ReceiptUrl) &&
             !dto.ReceiptUrl.Trim().StartsWith("/claims/receipts/", StringComparison.Ordinal)))
            throw new ClaimValidationException("Uploaded document URL is invalid.", nameof(dto.SupportingDocumentUrls));

        return urls;
    }

    // Loads the claim and checks the caller may act at its current step. Returns
    // an error result (to return as-is) on any failure; otherwise the claim.
    private async Task<(Claim? Claim, ClaimStatusTransitionResult? Error)> AuthorizeAsync(
        string id, string approverId)
    {
        var claim = await _repo.GetByIdAsync(id);
        if (claim is null)
            return (null, new ClaimStatusTransitionResult(false, false, null));

        // Only the current-step approver may act (by team seat); others are
        // treated as not-found so the claim stays hidden.
        var approvers = await _router.CurrentApproversAsync(Module, claim.EmployeeId, claim.CurrentStep);
        if (!approvers.Contains(approverId))
            return (null, new ClaimStatusTransitionResult(false, false, null));

        if (claim.Status != ClaimStatus.PENDING)
            return (claim, new ClaimStatusTransitionResult(true, false, claim,
                "Only pending claims can be approved or rejected."));

        return (claim, null);
    }
}

// Final values after claim rules are validated and calculated, ready to copy
// onto the Claim entity for saving.
internal sealed record PreparedClaimValues(
    decimal Amount,
    bool ExceedsLimit,
    string? PayViaAccountId,
    decimal? Distance,
    string? MileageOriginAddress,
    string? MileageDestinationAddress,
    decimal? MileageRateUsed,
    MileageUnit? MileageUnitUsed);
