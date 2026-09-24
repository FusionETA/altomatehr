using System.Text.Json;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Audit.Dtos;
using AltomateHR.Api.Modules.Audit.Entities;

namespace AltomateHR.Api.Modules.Audit;

// Writing and reading the per-org activity log.
public class AuditService : IAuditService
{
    private readonly IAuditRepository _repo;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AuditService> _logger;
    private readonly Employees.IDirectoryService _directory;

    public AuditService(
        IAuditRepository repo,
        ICurrentUser currentUser,
        ILogger<AuditService> logger,
        Employees.IDirectoryService directory)
    {
        _directory = directory;
        _repo = repo;
        _currentUser = currentUser;
        _logger = logger;
    }

    // Swallows everything. An audit log that can fail a save is worse than no
    // audit log: it turns an observability feature into a new way for the app to
    // break, and the first time it happens will be during an incident. Failures
    // go to the application log instead, where they are somebody's problem
    // without being the user's.
    public async Task WriteAsync(AuditEvent entry)
    {
        try
        {
            var organizationId = entry.OrganizationId ?? _currentUser.OrganizationId;
            if (string.IsNullOrWhiteSpace(organizationId))
            {
                _logger.LogWarning(
                    "Audit event {Action} dropped: no organization in context and none supplied.",
                    entry.Action);
                return;
            }

            await _repo.AppendAsync(
                new AuditLog
                {
                    ActorUserId = _currentUser.UserId,
                    ActorRole = _currentUser.Role,
                    // Copied, not joined — see AuditLog. Falls back to the id so
                    // a row always names someone.
                    ActorEmail = entry.ActorEmail ?? _currentUser.Email ?? "system@altomatehr",
                    // No display name on the token, so the address is the label.
                    // Better a real address than a GUID nobody can match to a
                    // person six months from now.
                    ActorName = entry.ActorName ?? _currentUser.Email ?? "System",
                    Action = entry.Action,
                    Status = entry.Status,
                    Summary = entry.Summary,
                    ErrorReason = entry.ErrorReason,
                    TargetType = entry.TargetType,
                    TargetId = entry.TargetId,
                    Metadata = entry.Metadata is null
                        ? null
                        : JsonSerializer.Serialize(entry.Metadata),
                    IpAddress = _currentUser.IpAddress,
                    // Set here, not by the database: the timestamp is hashed, so
                    // a value we never saw would make the row unverifiable. And
                    // truncated to what datetime(6) can hold, so the value that
                    // comes back out is the one that went in.
                    CreatedAt = AuditChain.TruncateToStorablePrecision(DateTime.UtcNow),
                },
                organizationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit event {Action}.", entry.Action);
        }
    }

    public async Task<AuditPageDto> ListAsync(AuditQueryDto query)
    {
        var organizationId = _currentUser.OrganizationId;
        if (string.IsNullOrWhiteSpace(organizationId)) return new AuditPageDto();

        var (rows, total) = await _repo.QueryAsync(query, organizationId);

        // Employee updates written before the summary named anyone carry the
        // user's GUID as their summary. The rows themselves are hash-chained
        // (AuditChain) and must never be rewritten, so the name is put in at
        // READ time instead — only for those, and only on this page's rows.
        var idsToName = rows
            .Where(r => IsEmployeeTarget(r) && r.Summary.Contains(r.TargetId!, StringComparison.Ordinal))
            .Select(r => r.TargetId!)
            .ToHashSet(StringComparer.Ordinal);

        var names = idsToName.Count == 0
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : (await _directory.GetUsersAsync())
                .Where(u => idsToName.Contains(u.Id))
                .ToDictionary(u => u.Id, u => PersonName.Display(u.Name, u.Email), StringComparer.Ordinal);

        return new AuditPageDto
        {
            Entries = rows.Select(r => ToDto(r, names)).ToList(),
            Total = total,
        };
    }

    private static bool IsEmployeeTarget(AuditLog row) =>
        string.Equals(row.TargetType, "Employee", StringComparison.Ordinal)
        && !string.IsNullOrEmpty(row.TargetId);

    // The summary as shown. A bare id reads as the sentence new entries use; an
    // id with more after it just has the id swapped for the name.
    private static string ReadableSummary(AuditLog row, IReadOnlyDictionary<string, string> names)
    {
        if (!IsEmployeeTarget(row) || !names.TryGetValue(row.TargetId!, out var name))
            return row.Summary;

        return row.Summary == row.TargetId
            ? $"Updated {name}'s details"
            : row.Summary.Replace(row.TargetId!, name, StringComparison.Ordinal);
    }

    public async Task<AuditVerificationDto> VerifyAsync()
    {
        var organizationId = _currentUser.OrganizationId;
        if (string.IsNullOrWhiteSpace(organizationId))
            return new AuditVerificationDto { Ok = true, EntriesChecked = 0 };

        var rows = await _repo.GetChainAsync(organizationId);
        var result = AuditChain.Verify(rows);

        return new AuditVerificationDto
        {
            Ok = result.Ok,
            EntriesChecked = result.EntriesChecked,
            HeadHash = result.HeadHash,
            // The line worth publishing outside the database. Without an
            // external anchor the chain only proves nobody edited it CARELESSLY.
            Head = result.Ok && result.HeadHash is not null && rows.Count > 0
                ? AuditChain.FormatHead(organizationId, rows[^1].Seq, result.HeadHash, rows[^1].CreatedAt)
                : null,
            BrokenAtSeq = result.BrokenAtSeq,
            Reason = result.Ok ? null : Describe(result.Reason),
        };
    }

    private static string Describe(AuditChainBreak reason) => reason switch
    {
        AuditChainBreak.SequenceGap =>
            "A row is missing from the middle of the log.",
        AuditChainBreak.PrevHashMismatch =>
            "A row's link to the one before it was rewritten.",
        AuditChainBreak.HashMismatch =>
            "A row's contents were edited after it was written.",
        AuditChainBreak.MissingChainColumns =>
            "A row has no hash, so it cannot be checked.",
        _ => "The log could not be verified.",
    };

    private static AuditLogDto ToDto(AuditLog row, IReadOnlyDictionary<string, string> names) => new()
    {
        Id = row.Id,
        Seq = row.Seq,
        ActorUserId = row.ActorUserId,
        ActorRole = row.ActorRole,
        ActorEmail = row.ActorEmail,
        ActorName = row.ActorName,
        Action = row.Action,
        Label = AuditActions.Humanize(row.Action),
        Status = row.Status,
        Summary = ReadableSummary(row, names),
        ErrorReason = row.ErrorReason,
        TargetType = row.TargetType,
        TargetId = row.TargetId,
        IpAddress = row.IpAddress,
        Metadata = row.Metadata,
        CreatedAt = row.CreatedAt,
    };
}
