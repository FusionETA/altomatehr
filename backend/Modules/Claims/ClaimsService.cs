using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Common;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Claims.Dtos;
using AltomateHR.Api.Modules.Accounts;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Realtime;
using AltomateHR.Api.Modules.Realtime.Dtos;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Modules.Claims;

// Business logic: DTO → entity mapping + business rules.
public class ClaimsService : IClaimsService
{
    private const ApprovalModule Module = ApprovalModule.CLAIMS;

    // Same cap as the attendance bulk endpoints. A request larger than this is
    // refused whole rather than half-applied.
    private const int MaxBulkIds = 200;

    private readonly IClaimsRepository _repo;
    private readonly IClaimReceiptStorage _receiptStorage;
    private readonly IChartOfAccountService _accounts;
    private readonly ISupervisionService _supervision;
    private readonly IApprovalRouter _router;
    private readonly IOrganizationService _organizations;
    private readonly ICurrentUser _currentUser;
    private readonly IRealtimeService _realtime;
    private readonly IEmployeeRowResolver _employees;
    private readonly IProjectService _projects;

    public ClaimsService(
        IClaimsRepository repo,
        IClaimReceiptStorage receiptStorage,
        IChartOfAccountService accounts,
        ISupervisionService supervision,
        IApprovalRouter router,
        IOrganizationService organizations,
        ICurrentUser currentUser,
        IRealtimeService realtime,
        IEmployeeRowResolver employees,
        IProjectService projects)
    {
        _repo = repo;
        _receiptStorage = receiptStorage;
        _accounts = accounts;
        _supervision = supervision;
        _router = router;
        _organizations = organizations;
        _currentUser = currentUser;
        _realtime = realtime;
        _employees = employees;
        _projects = projects;
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
        var saved = await _repo.AddAsync(claim);
        await NotifyAsync(saved, RealtimeAction.SUBMITTED, notifyClaimant: false);
        return saved;
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

        // An approver may already be looking at this claim in their queue; an
        // edit changes what they'd be signing off on.
        await NotifyAsync(claim, RealtimeAction.UPDATED, notifyClaimant: claim.EmployeeId != userId);
        return claim;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        // Read before deleting: once the row is gone there is no org to publish
        // to and no claimant to tell that their claim vanished.
        var claim = await _repo.GetByIdAsync(id);
        var deleted = await _repo.DeleteAsync(id);

        if (deleted && claim is not null)
            await NotifyAsync(claim, RealtimeAction.DELETED, notifyClaimant: true, notifyApprovers: true);

        return deleted;
    }

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

