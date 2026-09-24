using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Accounts.Entities;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Modules.Xero.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.Xero;

public class XeroService : IXeroService
{
    private readonly ICurrentUser _currentUser;
    private readonly IXeroRepository _repo;
    private readonly IAuditService _audit;
    private readonly ILogger<XeroService> _logger;
    private readonly IXeroClient _client;
    private readonly IDataProtector _protector;
    private readonly XeroOptions _options;

    public XeroService(
        ICurrentUser currentUser,
        IXeroRepository repo,
        IXeroClient client,
        IDataProtectionProvider dataProtection,
        IOptions<XeroOptions> options,
        IAuditService audit,
        // Optional so the many test call sites need not thread one through;
        // DI always supplies the real logger.
        ILogger<XeroService>? logger = null)
    {
        _currentUser = currentUser;
        _repo = repo;
        _client = client;
        _protector = dataProtection.CreateProtector("AltomateHR.XeroTokens.v1");
        _options = options.Value;
        _audit = audit;
        _logger = logger ?? NullLogger<XeroService>.Instance;
    }

    public async Task<XeroConnectUrlDto> CreateConnectUrlAsync(string? returnUrl)
    {
        var orgId = RequireOrganization();
        var userId = _currentUser.UserId ?? throw new XeroConnectionException("Current user is missing.");
        var stateValue = CreateStateValue();
        var now = DateTime.UtcNow;

        await _repo.AddStateAsync(new XeroOAuthState
        {
            OrganizationId = orgId,
            UserId = userId,
            State = stateValue,
            ReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? null : returnUrl,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(15),
        });

        return new XeroConnectUrlDto { Url = _client.BuildAuthorizationUrl(stateValue) };
    }

    public async Task<string> CompleteCallbackAsync(string code, string state)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return WithOutcome(_options.FailureRedirectUrl, "failed");

        var storedState = await _repo.GetStateAsync(state);
        if (storedState is null || storedState.UsedAt is not null || storedState.ExpiresAt < DateTime.UtcNow)
            return WithOutcome(_options.FailureRedirectUrl, "failed");

        var token = await _client.ExchangeCodeAsync(code);
        var tenants = await _client.GetTenantsAsync(token.AccessToken);

        // Which org this sign-in connects — see XeroTenantChoice. It used to be
        // tenants[0], i.e. whichever org the app was connected to FIRST, so an
        // admin who chose their company's Xero got another company's instead.
        var choice = XeroTenantChoice.Choose(
            tenants,
            XeroTenantChoice.AuthEventIdOf(token.AccessToken),
            await _repo.GetTenantIdsConnectedElsewhereAsync(
                tenants.Select(t => t.TenantId), storedState.OrganizationId));

        var now = DateTime.UtcNow;

        if (choice.Tenant is null)
        {
            // Spent either way: the same code can't be replayed into a
            // different outcome.
            storedState.UsedAt = now;
            await _repo.UpdateStateAsync(storedState);
            return WithRefusal(_options.FailureRedirectUrl, choice);
        }

        var tenant = choice.Tenant;

        // Read before the upsert, because the upsert is what clears
        // DisconnectedAt. An org that was already connected is re-authorising,
        // not connecting — and must not have its projects re-archived, since an
        // admin may have deliberately restored one in between.
        var previous = await _repo.GetConnectionAsync(storedState.OrganizationId);
        var firstConnect = previous is null || !previous.IsConnected;

        await _repo.UpsertConnectionAsync(new XeroConnection
        {
            OrganizationId = storedState.OrganizationId,
            ConnectionId = string.IsNullOrWhiteSpace(tenant.Id) ? null : tenant.Id,
            TenantId = tenant.TenantId,
            TenantName = tenant.TenantName,
            TenantType = tenant.TenantType,
            TokenType = token.TokenType,
            Scope = token.Scope,
            AccessTokenProtected = _protector.Protect(token.AccessToken),
            RefreshTokenProtected = _protector.Protect(token.RefreshToken),
            AccessTokenExpiresAt = now.AddSeconds(Math.Max(60, token.ExpiresIn - 60)),
            ConnectedAt = now,
            UpdatedAt = now,
        });

        // Xero is now the source of truth for which projects exist, so the ones
        // typed in by hand before it was connected drop out of every picker —
        // otherwise attendance and company structure keep offering projects no
        // claim, bill or timesheet can ever be costed against. Reversed on
        // disconnect.
        if (firstConnect)
            await _repo.ArchiveManualProjectsAsync(storedState.OrganizationId);

