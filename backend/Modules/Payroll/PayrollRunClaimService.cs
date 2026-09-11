using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class PayrollRunClaimService : IPayrollRunClaimService
{
    private readonly IPayrollRunClaimRepository _attachments;
    private readonly IPayrollRunRepository _runs;
    private readonly IClaimsService _claims;
    private readonly Employees.IDirectoryService _directory;
    private readonly IAuditService _audit;

    public PayrollRunClaimService(
        IPayrollRunClaimRepository attachments,
        IPayrollRunRepository runs,
        IClaimsService claims,
        Employees.IDirectoryService directory,
        IAuditService audit)
    {
        _attachments = attachments;
        _runs = runs;
        _claims = claims;
        _directory = directory;
        _audit = audit;
    }

    public async Task<List<PayrollRunClaimDto>> GetForRunAsync(string runId)
    {
        var rows = await _attachments.GetForRunAsync(runId);
        if (rows.Count == 0) return [];

        var people = await LoadPeopleAsync();

        // The claim numbers are read for display only; a claim deleted since
        // attach leaves the snapshot intact, which is the point of snapshotting.
        var claimNumbers = (await _claims.GetAllForOrgAsync())
            .ToDictionary(c => c.Id, c => c.ClaimNumber, StringComparer.Ordinal);

        return rows
            .Select(r =>
            {
                var person = people.ByProfileId.GetValueOrDefault(r.EmployeeProfileId);
                return new PayrollRunClaimDto
                {
                    Id = r.Id,
                    PayrollRunId = r.PayrollRunId,
                    ClaimId = r.ClaimId,
                    EmployeeProfileId = r.EmployeeProfileId,
                    Label = r.Label,
                    Amount = r.Amount,
                    EmployeeName = person?.Name ?? string.Empty,
                    EmployeeNumber = person?.EmployeeNumber,
                    ClaimNumber = claimNumbers.GetValueOrDefault(r.ClaimId) ?? string.Empty,
                    CreatedAt = r.CreatedAt,
                };
            })
            .OrderBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ThenBy(r => r.Label, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<List<AttachableClaimDto>> GetAttachableAsync()
    {
        var claims = await _claims.GetPayrollReimbursableAsync();
        if (claims.Count == 0) return [];

        var people = await LoadPeopleAsync();
        var attached = (await _attachments.GetAllAsync())
            .ToDictionary(a => a.ClaimId, StringComparer.Ordinal);

        // Only the runs actually referenced, so the period label costs one read
        // rather than one per claim.
        var runs = (await _runs.GetAllAsync()).ToDictionary(r => r.Id, StringComparer.Ordinal);

        return claims
            .Select(c =>
            {
                var person = people.ByUserId.GetValueOrDefault(c.EmployeeId);
                var existing = attached.GetValueOrDefault(c.Id);
                var run = existing is not null ? runs.GetValueOrDefault(existing.PayrollRunId) : null;

                return new AttachableClaimDto
                {
                    ClaimId = c.Id,
                    ClaimNumber = c.ClaimNumber,
                    Title = c.Title,
                    Category = c.Category,
                    ClaimType = c.ClaimType,
                    Amount = c.Amount,
                    SpentAt = c.SpentAt,
                    UserId = c.EmployeeId,
                    EmployeeProfileId = person?.ProfileId,
                    EmployeeName = person?.Name ?? string.Empty,
                    EmployeeNumber = person?.EmployeeNumber,
                    AttachedToRunId = existing?.PayrollRunId,
                    AttachedToRunPeriod = run is null
                        ? null
                        : PayrollPeriodLabel.For(run.PeriodYear, run.PeriodMonth),
                    BlockedReason = BlockedReasonFor(c, person),
                };
            })
            .OrderByDescending(c => c.SpentAt)
            .ThenBy(c => c.ClaimNumber, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<PayrollRunClaimAttachResult> AttachAsync(string runId, string claimId)
    {
        var run = await _runs.GetByIdAsync(runId);
        if (run is null) return new PayrollRunClaimAttachResult(false, false, null, null);

        if (run.Status != PayrollRunStatus.DRAFT)
        {
            return new PayrollRunClaimAttachResult(
                true, false, null,
                "This run is no longer a draft and cannot take new reimbursements.");
        }

        // The tenant filter is what scopes this: a claim id from another org
        // simply does not resolve.
        var claim = await _claims.GetByIdAsync(claimId);
        if (claim is null)
        {
            return new PayrollRunClaimAttachResult(
                true, false, null, "Claim not found in this organisation.");
        }

        var existing = await _attachments.GetByClaimIdAsync(claimId);
        if (existing is not null)
        {
            var holder = await _runs.GetByIdAsync(existing.PayrollRunId);
            var where = holder is null
                ? "another payroll run"
                : PayrollPeriodLabel.For(holder.PeriodYear, holder.PeriodMonth);

            return new PayrollRunClaimAttachResult(
                true, false, null,
                $"This claim is already attached to {where}. Detach it first.");
        }

        var people = await LoadPeopleAsync();
        var person = people.ByUserId.GetValueOrDefault(claim.EmployeeId);

        var blocked = BlockedReasonFor(claim, person);
        if (blocked is not null) return new PayrollRunClaimAttachResult(true, false, null, blocked);

        var attachment = await _attachments.AddAsync(new PayrollRunClaim
        {
            PayrollRunId = runId,
            ClaimId = claim.Id,
            EmployeeProfileId = person!.ProfileId,
            // Snapshotted here, on purpose. Editing the claim afterwards must
            // not move a figure on a run that has already been generated.
            Label = claim.Title,
            Amount = claim.Amount,
        });

        await _runs.MarkMutatedAsync(runId);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunClaimAttach,
            $"Attached claim {claim.ClaimNumber} to the "
            + $"{PayrollPeriodLabel.For(run.PeriodYear, run.PeriodMonth)} payroll run",
            TargetType: "PayrollRunClaim",
            TargetId: attachment.Id,
            Metadata: new
            {
                PayrollRunId = runId,
                ClaimId = claim.Id,
                claim.ClaimNumber,
                attachment.EmployeeProfileId,
                attachment.Amount,
            }));

        return new PayrollRunClaimAttachResult(true, true, new PayrollRunClaimDto
        {
            Id = attachment.Id,
            PayrollRunId = attachment.PayrollRunId,
            ClaimId = attachment.ClaimId,
            EmployeeProfileId = attachment.EmployeeProfileId,
            Label = attachment.Label,
            Amount = attachment.Amount,
            EmployeeName = person.Name,
            EmployeeNumber = person.EmployeeNumber,
            ClaimNumber = claim.ClaimNumber,
            CreatedAt = attachment.CreatedAt,
        }, null);
    }

    public async Task<PayrollRunClaimDetachResult> DetachAsync(string claimId)
    {
        var existing = await _attachments.GetByClaimIdAsync(claimId);
        if (existing is null) return new PayrollRunClaimDetachResult(false, false, null);

        var run = await _runs.GetByIdAsync(existing.PayrollRunId);

        // A submitted run's reimbursement has been paid. Taking it off would
        // leave the claim looking unpaid and free to attach somewhere else.
        if (run is not null && run.Status != PayrollRunStatus.DRAFT)
        {
            return new PayrollRunClaimDetachResult(
                true, false,
                "This run is no longer a draft — revert it before detaching reimbursements.");
        }

        await _attachments.DeleteByClaimIdAsync(claimId);

        if (run is not null) await _runs.MarkMutatedAsync(run.Id);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollRunClaimDetach,
            "Detached a claim from a payroll run",
            TargetType: "PayrollRun",
            TargetId: existing.PayrollRunId,
            Metadata: new
            {
                PayrollRunId = existing.PayrollRunId,
                ClaimId = claimId,
                existing.EmployeeProfileId,
                existing.Amount,
            }));

        return new PayrollRunClaimDetachResult(true, true, null);
    }

    // ─── Eligibility ────────────────────────────────────────────────────

    // Null means the claim can be attached. The reimbursable set already
    // guarantees APPROVED + PERSONAL + PAYROLL settlement, so what is left is
    // whether there is anyone on the payroll to pay it to, and the belt-and-
    // braces check that it has not already gone out through Xero.
    private static string? BlockedReasonFor(Claim claim, Person? person)
    {
        if (person is null)
        {
            return "The claim's submitter has no employee profile, so they cannot be paid through payroll.";
        }

        if (person.IsArchived)
        {
            return "The claim's submitter is archived and is not on this payroll.";
        }

        // A PAYROLL-settled claim never syncs to Xero, so this only fires when
        // the route was switched after the money had already gone out. Paying
        // it again through payroll would reimburse it twice.
        if (!string.IsNullOrWhiteSpace(claim.XeroBillId))
        {
            return "This claim has already been billed to Xero, so it cannot also be reimbursed through payroll.";
        }

        return null;
    }

    // ─── Roster lookup ──────────────────────────────────────────────────

    private sealed record Person(
        string ProfileId, string UserId, string Name, string? EmployeeNumber, bool IsArchived);

    private sealed record People(
        IReadOnlyDictionary<string, Person> ByUserId,
        IReadOnlyDictionary<string, Person> ByProfileId);

    private async Task<People> LoadPeopleAsync()
    {
        var profiles = await _directory.GetProfilesForCurrentOrgAsync();
        var users = (await _directory.GetUsersAsync())
            .ToDictionary(u => u.Id, StringComparer.Ordinal);
        var memberships = (await _directory.GetMembershipsForCurrentOrgAsync())
            .ToDictionary(m => m.UserId, StringComparer.Ordinal);

        var byUserId = new Dictionary<string, Person>(StringComparer.Ordinal);
        var byProfileId = new Dictionary<string, Person>(StringComparer.Ordinal);

        foreach (var profile in profiles)
        {
            var person = new Person(
                profile.Id,
                profile.UserId,
                users.TryGetValue(profile.UserId, out var user) ? user.Name : string.Empty,
                memberships.GetValueOrDefault(profile.UserId)?.EmployeeNumber,
                profile.IsArchived);

            byUserId[profile.UserId] = person;
            byProfileId[profile.Id] = person;
        }

        return new People(byUserId, byProfileId);
    }
}