        // Notifies the claimant AND, when the chain advanced rather than ended,
        // whoever is now the current-step approver — so the claim appears in the
        // next reviewer's queue without them reloading.
        await NotifyAsync(claim, RealtimeAction.APPROVED, notifyClaimant: true);
        return new ClaimStatusTransitionResult(true, true, claim);
    }

    // Approve many claims in one call. Each is judged independently — a claim the
    // caller may not approve, or that someone else already decided, fails on its
    // own line and never blocks the rest.
    //
    // Over-limit claims are deliberately EXCLUDED. ExceedsLimit marks a claim
    // that blew past the account's spend limit, which is exactly the kind that
    // wants a human looking at it; letting one ride along in a batch of twenty
    // is how an over-limit claim gets approved without anyone reading it. They
    // are refused with a reason so the approver knows to open them individually,
    // rather than silently dropped.
    public async Task<ClaimsBulkResult> BulkApproveAsync(IReadOnlyList<string> ids, string approverId)
    {
        if (ids.Count > MaxBulkIds)
        {
            return new ClaimsBulkResult(0, ids.Count, [
                new ClaimsBulkResultItem(string.Empty, false, $"Too many claims — pick fewer than {MaxBulkIds}."),
            ]);
        }

        var items = new List<ClaimsBulkResultItem>(ids.Count);
        var approved = new List<Claim>();

        // Distinct: the same id twice would otherwise be counted as two successes
        // while only one claim moved.
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            var (claim, error) = await AuthorizeAsync(id, approverId);
            if (error is not null)
            {
                items.Add(new ClaimsBulkResultItem(id, false, error.ErrorMessage ?? "You can't approve this claim."));
                continue;
            }

            if (claim!.ExceedsLimit)
            {
                items.Add(new ClaimsBulkResultItem(id, false,
                    "Over the spend limit — approve this one on its own after reading it."));
                continue;
            }

            var stepCount = await _router.StepCountAsync(Module, claim.EmployeeId);
            if (claim.CurrentStep + 1 >= stepCount)
                claim.Status = ClaimStatus.APPROVED;
            else
                claim.CurrentStep += 1;

            claim.UpdatedAt = DateTime.UtcNow;
            await _repo.UpdateAsync(claim);
            approved.Add(claim);
            items.Add(new ClaimsBulkResultItem(id, true));
        }

        // Notify after every write lands, so a claimant refreshing on the first
        // notification sees the whole batch settled rather than a partial state.
        foreach (var claim in approved)
            await NotifyAsync(claim, RealtimeAction.APPROVED, notifyClaimant: true);

        return new ClaimsBulkResult(items.Count(i => i.Ok), items.Count(i => !i.Ok), items);
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

        // Rejection is terminal, so there is no next approver — only the
        // claimant needs to know.
        await NotifyAsync(claim, RealtimeAction.REJECTED, notifyClaimant: true, notifyApprovers: true);
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

    // ---- Import / export ----

    public async Task<TabularExportResult> ExportSummaryAsync(ClaimsExportQueryDto query, TabularFormat format)
    {
        var bySubmitted = string.Equals(query.DateField, "submitted", StringComparison.OrdinalIgnoreCase);

        var claims = (await _repo.GetAllAsync())
            .Where(c => Matches(c, query, bySubmitted))
            .OrderByDescending(c => bySubmitted ? c.SubmittedAt : c.SpentAt)
            .ThenBy(c => c.ClaimNumber, StringComparer.Ordinal)
            .ToList();

        // Three lookups for the whole file rather than per row: an export of a
        // few thousand claims would otherwise be a few thousand queries.
        var employees = await _employees.GetSnapshotAsync();
        var projects = (await _projects.GetAllAsync())
            .ToDictionary(p => p.Id, p => p.Name, StringComparer.Ordinal);
        var accounts = (await _accounts.GetAllAsync())
            .ToDictionary(a => a.Id, a => (a.Code, a.Name), StringComparer.Ordinal);

        var caption = DescribeSelection(query, bySubmitted, claims.Count);

        // PDF gets a narrower, printable sheet — see ClaimsSummarySheet's note on
        // why A4 landscape can't carry the full spreadsheet column set.
        if (format == TabularFormat.Pdf)
        {
            var organizationName = await OrganizationNameAsync();
            var printable = ClaimsSummarySheet.BuildPrintable(
                claims, employees, projects, accounts, caption);

            return TabularExportResult.From(
                printable, format, ExportFileName(query),
                new TabularPdfHeader(organizationName, "Claims Report"));
        }

        var sheet = ClaimsSummarySheet.BuildExport(claims, employees, projects, accounts);
        return TabularExportResult.From(sheet, format, ExportFileName(query));
    }

    // The sentence printed under the report title. A PDF outlives the filename
    // it was downloaded under, so what it covers has to be on the page.
    private static string DescribeSelection(ClaimsExportQueryDto query, bool bySubmitted, int count)
    {
        var dateLabel = bySubmitted ? "submitted" : "spent";
        var range = (query.From, query.To) switch
        {
            ({ } from, { } to) => $"{from:dd MMM yyyy} – {to:dd MMM yyyy} ({dateLabel})",
            ({ } from, null) => $"from {from:dd MMM yyyy} ({dateLabel})",
            (null, { } to) => $"up to {to:dd MMM yyyy} ({dateLabel})",
            _ => "All dates",
        };

        var parts = new List<string> { range, $"{count} claim(s)" };
        if (query.Status is { } status) parts.Add($"status {status}");
        if (query.PaymentType is { } paymentType) parts.Add($"paid with {paymentType.ToString().ToLowerInvariant()} money");
        if (!string.IsNullOrWhiteSpace(query.EmployeeId)) parts.Add("one employee");
        if (!string.IsNullOrWhiteSpace(query.ProjectId)) parts.Add("one project");

        return string.Join("  ·  ", parts);
    }

    private async Task<string> OrganizationNameAsync()
    {
        var organizationId = _currentUser.OrganizationId;
        if (string.IsNullOrEmpty(organizationId)) return "Organization";
        return (await _organizations.GetByIdAsync(organizationId))?.Name ?? "Organization";
    }

    public TabularExportResult BuildImportTemplate(TabularFormat format) =>
        TabularExportResult.From(
            ClaimsSummarySheet.BuildImportTemplate(), format, "claims-import-template");

    // Bulk-import historical claims — a migration path off another system, not a
    // second way to file a claim. So, deliberately unlike CreateAsync:
    //
    //   - the row's Amount is TRUSTED as given, never recomputed from a mileage
    //     rate (the money already moved; re-deriving it would rewrite history),
    //   - the account spend-limit and mileage-account rules are NOT enforced
    //     (they gate what an employee may submit today, not what happened),
    //   - the row's Status is honoured, so settled claims import as APPROVED
    //     instead of landing in somebody's approval queue,
    //   - nothing is ever updated or deleted, so a re-upload is safe.
    public async Task<TabularImportResult> ImportAsync(byte[] content, TabularFormat format)
    {
        IReadOnlyList<IReadOnlyList<string>> rows;
        try
        {
            rows = TabularReader.Read(content, format);
        }
        catch (InvalidDataException ex)
        {
            return TabularImportResult.FileError(ex.Message);
        }

        if (rows.Count == 0) return TabularImportResult.FileError("The file is empty.");

        var columns = ClaimsSummarySheet.ImportColumns;
        var (map, missing) = TabularHeaderMap.Build(
            rows[0], columns, EmployeeImportColumns.IdentityGroup);
        if (map is null)
            return TabularImportResult.FileError($"Missing required column(s): {string.Join(", ", missing)}.");
        if (rows.Count == 1)
            return TabularImportResult.FileError("The file has a header row but no data rows.");

        var result = new TabularImportResult();
        var employees = await _employees.GetSnapshotAsync();
        var accountIdsByCode = (await _accounts.GetAllAsync())
            .GroupBy(a => a.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var existing = await _repo.GetAllAsync();
        var seenNumbers = existing
            .Select(c => c.ClaimNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenKeys = existing
            .Select(DedupeKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        var imported = 0;

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 1;   // 1-based, header included — what the admin sees

            if (TabularTemplate.IsExampleRow(map, row, columns))
            {
                result.CountSkipped();
                continue;
            }

            var email = map.Cell(row, "employeeEmail");
            var name = map.Cell(row, "employeeName");
            var (employeeId, ambiguous) = employees.Resolve(email, name);
            if (ambiguous)
            {
                result.Fail(rowNumber, $"More than one employee is named '{name}'. Use the Employee Email column.");
                continue;
            }
            if (employeeId is null)
            {
                result.Fail(rowNumber, $"No employee in this organization matches '{(email.Length > 0 ? email : name)}'.");
                continue;
            }

            var title = TabularCell.Text(map.Cell(row, "title"), 200);
            if (title is null)
            {
                result.Fail(rowNumber, "Title is required.");
                continue;
            }

            var category = TabularCell.Enum<ClaimCategory>(map.Cell(row, "category"));
            if (category is null)
            {
                result.Fail(rowNumber,
                    $"Category must be one of: {string.Join(", ", Enum.GetNames<ClaimCategory>())}.");
                continue;
            }

            var amount = TabularCell.Money(map.Cell(row, "amount"));
            if (amount is null || amount <= 0)
            {
                result.Fail(rowNumber, "Amount must be a number greater than zero.");
                continue;
            }

            var spentOn = TabularCell.Date(map.Cell(row, "spentOn"));
            if (spentOn is null)
            {
                result.Fail(rowNumber, "Spent On must be a date, e.g. 2026-01-15.");
                continue;
            }

            var statusCell = map.Cell(row, "status");
            // Blank means "this already happened and was settled" — the whole
            // point of a history import. A typo, though, must not silently
            // become APPROVED.
            var status = TabularCell.IsBlank(statusCell)
                ? ClaimStatus.APPROVED
                : TabularCell.Enum<ClaimStatus>(statusCell);
            if (status is null)
            {
                result.Fail(rowNumber,
                    $"Status must be one of: {string.Join(", ", Enum.GetNames<ClaimStatus>())}.");
                continue;
            }

            var claimType = TabularCell.Enum<ClaimType>(map.Cell(row, "claimType")) ?? ClaimType.EXPENSE;
            var paymentType = TabularCell.Enum<PaymentType>(map.Cell(row, "paymentType")) ?? PaymentType.PERSONAL;

            string? chartOfAccountId = null;
            var accountCode = map.Cell(row, "accountCode");
            if (!TabularCell.IsBlank(accountCode))
            {
                if (!accountIdsByCode.TryGetValue(accountCode.Trim(), out var accountId))
                {
                    result.Fail(rowNumber, $"No chart of account has the code '{accountCode.Trim()}'.");
                    continue;
                }
                chartOfAccountId = accountId;
            }

            var suppliedNumber = TabularCell.Text(map.Cell(row, "claimNumber"), 40);

            // Two dedupe routes. An explicit Claim # is exact, so honour it; with
            // none, fall back to "same person, same day, same amount, same
            // title" — which is what a duplicated spreadsheet row looks like.
            if (suppliedNumber is not null && seenNumbers.Contains(suppliedNumber))
            {
                result.CountSkipped();
                continue;
            }

            var key = DedupeKey(employeeId, spentOn.Value, amount.Value, title);
            if (suppliedNumber is null && seenKeys.Contains(key))
            {
                result.CountSkipped();
                continue;
            }

            var claim = new Claim
            {
                ClaimNumber = suppliedNumber ?? GenerateClaimNumber(),
                Title = title,
                Description = TabularCell.Text(map.Cell(row, "description")) ?? string.Empty,
                Category = category.Value,
                Amount = decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero),
                Currency = (TabularCell.Text(map.Cell(row, "currency"), 3) ?? "MYR").ToUpperInvariant(),
                SpentAt = spentOn.Value,
                SubmittedAt = spentOn.Value,
                Status = status.Value,
                // Imported rows are historical: parking them at step 0 while
                // APPROVED is harmless (the chain is only read while PENDING),
                // and an imported PENDING row correctly starts at the first step.
                CurrentStep = 0,
                ClaimType = claimType,
                PaymentType = paymentType,
                EmployeeId = employeeId,
                ChartOfAccountId = chartOfAccountId,
                ReviewNotes = TabularCell.Text(map.Cell(row, "reviewNotes")),
                CreatedAt = now,
                UpdatedAt = now,
            };

            await _repo.AddAsync(claim);

            seenNumbers.Add(claim.ClaimNumber);
            seenKeys.Add(key);
            result.CountImported();
            imported++;
        }

        if (imported > 0) await NotifyImportAsync(employees);
        return result;
    }

    private static bool Matches(Claim claim, ClaimsExportQueryDto query, bool bySubmitted)
    {
        var when = (bySubmitted ? claim.SubmittedAt : claim.SpentAt).Date;
        if (query.From is { } from && when < from.Date) return false;
        if (query.To is { } to && when > to.Date) return false;
        if (query.Status is { } status && claim.Status != status) return false;
        if (query.PaymentType is { } paymentType && claim.PaymentType != paymentType) return false;
        if (!string.IsNullOrWhiteSpace(query.EmployeeId) && claim.EmployeeId != query.EmployeeId) return false;
        if (!string.IsNullOrWhiteSpace(query.ProjectId) && claim.ProjectId != query.ProjectId) return false;
        return true;
    }

    // The range goes in the filename so a downloads folder full of these stays
    // readable instead of being claims-summary(3).xlsx.
    private static string ExportFileName(ClaimsExportQueryDto query)
    {
        if (query.From is { } from && query.To is { } to)
            return $"claims-summary-{from:yyyy-MM-dd}-to-{to:yyyy-MM-dd}";
        return $"claims-summary-{DateTime.UtcNow:yyyy-MM-dd}";
    }

    private static string DedupeKey(Claim claim) =>
        DedupeKey(claim.EmployeeId, claim.SpentAt, claim.Amount, claim.Title);

    private static string DedupeKey(string employeeId, DateTime spentAt, decimal amount, string title) =>
        string.Join('|', employeeId, spentAt.ToString("yyyy-MM-dd"), amount.ToString("0.00"), title.Trim());

    // One nudge for the whole import, not one per row: a 500-row migration
    // shouldn't push 500 events at everyone's browser. Every member is a target
    // because an import can touch anybody's list — the client re-reads through
    // /claims, so nobody learns anything they couldn't already see.
    private async Task NotifyImportAsync(EmployeeRowIndex employees)
    {
        var organizationId = _currentUser.OrganizationId;
        if (string.IsNullOrEmpty(organizationId)) return;

        await _realtime.PublishAsync(
            organizationId,
            employees.Members.Select(m => (string?)m.Id),
            RealtimeEventDto.For(RealtimeScope.CLAIMS, RealtimeAction.SUBMITTED));
    }

    // Live nudge for one claim. Targets are derived from the claim's CURRENT
    // state: whoever must act on it now (empty once it's approved or rejected)
    // plus, optionally, the person who filed it.
    //
    // Note this pushes only an id and a scope, never the claim — the client
    // re-reads through /claims, which already enforces who may see what.
    //
    // `notifyApprovers` forces the approver fan-out for the terminal states
    // (rejected, deleted), where the PENDING check would otherwise conclude
    // there is nobody left to tell — but a peer approver at the same step still
    // needs the row to leave their queue.
    private async Task NotifyAsync(
        Claim claim, RealtimeAction action, bool notifyClaimant, bool notifyApprovers = false)
    {
        var targets = new List<string?>();
        if (notifyClaimant) targets.Add(claim.EmployeeId);

        if (notifyApprovers || claim.Status == ClaimStatus.PENDING)
            targets.AddRange(await _router.CurrentApproversAsync(Module, claim.EmployeeId, claim.CurrentStep));

        await _realtime.PublishAsync(
            claim.OrganizationId,
            targets,
            RealtimeEventDto.For(RealtimeScope.CLAIMS, action, claim.Id));
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

    public async Task<IReadOnlyList<Claim>> GetAllForOrgAsync() => await _repo.GetAllAsync();
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