        storedState.UsedAt = now;
        await _repo.UpdateStateAsync(storedState);

        // The OAuth callback runs BEFORE the app has a session for this request,
        // so the org comes from the state row rather than from ICurrentUser —
        // otherwise the one event proving who connected the accounting system
        // would be the one event that never gets written.
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.XeroConnect,
            tenant.TenantName,
            TargetType: "XeroConnection",
            TargetId: tenant.TenantId,
            Metadata: new { tenant.TenantName, tenant.TenantType },
            OrganizationId: storedState.OrganizationId));

        // The frontend has no router, so the return URL is only ever the app
        // root — the marker is what actually gets the admin back to the Xero
        // card, and tells them it worked. Without it a successful connect just
        // drops them on the dashboard with nothing said.
        return WithOutcome(storedState.ReturnUrl ?? _options.SuccessRedirectUrl, "connected");
    }

    // Appends ?xero=connected / ?xero=failed, respecting whatever query the
    // configured URL already carries.
    private static string WithOutcome(string url, string outcome) =>
        url.Contains('?') ? $"{url}&xero={outcome}" : $"{url}?xero={outcome}";

    // A refused connect says WHY, so the card can tell the admin what to do
    // instead of "the sign-in was cancelled".
    private static string WithRefusal(string url, XeroTenantChoice.Result choice)
    {
        var reason = choice.Refusal switch
        {
            XeroTenantChoice.Refusal.InUseElsewhere => "in-use",
            XeroTenantChoice.Refusal.SeveralAuthorised => "several",
            _ => "none",
        };
        var withReason = $"{WithOutcome(url, "failed")}&xeroReason={reason}";
        return choice.TenantName is null
            ? withReason
            : $"{withReason}&xeroOrg={Uri.EscapeDataString(choice.TenantName)}";
    }

    public async Task<XeroStatusDto> GetStatusAsync()
    {
        var connection = await GetCurrentConnectionAsync();
        if (connection is null || !connection.IsConnected)
            return new XeroStatusDto { Connected = false };

        return new XeroStatusDto
        {
            Connected = true,
            TenantId = connection.TenantId,
            TenantName = connection.TenantName,
            ConnectedAt = connection.ConnectedAt,
            UpdatedAt = connection.UpdatedAt,
            AccessTokenExpiresAt = connection.AccessTokenExpiresAt,
            // Both ways a connection dies, reported the same way and without
            // calling Xero: the flag a failed refresh left behind, or tokens that
            // no longer decrypt. Either way the tenant name above still shows, so
            // the admin knows which Xero org to reconnect.
            NeedsReconnect = connection.NeedsReconnect || !CanDecryptTokens(connection),
        };
    }

    public async Task DisconnectAsync()
    {
        var connection = await GetCurrentConnectionAsync();
        if (connection is null) return;

        connection.DisconnectedAt = DateTime.UtcNow;
        connection.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateConnectionAsync(connection);

        // Hand-created projects come back — without Xero there is nothing else
        // to pick, and an org with no selectable project can't clock in at all.
        await _repo.RestoreProjectsArchivedByXeroConnectAsync(connection.OrganizationId);

        // Disconnecting stops every approved claim reaching the accounting
        // system. Worth a name and a timestamp.
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.XeroDisconnect,
            connection.TenantName,
            TargetType: "XeroConnection",
            TargetId: connection.TenantId));
    }

    // The currencies this org may file claims in. Empty when Xero is not
    // connected — the caller decides what that means rather than being handed a
    // guess, because "we could not ask" and "Xero allows nothing" are very
    // different situations.
    public async Task<IReadOnlyList<XeroCurrencyResponse>> GetCurrenciesAsync()
    {
        var connection = await GetCurrentConnectionAsync();
        if (connection is null || !connection.IsConnected) return [];

        var accessToken = await GetValidAccessTokenAsync(connection);
        return await _client.GetCurrenciesAsync(accessToken, connection.TenantId);
    }

    public async Task<XeroSyncAccountsResultDto> SyncAccountsAsync()
    {
        var orgId = RequireOrganization();
        var connection = await GetCurrentConnectionAsync()
            ?? throw new XeroConnectionException("Connect Xero before syncing accounts.");

        if (!connection.IsConnected)
            throw new XeroConnectionException("Xero is disconnected.");

        var accessToken = await GetValidAccessTokenAsync(connection);
        var xeroAccounts = await _client.GetAccountsAsync(accessToken, connection.TenantId);
        var result = new XeroSyncAccountsResultDto();
        var now = DateTime.UtcNow;

        foreach (var xeroAccount in xeroAccounts)
        {
            if (!ShouldImportAccount(xeroAccount))
            {
                // Not claimable — but an earlier, unfiltered sync may already
                // have imported it. Skipping alone would leave "Sales" and
                // "Retained Earnings" sitting selectable in the claim form
                // forever, so a row that exists is corrected rather than left.
                var stale = await _repo.GetAccountByXeroIdAsync(orgId, xeroAccount.AccountId);
                if (stale is not null)
                {
                    stale.IsSelectable = false;
                    stale.IsArchived = true;
                    stale.XeroSyncedAt = now;
                    await _repo.UpdateAccountAsync(stale);
                }

                result.Skipped++;
                continue;
            }

            var existing = await _repo.GetAccountByXeroIdAsync(orgId, xeroAccount.AccountId);
            if (existing is null)
            {
                await _repo.AddAccountAsync(new ChartOfAccount
                {
                    OrganizationId = orgId,
                    Code = xeroAccount.Code,
                    Name = xeroAccount.Name,
                    Type = ToLocalAccountType(xeroAccount.Type),
                    XeroAccountId = xeroAccount.AccountId,
                    XeroStatus = xeroAccount.Status,
                    XeroSyncedAt = now,
                    IsSelectable = IsActive(xeroAccount.Status) && IsClaimable(xeroAccount.Type),
                    CreatedAt = now,
                });
                result.Imported++;
                continue;
            }

            existing.Code = xeroAccount.Code;
            existing.Name = xeroAccount.Name;
            existing.Type = ToLocalAccountType(xeroAccount.Type);
            existing.XeroStatus = xeroAccount.Status;
            existing.XeroSyncedAt = now;
            existing.IsArchived = !IsActive(xeroAccount.Status);
            // A bank account that was somehow selectable stops being so.
            if (!IsClaimable(xeroAccount.Type)) existing.IsSelectable = false;
            await _repo.UpdateAccountAsync(existing);
            result.Updated++;
        }

        // A sync rewrites the chart of accounts every claim is coded against,
        // so the counts are worth keeping even though nothing failed.
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.XeroSyncAccounts,
            $"{result.Imported} imported · {result.Updated} updated · {result.Skipped} skipped",
            TargetType: "XeroConnection",
            TargetId: connection.TenantId,
            Metadata: new { result.Imported, result.Updated, result.Skipped }));

        return result;
    }

    public async Task<XeroSyncProjectsResultDto> SyncProjectsAsync()
    {
        var orgId = RequireOrganization();
        var connection = await GetCurrentConnectionAsync()
            ?? throw new XeroConnectionException("Connect Xero before syncing projects.");

        if (!connection.IsConnected)
            throw new XeroConnectionException("Xero is disconnected.");

        var accessToken = await GetValidAccessTokenAsync(connection);
        var xeroProjects = await _client.GetProjectsAsync(accessToken, connection.TenantId);
        var result = new XeroSyncProjectsResultDto();
        var now = DateTime.UtcNow;

        foreach (var xeroProject in xeroProjects)
        {
            if (!ShouldImportProject(xeroProject))
            {
                result.Skipped++;
                continue;
            }

            var existing = await _repo.GetProjectByXeroIdAsync(orgId, xeroProject.ProjectId);
            if (existing is null)
            {
                await _repo.AddProjectAsync(new Project
                {
                    OrganizationId = orgId,
                    Name = xeroProject.Name,
                    XeroProjectId = xeroProject.ProjectId,
                    XeroStatus = xeroProject.Status,
                    XeroSyncedAt = now,
                    IsArchived = IsClosedProject(xeroProject.Status),
                    CreatedAt = now,
                });
                result.Imported++;
                continue;
            }

            existing.Name = xeroProject.Name;
            existing.XeroStatus = xeroProject.Status;
            existing.XeroSyncedAt = now;
            existing.IsArchived = IsClosedProject(xeroProject.Status);
            await _repo.UpdateProjectAsync(existing);
            result.Updated++;
        }

        // Most Xero orgs don't use the Projects product at all — they model
        // projects as options on a tracking category, which is what the
        // previous system syncs and what the loop above can never see. Without
        // this the sync reports a truthful "0 added, 0 updated" against a Xero
        // that plainly has projects in it.
        //
        // A fallback, not a second pass: an org that DOES use Xero Projects
        // very likely also has tracking categories for regions or cost
        // centres, and importing those as projects would be worse than the
        // problem this solves.
        if (result.Imported + result.Updated == 0)
            await SyncTrackingOptionProjectsAsync(orgId, connection, accessToken, now, result);

        // Orgs that connected Xero before the archive-on-connect rule existed
        // never had their hand-typed projects hidden, and an admin who connects
        // and only syncs later gets here with them still showing. Either way,
        // once Xero has actually supplied projects it owns the list.
        //
        // Guarded on the sync having produced something: a Xero org with no
        // projects would otherwise leave the org with nothing selectable at
        // all, and nobody able to clock in.
        if (result.Imported + result.Updated > 0)
            await _repo.ArchiveManualProjectsAsync(orgId);

        return result;
    }

    // Each ACTIVE option on the org's project tracking category becomes a
    // project, keyed on the option id so a rename in Xero renames it here
    // rather than creating a second one.
    private async Task SyncTrackingOptionProjectsAsync(
        string orgId,
        XeroConnection connection,
        string accessToken,
        DateTime now,
        XeroSyncProjectsResultDto result)
    {
        List<XeroTrackingCategoryResponse> categories;
        try
        {
            categories = await _client.GetTrackingCategoriesAsync(accessToken, connection.TenantId);
        }
        catch (XeroConnectionException)
        {
            // An org without the accounting.settings scope, or a Xero fault.
            // The Projects-API half of this sync already succeeded; failing the
            // whole call now would throw that away.
            return;
        }

        var active = categories
            .Where(c => string.Equals(c.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (active.Count == 0) return;

        var category = active.FirstOrDefault(c =>
            string.Equals(c.TrackingCategoryId, connection.ProjectTrackingCategoryId, StringComparison.Ordinal));

        if (category is null)
        {
            // One category is not a choice worth asking about; two is.
            if (active.Count > 1)
            {
                result.NeedsTrackingCategoryChoice = true;
                return;
            }

            category = active[0];
            connection.ProjectTrackingCategoryId = category.TrackingCategoryId;
            connection.ProjectTrackingCategoryName = category.Name;
            await _repo.UpdateConnectionAsync(connection);
        }
        else if (!string.Equals(connection.ProjectTrackingCategoryName, category.Name, StringComparison.Ordinal))
        {
            connection.ProjectTrackingCategoryName = category.Name;
            await _repo.UpdateConnectionAsync(connection);
        }

        result.TrackingCategoryName = category.Name;

        foreach (var option in category.Options)
        {
            if (string.IsNullOrWhiteSpace(option.Name))
            {
                result.Skipped++;
                continue;
            }

            var existing = await _repo.GetProjectByTrackingOptionAsync(orgId, option.TrackingOptionId);
            var archived = !string.Equals(option.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);

            if (existing is null)
            {
                await _repo.AddProjectAsync(new Project
                {
                    OrganizationId = orgId,
                    Name = option.Name,
                    XeroTrackingOptionId = option.TrackingOptionId,
                    XeroTrackingCategoryId = category.TrackingCategoryId,
                    XeroStatus = option.Status,
                    XeroSyncedAt = now,
                    IsArchived = archived,
                    CreatedAt = now,
                });
                result.Imported++;
                continue;
            }

            existing.Name = option.Name;
            existing.XeroTrackingCategoryId = category.TrackingCategoryId;
            existing.XeroStatus = option.Status;
            existing.XeroSyncedAt = now;
            existing.IsArchived = archived;
            await _repo.UpdateProjectAsync(existing);
            result.Updated++;
        }
    }

    public async Task<XeroProjectTrackingDto> GetProjectTrackingAsync()
    {
        var connection = await GetCurrentConnectionAsync();
        if (connection is null || !connection.IsConnected) return new XeroProjectTrackingDto();

        var accessToken = await GetValidAccessTokenAsync(connection);
        var categories = await _client.GetTrackingCategoriesAsync(accessToken, connection.TenantId);

        return new XeroProjectTrackingDto
        {
            SelectedCategoryId = connection.ProjectTrackingCategoryId,
            Categories =
            [
                .. categories
                    .Where(c => string.Equals(c.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                    .Select(c => new XeroTrackingCategoryDto
                    {
                        Id = c.TrackingCategoryId,
                        Name = c.Name,
                        OptionCount = c.Options.Count,
                    })
            ],
        };
    }

    public async Task<XeroSyncProjectsResultDto?> SetProjectTrackingCategoryAsync(string? categoryId)
    {
        var connection = await GetCurrentConnectionAsync()
            ?? throw new XeroConnectionException("Connect Xero before choosing a tracking category.");

        if (string.IsNullOrWhiteSpace(categoryId))
        {
            connection.ProjectTrackingCategoryId = null;
            connection.ProjectTrackingCategoryName = null;
            await _repo.UpdateConnectionAsync(connection);
            return null;
        }

        var accessToken = await GetValidAccessTokenAsync(connection);
        var categories = await _client.GetTrackingCategoriesAsync(accessToken, connection.TenantId);
        var category = categories.FirstOrDefault(c =>
            string.Equals(c.TrackingCategoryId, categoryId, StringComparison.Ordinal))
            ?? throw new XeroConnectionException("That tracking category no longer exists in Xero.");

        var previous = connection.ProjectTrackingCategoryName;
        connection.ProjectTrackingCategoryId = category.TrackingCategoryId;
        connection.ProjectTrackingCategoryName = category.Name;
        await _repo.UpdateConnectionAsync(connection);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.XeroProjectTrackingSet,
            previous is null || previous == category.Name
                ? $"Projects now come from the Xero tracking category \"{category.Name}\""
                : $"Switched the project tracking category from \"{previous}\" to \"{category.Name}\"",
            TargetType: "XeroConnection",
            TargetId: connection.Id,
            Metadata: new { Category = new { From = previous, To = category.Name } }));

        // Pulls the new category's options in now. Projects from the previous
        // category are NOT touched — they stop being offered (see
        // IProjectTrackingScope) and come back if the admin switches back.
        return await SyncProjectsAsync();
    }

    private async Task<string> GetValidAccessTokenAsync(XeroConnection connection)
    {
        string refreshToken;
        try
        {
            if (connection.AccessTokenExpiresAt > DateTime.UtcNow.AddMinutes(2))
                return _protector.Unprotect(connection.AccessTokenProtected);

            refreshToken = _protector.Unprotect(connection.RefreshTokenProtected);
        }
        catch (CryptographicException)
        {
            // Decrypting can only fail because the key ring that wrote these
            // tokens is gone, which makes the ciphertext in the DB permanently
            // unreadable. Same outcome as a revoked token, so same handling.
            await MarkReconnectRequiredAsync(
                connection, "The data-protection key ring that encrypted the stored tokens is gone.");
            throw new XeroReconnectRequiredException(
                "The stored Xero tokens can no longer be decrypted, so this connection is dead. " +
                "Reconnect Xero to continue.");
        }

        XeroTokenResponse refreshed;
        try
        {
            refreshed = await _client.RefreshTokenAsync(refreshToken);
        }
        catch (XeroConnectionException ex) when (ex.StatusCode is 400 or 401)
        {
            // OAuth's invalid_grant: Xero has revoked the refresh token, or it
            // lapsed after 60 days idle. No retry recovers it, and leaving the
            // connection unflagged means every later call fails the same way.
            await MarkReconnectRequiredAsync(
                connection, "Xero expired or revoked the refresh token.");
            throw new XeroReconnectRequiredException(
                $"Xero has expired or revoked this connection. Reconnect Xero to continue. {ex.Message}");
        }

        var now = DateTime.UtcNow;

        connection.AccessTokenProtected = _protector.Protect(refreshed.AccessToken);
        connection.RefreshTokenProtected = _protector.Protect(refreshed.RefreshToken);
        connection.AccessTokenExpiresAt = now.AddSeconds(Math.Max(60, refreshed.ExpiresIn - 60));
        connection.TokenType = refreshed.TokenType;
        connection.Scope = refreshed.Scope;
        connection.UpdatedAt = now;
        connection.ReconnectRequiredAt = null;
        await _repo.UpdateConnectionAsync(connection);

        return refreshed.AccessToken;
    }

    public async Task<XeroFileContent?> GetFileContentAsync(string fileId)
    {
        var connection = await GetCurrentConnectionAsync();
        if (connection is null || !connection.IsConnected) return null;

        var accessToken = await GetValidAccessTokenAsync(connection);
        return await _client.GetFileContentAsync(accessToken, connection.TenantId, fileId);
    }

    public async Task<XeroUploadedFile?> TryUploadFileAsync(
        string folderName, byte[] content, string fileName, string contentType)
    {
        try
        {
            var connection = await GetCurrentConnectionAsync();
            if (connection is null || !connection.IsConnected) return null;

            var accessToken = await GetValidAccessTokenAsync(connection);

            // Null folder is fine — the file lands in the tenant's inbox rather
            // than nowhere, which is better than refusing the upload over a
            // missing folder.
            var folderId = await _client.EnsureFolderAsync(accessToken, connection.TenantId, folderName);

            return await _client.UploadFileAsync(
                accessToken, connection.TenantId, folderId, content, fileName, contentType);
        }
        catch (Exception ex)
        {
            // Swallowed on purpose, and logged so this is visible as a Xero
            // problem rather than as files quietly landing on disk forever.
            // An expired connection, a tenant that never granted the `files`
            // scope, a network fault — none of them should block a submission.
            _logger.LogWarning(ex,
                "Xero file upload failed for {Folder}; falling back to local storage.", folderName);
            return null;
        }
    }

    public async Task<XeroBillResponse> CreateBillAsync(XeroBillRequest bill)
    {
        var connection = await GetCurrentConnectionAsync();
        if (connection is null || !connection.IsConnected)
            throw new XeroConnectionException("This organization isn't connected to Xero.");

        var accessToken = await GetValidAccessTokenAsync(connection);
        var tracking = await ProjectTrackingAsync(connection, accessToken, bill.ProjectTrackingOptionId);
        if (tracking is not null)
            bill = bill with { Lines = [.. bill.Lines.Select(l => l with { Tracking = tracking })] };

        return await _client.CreateBillAsync(accessToken, connection.TenantId, bill);
    }

    // The tracking tag for a claim's project: the CURRENT project category and
    // the option's CURRENT name, both read live from Xero — the bill API names
    // tracking by name, not id, so a name cached at the last sync could be
    // stale and get the bill refused.
    //
    // Never fatal. No project, a project from another category (the admin has
    // since switched), a category that's gone, or Xero not answering: the bill
    // still posts, just untagged. Losing a reporting dimension beats losing the
    // reimbursement.
    private async Task<IReadOnlyList<XeroTrackingRef>?> ProjectTrackingAsync(
        XeroConnection connection, string accessToken, string? optionId)
    {
        if (string.IsNullOrEmpty(optionId) || string.IsNullOrEmpty(connection.ProjectTrackingCategoryId))
            return null;

        try
        {
            var category = (await _client.GetTrackingCategoriesAsync(accessToken, connection.TenantId))
                .FirstOrDefault(c => string.Equals(
                    c.TrackingCategoryId, connection.ProjectTrackingCategoryId, StringComparison.Ordinal));
            var option = category?.Options.FirstOrDefault(o =>
                string.Equals(o.TrackingOptionId, optionId, StringComparison.Ordinal));

            return category is null || option is null || string.IsNullOrWhiteSpace(option.Name)
                ? null
                : [new XeroTrackingRef(category.Name, option.Name)];
        }
        catch (XeroConnectionException ex)
        {
            _logger.LogWarning(ex, "Could not read tracking categories; posting without a project tag.");
            return null;
        }
    }

    public async Task<XeroSpendResponse> CreateSpendAsync(XeroSpendRequest spend)
    {
        var connection = await GetCurrentConnectionAsync();
        if (connection is null || !connection.IsConnected)
            throw new XeroConnectionException("This organization isn't connected to Xero.");

        var accessToken = await GetValidAccessTokenAsync(connection);
        var tracking = await ProjectTrackingAsync(connection, accessToken, spend.ProjectTrackingOptionId);
        if (tracking is not null)
            spend = spend with { Lines = [.. spend.Lines.Select(l => l with { Tracking = tracking })] };

        return await _client.CreateSpendAsync(accessToken, connection.TenantId, spend);
    }

    public async Task<XeroManualJournalResponse> CreateManualJournalAsync(
        XeroManualJournalRequest journal)
    {
        var connection = await GetCurrentConnectionAsync();
        if (connection is null || !connection.IsConnected)
            throw new XeroConnectionException("This organization isn't connected to Xero.");

        var accessToken = await GetValidAccessTokenAsync(connection);
        return await _client.CreateManualJournalAsync(accessToken, connection.TenantId, journal);
    }

    public async Task<IReadOnlyList<XeroTrackingCategoryResponse>> GetTrackingCategoriesAsync()
    {
        var connection = await GetCurrentConnectionAsync();
        if (connection is null || !connection.IsConnected)
            throw new XeroConnectionException("This organization isn't connected to Xero.");

        var accessToken = await GetValidAccessTokenAsync(connection);
        return await _client.GetTrackingCategoriesAsync(accessToken, connection.TenantId);
    }

    public async Task<bool> IsConnectedAsync()
    {
        var connection = await GetCurrentConnectionAsync();
        return connection is not null && connection.IsConnected;
    }

    private async Task<XeroConnection?> GetCurrentConnectionAsync() =>
        await _repo.GetConnectionAsync(RequireOrganization());

    private string RequireOrganization() =>
        _currentUser.OrganizationId ?? throw new XeroConnectionException("Current organization is missing.");

    private static string CreateStateValue()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }

    // Xero types a claim can be coded to — the four Xero groups under
    // "Expenses" in its own account-type picker: Depreciation, Direct Costs,
    // Expense, Overhead. DEPRECIATN is Xero's code for the first, and its
    // absence here meant a chart that files anything under Depreciation lost
    // those accounts on every sync.
    //
    // A chart of accounts also holds revenue, receivables, equity and
    // liabilities — none of which an employee can spend against, and all of
    // which would otherwise land in the claim form's account picker.
    private static readonly string[] ClaimableTypes =
        ["EXPENSE", "DIRECTCOSTS", "OVERHEADS", "DEPRECIATN"];

    // BANK is imported but NOT claimable: it is the account company-paid claims
    // are spent FROM (Claim.PayViaAccountId), never the account they are coded
    // to. Dropping it would break the company-spend path.
    private static bool IsBank(string type) =>
        string.Equals(type, "BANK", StringComparison.OrdinalIgnoreCase);

    private static bool IsClaimable(string type) =>
        ClaimableTypes.Contains(type, StringComparer.OrdinalIgnoreCase);

    private static bool ShouldImportAccount(XeroAccountResponse account)
    {
        var known = IsActive(account.Status)
            || string.Equals(account.Status, "ARCHIVED", StringComparison.OrdinalIgnoreCase);

        return known && (IsClaimable(account.Type) || IsBank(account.Type));
    }

    private static bool IsActive(string status) =>
        string.Equals(status, "ACTIVE", StringComparison.OrdinalIgnoreCase);

    // Only BANK and EXPENSE exist locally. Everything importable that is not a
    // bank is an expense family type, so it collapses to EXPENSE.
    private static string ToLocalAccountType(string xeroType) =>
        IsBank(xeroType) ? "BANK" : "EXPENSE";

    private static bool ShouldImportProject(XeroProjectResponse project) =>
        !string.Equals(project.Status, "DELETED", StringComparison.OrdinalIgnoreCase);

    private static bool IsClosedProject(string status) =>
        string.Equals(status, "CLOSED", StringComparison.OrdinalIgnoreCase);

    // Mark a connection unusable without touching the tokens: IsConnected keys
    // off DisconnectedAt, so /xero/status stops claiming to be connected and the
    // UI offers Connect again. Audited under xero.disconnect like a manual one —
    // the summary says it was Xero's doing, not an admin's.
    private async Task MarkReconnectRequiredAsync(XeroConnection connection, string reason)
    {
        if (connection.NeedsReconnect) return;   // already flagged; don't re-audit on every call

        connection.ReconnectRequiredAt = DateTime.UtcNow;
        connection.UpdatedAt = connection.ReconnectRequiredAt.Value;
        await _repo.UpdateConnectionAsync(connection);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.XeroReconnectRequired,
            connection.TenantName,
            TargetType: "XeroConnection",
            TargetId: connection.TenantId,
            Metadata: new { Reason = reason },
            OrganizationId: connection.OrganizationId));
    }

    private bool CanDecryptTokens(XeroConnection connection)
    {
        try
        {
            _protector.Unprotect(connection.RefreshTokenProtected);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
