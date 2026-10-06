using System.Text.Json;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Policies;

namespace AltomateHR.Api.Modules.Employees;

// The monolith's payroll-transfer.service, on this app's model: a person is a
// User with a membership + profile per org, so a transfer archives the source
// org's profile and gives them a membership + profile at the target.
//
// Who may: an admin who is Admin/Owner in BOTH orgs (with the Employees module
// granted at the target). The source side is the active org; the target must
// be one of the admin's own memberships, which the options call lists.
//
// Round trip (A → B → A): the person already has a membership + archived
// profile at A, and those are REUSED rather than duplicated — same staff
// number, same history.
public class EmployeeTransferService : IEmployeeTransferService
{
    private static readonly JsonSerializerOptions CamelCase = new(JsonSerializerDefaults.Web);

    private readonly IEmployeeTransferRepository _transfers;
    private readonly IOrganizationMembershipRepository _memberships;
    private readonly IEmployeeProfileRepository _profiles;
    private readonly IEmployeePolicyRepository _policies;
    private readonly IOrganizationRepository _organizations;
    private readonly IPayslipRepository _payslips;
    private readonly IUserRepository _users;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IEmploymentHistory _history;
    private readonly IPayrollDraftStaleness? _drafts;

    public EmployeeTransferService(
        IEmployeeTransferRepository transfers,
        IOrganizationMembershipRepository memberships,
        IEmployeeProfileRepository profiles,
        IEmployeePolicyRepository policies,
        IOrganizationRepository organizations,
        IPayslipRepository payslips,
        IUserRepository users,
        IAuditService audit,
        ICurrentUser currentUser,
        IEmploymentHistory history,
        IPayrollDraftStaleness? drafts = null)
    {
        _transfers = transfers;
        _memberships = memberships;
        _profiles = profiles;
        _policies = policies;
        _organizations = organizations;
        _payslips = payslips;
        _users = users;
        _audit = audit;
        _currentUser = currentUser;
        _history = history;
        _drafts = drafts;
    }

    public async Task<EmployeeTransferOptionsDto?> GetOptionsAsync(string userId)
    {
        if (await _memberships.GetForUserInCurrentOrgAsync(userId) is null) return null;

        var targets = await ListTargetsAsync();
        foreach (var t in targets) t.EmployeeActiveHere = await ActiveAtTargetAsync(t.Id, userId);
        var open = await _transfers.GetOpenForUserAsync(userId);

        return new EmployeeTransferOptionsDto
        {
            Targets = targets,
            Pending = open is null ? null : await ToDtoAsync(open, targets),
        };
    }

    public async Task<IReadOnlyList<EmployeeTransferDto>> ListOpenAsync()
    {
        var open = await _transfers.GetOpenForCurrentOrgAsync();
        if (open.Count == 0) return [];

        var targets = await ListTargetsAsync();
        var result = new List<EmployeeTransferDto>(open.Count);
        foreach (var t in open) result.Add(await ToDtoAsync(t, targets));
        return result;
    }

    public async Task<TransferResult> CreateAsync(string userId, CreateEmployeeTransferDto dto)
    {
        var sourceOrgId = _currentUser.OrganizationId;
        if (string.IsNullOrEmpty(sourceOrgId) || string.IsNullOrEmpty(_currentUser.UserId))
            return Fail("Session expired. Please sign in again.");

        var membership = await _memberships.GetForUserInCurrentOrgAsync(userId);
        if (membership is null) return new TransferResult(false, null, null);   // 404

        // Admins and owners are separate accounts that administer an org; they
        // are not on its payroll, so there is nothing to move.
        if (OrgRoles.IsAdministrative(membership.Role))
            return Fail("Only employees and supervisors can be transferred.");

        var effectiveDate = DateTime.SpecifyKind(dto.EffectiveDate.Date, DateTimeKind.Utc);
        var today = Today();
        if (effectiveDate < today)
            return Fail("The effective date can't be in the past.");

        if (dto.TargetOrganizationId == sourceOrgId)
            return Fail("Pick a different company — they already work here.");

        var source = await _profiles.GetByUserAsync(userId);
        if (source?.IsArchived == true)
            return Fail("This employee is archived — restore them before transferring.");

        // Their last day here is the day before; it can't fall before they started.
        var joined = source?.JoinDate ?? membership.JoinDate;
        if (joined is not null && effectiveDate <= joined.Value.Date)
            return Fail($"The transfer date must be after they joined this company ({joined.Value:yyyy-MM-dd}).");

        var targets = await ListTargetsAsync();
        var target = targets.FirstOrDefault(t => t.Id == dto.TargetOrganizationId);
        if (target is null)
            return Fail("You don't administer that company, or it has no policy set up yet.");

        if (target.Policies.All(p => p.Id != dto.TargetPolicyId))
            return Fail("That policy doesn't belong to the target company.");

        if (await ActiveAtTargetAsync(dto.TargetOrganizationId, userId))
            return Fail($"This employee is already active at {target.Name}.");

        if (await _transfers.GetOpenForUserAsync(userId) is not null)
            return Fail("A transfer is already scheduled for this employee. Cancel it first to schedule another.");

        var transfer = new EmployeeTransfer
        {
            OrganizationId = sourceOrgId,
            UserId = userId,
            TargetOrganizationId = dto.TargetOrganizationId,
            TargetPolicyId = dto.TargetPolicyId,
            EffectiveDate = effectiveDate,
            CopyPayrollInfo = dto.CopyPayrollInfo,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim(),
            CreatedByUserId = _currentUser.UserId,
        };

        var who = await NameOfAsync(userId);
        var executedNow = effectiveDate == today;

        if (executedNow)
        {
            // Inline, so the admin sees it land rather than "tomorrow". The
            // transfer row is written in the same commit as the move itself.
            try
            {
                await ExecuteAsync(transfer, isNewTransfer: true);
            }
            catch (TransferException ex)
            {
                return Fail(ex.Message);
            }
        }
        else
        {
            await _transfers.AddAsync(transfer);
        }

        // A transfer that ran now is logged once, as the move itself.
        if (executedNow)
        {
            await AuditExecutedAsync(transfer, target.Name, who, sourceOrgId);
        }
        else
        {
            await _audit.WriteAsync(new AuditEvent(
                AuditActions.EmployeeTransferSchedule,
                $"Scheduled {who}'s transfer to {target.Name} on {effectiveDate:yyyy-MM-dd}",
                TargetType: "Employee",
                TargetId: userId,
                Metadata: new
                {
                    TransferId = transfer.Id,
                    transfer.TargetOrganizationId,
                    EffectiveDate = effectiveDate.ToString("yyyy-MM-dd"),
                    transfer.CopyPayrollInfo,
                }));
        }

        return new TransferResult(true, await ToDtoAsync(transfer, targets), null, executedNow);
    }

    public async Task<(bool Ok, DuplicateResultDto? Result, string? Error)> DuplicateAsync(
        string userId, DuplicateEmployeeDto dto)
    {
        var sourceOrgId = _currentUser.OrganizationId;
        if (string.IsNullOrEmpty(sourceOrgId) || string.IsNullOrEmpty(_currentUser.UserId))
            return (false, null, "Session expired. Please sign in again.");

        var membership = await _memberships.GetForUserInCurrentOrgAsync(userId);
        if (membership is null) return (false, null, null);   // 404

        if (OrgRoles.IsAdministrative(membership.Role))
            return (false, null, "Only employees and supervisors can be added to another company.");

        var source = await _profiles.GetByUserAsync(userId);
        if (source?.IsArchived == true)
            return (false, null, "This employee is archived here — use Transfer, or restore them first.");

        if (dto.TargetOrganizationId == sourceOrgId)
            return (false, null, "Pick a different company — they already work here.");

        var target = (await ListTargetsAsync()).FirstOrDefault(t => t.Id == dto.TargetOrganizationId);
        if (target is null)
            return (false, null, "You don't administer that company, or it has no policy set up yet.");
        if (target.Policies.All(p => p.Id != dto.TargetPolicyId))
            return (false, null, "That policy doesn't belong to the target company.");
        if (await ActiveAtTargetAsync(target.Id, userId))
            return (false, null, $"This employee already works at {target.Name}.");
        // A queued transfer would then fail on its date ("already active there").
        if (await _transfers.GetOpenForUserAsync(userId) is not null)
            return (false, null, "A transfer is scheduled for this employee. Cancel it first, or let it run.");

        var joinDate = DateTime.SpecifyKind(dto.JoinDate.Date, DateTimeKind.Utc);
        var sourceOrgName = (await _organizations.GetByIdAsync(sourceOrgId))?.Name ?? "another company";
        var now = DateTime.UtcNow;
        var newMemberships = new List<OrganizationMembership>();
        var newProfiles = new List<EmployeeProfile>();

        // Membership at the target — reopened if they worked there before.
        var targetMembership = await _memberships.GetAsync(target.Id, userId);
        if (targetMembership is null)
        {
            targetMembership = new OrganizationMembership
            {
                OrganizationId = target.Id,
                UserId = userId,
                Role = OrgRoles.Employee,
                EmployeeNumber = await TargetEmployeeNumberAsync(target.Id, membership.EmployeeNumber),
            };
            newMemberships.Add(targetMembership);
        }
        targetMembership.JobTitle = membership.JobTitle;
        targetMembership.PolicyId = dto.TargetPolicyId;
        targetMembership.ShiftId = null;
        targetMembership.JoinDate = joinDate;
        targetMembership.HiddenByEmployeeAt = null;
        targetMembership.UpdatedAt = now;

        var targetProfile = await _profiles.GetByUserInOrgAsync(target.Id, userId);
        if (targetProfile is not null)
        {
            await _history.RecordEndedIfMissingAsync(
                target.Id, userId, targetProfile.JoinDate, targetProfile.LeaveDate, targetProfile.ArchiveReason);
        }
        else
        {
            targetProfile = new EmployeeProfile { OrganizationId = target.Id, UserId = userId };
            newProfiles.Add(targetProfile);
        }

        var from = source ?? new EmployeeProfile();
        CopyPersonal(from, targetProfile);
        // Start from a clean payroll slate (also clears an old tenure's salary
        // on a reopened profile), then lay the person's own numbers back on.
        ResetPayroll(from, targetProfile);
        if (dto.CopyStatutoryAndBank) CopyStatutoryAndBank(from, targetProfile);
        targetProfile.JoinDate = joinDate;
        targetProfile.LeaveDate = null;
        targetProfile.IsArchived = false;
        targetProfile.ArchivedAt = null;
        targetProfile.ArchiveReason = null;
        targetProfile.UpdatedAt = now;

        await _history.OpenAsync(target.Id, userId, joinDate, $"Added from {sourceOrgName} (works at both)");
        await _transfers.CommitAsync(newMemberships, newProfiles);
        if (_drafts is not null) await _drafts.MarkAllDraftsForOrgAsync(target.Id);

        var who = await NameOfAsync(userId);
        var metadata = new
        {
            SourceOrganizationId = sourceOrgId,
            TargetOrganizationId = target.Id,
            JoinDate = joinDate.ToString("yyyy-MM-dd"),
            dto.CopyStatutoryAndBank,
        };
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.EmployeeDuplicate, $"Added {who} to {target.Name} as well",
            TargetType: "Employee", TargetId: userId, Metadata: metadata, OrganizationId: sourceOrgId));
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.EmployeeDuplicate, $"{who} added from {sourceOrgName} (works at both)",
            TargetType: "Employee", TargetId: userId, Metadata: metadata, OrganizationId: target.Id));

        return (true, new DuplicateResultDto { TargetOrganizationId = target.Id, TargetOrganizationName = target.Name }, null);
    }

    public async Task<TransferResult> CancelAsync(string userId, string transferId)
    {
        var transfer = await _transfers.GetByIdAsync(transferId);   // tenant-filtered: source org only
        if (transfer is null || transfer.UserId != userId) return new TransferResult(false, null, null);

        if (transfer.Status is EmployeeTransferStatus.PENDING or EmployeeTransferStatus.FAILED)
        {
            transfer.Status = EmployeeTransferStatus.CANCELLED;
            await _transfers.UpdateAsync(transfer);

            await _audit.WriteAsync(new AuditEvent(
                AuditActions.EmployeeTransferCancel,
                $"Cancelled {await NameOfAsync(userId)}'s transfer scheduled for {transfer.EffectiveDate:yyyy-MM-dd}",
                TargetType: "Employee",
                TargetId: userId,
                Metadata: new { TransferId = transfer.Id }));
        }

        return new TransferResult(true, await ToDtoAsync(transfer, await ListTargetsAsync()), null);
    }

    public async Task ExecuteDueAsync(string transferId)
    {
        var transfer = await _transfers.GetByIdAnyOrgAsync(transferId);
        if (transfer is null
            || transfer.Status is EmployeeTransferStatus.EXECUTED or EmployeeTransferStatus.CANCELLED)
            return;

        await ExecuteAsync(transfer, isNewTransfer: false);

        var targetName = (await _organizations.GetByIdAsync(transfer.TargetOrganizationId))?.Name
                         ?? "another company";
        await AuditExecutedAsync(transfer, targetName, await NameOfAsync(transfer.UserId), transfer.OrganizationId);
    }

    public async Task MarkFailedAsync(string transferId, string error)
    {
        var transfer = await _transfers.GetByIdAnyOrgAsync(transferId);
        if (transfer is null || transfer.Status != EmployeeTransferStatus.PENDING
                                && transfer.Status != EmployeeTransferStatus.FAILED)
            return;

        transfer.Status = EmployeeTransferStatus.FAILED;
        transfer.ErrorMessage = error.Length > 500 ? error[..500] : error;
        await _transfers.UpdateAsync(transfer);
    }

    // ---- The move itself ----

    // Validates the target side again (time has passed since scheduling), then
    // builds every change and commits them in ONE SaveChanges. Throws
    // TransferException for a reason the admin can act on.
    private async Task ExecuteAsync(EmployeeTransfer transfer, bool isNewTransfer)
    {
        var sourceOrgId = transfer.OrganizationId;
        var targetOrgId = transfer.TargetOrganizationId;
        var userId = transfer.UserId;

        var targetOrg = await _organizations.GetByIdAsync(targetOrgId)
                        ?? throw new TransferException("The target company no longer exists.");
        var sourceOrgName = (await _organizations.GetByIdAsync(sourceOrgId))?.Name ?? "the previous company";

        var sourceMembership = await _memberships.GetAsync(sourceOrgId, userId)
                               ?? throw new TransferException("This person is no longer a member of the source company.");

        // Whoever scheduled it must still run the target: access can be taken
        // away between scheduling and the effective date.
        if (!await AdministersTargetAsync(transfer.CreatedByUserId, targetOrgId))
            throw new TransferException(
                $"The admin who scheduled this no longer manages employees at {targetOrg.Name}. Cancel it and have someone who does schedule it again.");

        var policy = (await _policies.GetActiveForOrgsAsync([targetOrgId]))
                     .FirstOrDefault(p => p.Id == transfer.TargetPolicyId)
                     ?? throw new TransferException("The chosen policy no longer exists at the target company.");

        var targetMembership = await _memberships.GetAsync(targetOrgId, userId);
        var targetProfile = await _profiles.GetByUserInOrgAsync(targetOrgId, userId);
        if (targetMembership is not null && (targetProfile is null || !targetProfile.IsArchived))
            throw new TransferException($"This person is already active at {targetOrg.Name}.");

        var source = await _profiles.GetByUserInOrgAsync(sourceOrgId, userId);
        var effective = transfer.EffectiveDate;
        var now = DateTime.UtcNow;

        // They may have left in the meantime (archived, or a planned leave date
        // before the transfer). Running anyway would rewrite that leave date
        // and hand them an active job at the target.
        if (source?.IsArchived == true)
            throw new TransferException(
                "This person was archived after the transfer was scheduled. Cancel it, or restore them first.");
        if (source?.LeaveDate is { } leaving && leaving.Date < effective.AddDays(-1))
            throw new TransferException(
                $"Their last day here ({leaving:yyyy-MM-dd}) is before the transfer date. Cancel the transfer.");

        // This year's figures at the source, for PCB continuity at the target.
        // Read BEFORE anything changes.
        // Each read is for ONE named org: the inline path's current org is the
        // source, and the job has none — neither may decide what gets counted.
        PayrollYtdTotals? sourceYtd = null, targetOwnYtd = null;
        if (transfer.CopyPayrollInfo && source is not null)
        {
            sourceYtd = (await _payslips.GetYtdByEmployeeInOrgAsync(sourceOrgId, effective.Year))
                .GetValueOrDefault(source.Id);
        }
        if (transfer.CopyPayrollInfo && targetProfile is not null)
        {
            targetOwnYtd = (await _payslips.GetYtdByEmployeeInOrgAsync(targetOrgId, effective.Year))
                .GetValueOrDefault(targetProfile.Id);
        }

        var newMemberships = new List<OrganizationMembership>();
        var newProfiles = new List<EmployeeProfile>();

        // 1) Close the source side: same fields the Archive toggle sets, so the
        //    past-leaver sweep and the employee list agree with it. A member
        //    with no saved profile gets an archived one, or they'd stay listed
        //    as active at the company they left.
        if (source is null)
        {
            source = new EmployeeProfile { OrganizationId = sourceOrgId, UserId = userId };
            newProfiles.Add(source);
        }
        source.LeaveDate = effective.AddDays(-1);
        source.IsArchived = true;
        source.ArchivedAt = now;
        source.ArchiveReason = Truncate($"Transferred to {targetOrg.Name}", 200);
        source.UpdatedAt = now;

        await _history.CloseAsync(
            sourceOrgId, userId, source.JoinDate ?? sourceMembership.JoinDate, source.LeaveDate,
            $"Transferred to {targetOrg.Name}", transfer.Id);

        // 2) Membership at the target — reused on a round trip.
        if (targetMembership is null)
        {
            targetMembership = new OrganizationMembership
            {
                OrganizationId = targetOrgId,
                UserId = userId,
                // Supervisor is earned by a team placement, and teams don't move.
                Role = OrgRoles.Employee,
                EmployeeNumber = await TargetEmployeeNumberAsync(targetOrgId, sourceMembership.EmployeeNumber),
            };
            newMemberships.Add(targetMembership);
        }
        targetMembership.JobTitle = sourceMembership.JobTitle;
        targetMembership.PolicyId = policy.Id;
        targetMembership.ShiftId = null;
        targetMembership.JoinDate = effective;
        targetMembership.UpdatedAt = now;

        // A returning employee may have removed this company from their own
        // list after leaving ("Leave company"); it comes back now.
        targetMembership.HiddenByEmployeeAt = null;

        // 3) Profile at the target — reused on a round trip, then refreshed.
        //    Its old dates are about to be overwritten; history keeps them.
        if (targetProfile is not null)
        {
            await _history.RecordEndedIfMissingAsync(
                targetOrgId, userId, targetProfile.JoinDate, targetProfile.LeaveDate, targetProfile.ArchiveReason);
        }
        await _history.OpenAsync(targetOrgId, userId, effective, $"Transferred from {sourceOrgName}", transfer.Id);

        if (targetProfile is null)
        {
            targetProfile = new EmployeeProfile { OrganizationId = targetOrgId, UserId = userId };
            newProfiles.Add(targetProfile);
        }
        CopyPersonal(source, targetProfile);
        if (transfer.CopyPayrollInfo)
        {
            CopyPayroll(source, targetProfile);
            CarryYearToDate(source, sourceYtd, targetOwnYtd, effective.Year, targetProfile);
        }
        else
        {
            ResetPayroll(source, targetProfile);
        }
        targetProfile.JoinDate = effective;
        targetProfile.LeaveDate = null;
        targetProfile.IsArchived = false;
        targetProfile.ArchivedAt = null;
        targetProfile.ArchiveReason = null;
        targetProfile.UpdatedAt = now;

        // 4) The queue row, in the same commit.
        transfer.Status = EmployeeTransferStatus.EXECUTED;
        transfer.ExecutedAt = now;
        transfer.ErrorMessage = null;

        await _transfers.CommitExecutionAsync(transfer, isNewTransfer, newMemberships, newProfiles);

        // Payroll inputs changed in both companies (an archive and a leave date
        // at the source; a new or reopened employee at the target). Named orgs:
        // the job runs with no current one.
        if (_drafts is not null)
        {
            await _drafts.MarkAllDraftsForOrgAsync(sourceOrgId);
            await _drafts.MarkAllDraftsForOrgAsync(targetOrgId);
        }
    }

    // Who the person is — always goes across.
    private static void CopyPersonal(EmployeeProfile from, EmployeeProfile to)
    {
        to.Phone = from.Phone;
        to.AlternateEmail = from.AlternateEmail;
        to.Gender = from.Gender;
        to.DateOfBirth = from.DateOfBirth;
        to.Nationality = from.Nationality;
        to.Race = from.Race;
        to.HasPr = from.HasPr;
        to.IdType = from.IdType;
        to.IdNumber = from.IdNumber;
        to.MaritalStatus = from.MaritalStatus;
        to.IsResident = from.IsResident;
        to.IsOku = from.IsOku;
        to.AddressLine1 = from.AddressLine1;
        to.AddressLine2 = from.AddressLine2;
        to.City = from.City;
        to.Postcode = from.Postcode;
        to.State = from.State;
        to.EmergencyContactName = from.EmergencyContactName;
        to.EmergencyContactPhone = from.EmergencyContactPhone;
        to.EmergencyContactRelation = from.EmergencyContactRelation;
        to.SpouseWorking = from.SpouseWorking;
        to.SpouseDisabled = from.SpouseDisabled;
        to.SpousePcbNumber = from.SpousePcbNumber;
        to.SpouseIdNumber = from.SpouseIdNumber;
        to.ChildReliefJson = from.ChildReliefJson;
    }

    // Statutory, bank and salary setup. Not copied either way: department,
    // location, contract terms and the work permit (PLKS) — those belong to the
    // employer, and a new employer means new ones.
    private static void CopyPayroll(EmployeeProfile from, EmployeeProfile to)
    {
        CopyStatutoryAndBank(from, to);
        to.SalaryType = from.SalaryType;
        to.MonthlySalary = from.MonthlySalary;
        to.HourlyRate = from.HourlyRate;
        to.FixedAllowancesJson = from.FixedAllowancesJson;
    }

    // The person's OWN statutory registrations and bank account — the same
    // whichever company employs them. Salary is the employer's, so not here.
    private static void CopyStatutoryAndBank(EmployeeProfile from, EmployeeProfile to)
    {
        to.ContributeToEpf = from.ContributeToEpf;
        to.EpfNumber = from.EpfNumber;
        to.EpfEmployeeRate = from.EpfEmployeeRate;
        to.EpfEmployeeVoluntary = from.EpfEmployeeVoluntary;
        to.EpfEmployerVoluntary = from.EpfEmployerVoluntary;
        to.EpfMemberBefore1998 = from.EpfMemberBefore1998;
        to.SocsoNumber = from.SocsoNumber;
        to.SocsoScheme = from.SocsoScheme;
        to.ContributeToEis = from.ContributeToEis;
        to.ContributeToSkbbk = from.ContributeToSkbbk;
        to.IncomeTaxNumber = from.IncomeTaxNumber;
        to.SpecialTaxScheme = from.SpecialTaxScheme;
        to.SpecialTaxFrom = from.SpecialTaxFrom;
        to.SpecialTaxTo = from.SpecialTaxTo;
        to.PcbBorneByEmployer = from.PcbBorneByEmployer;
        to.SsfwNumber = from.SsfwNumber;
        to.PaymentMethod = from.PaymentMethod;
        to.BankName = from.BankName;
        to.BankAccountHolderName = from.BankAccountHolderName;
        to.BankAccountNumber = from.BankAccountNumber;
    }

    // "Personal only": the target admin sets payroll up from scratch. Defaults
    // match a brand-new profile; the salary TYPE still carries, as the monolith did.
    private static void ResetPayroll(EmployeeProfile from, EmployeeProfile to)
    {
        to.ContributeToEpf = true;
        to.EpfNumber = null;
        to.EpfEmployeeRate = 0m;
        to.EpfEmployeeVoluntary = 0m;
        to.EpfEmployerVoluntary = 0m;
        to.EpfMemberBefore1998 = false;
        to.SocsoNumber = null;
        to.SocsoScheme = null;
        to.ContributeToEis = true;
        to.ContributeToSkbbk = false;
        to.IncomeTaxNumber = null;
        to.SpecialTaxScheme = null;
        to.SpecialTaxFrom = null;
        to.SpecialTaxTo = null;
        to.PcbBorneByEmployer = false;
        to.SsfwNumber = null;
        to.PaymentMethod = PaymentMethod.BANK_TRANSFER;
        to.BankName = null;
        to.BankAccountHolderName = null;
        to.BankAccountNumber = null;
        to.SalaryType = from.SalaryType;
        to.MonthlySalary = null;
        to.HourlyRate = null;
        to.FixedAllowancesJson = null;
        to.PrevEmploymentYear = null;
        to.PrevRemuneration = null;
        to.PrevEpf = null;
        to.PrevPcb = null;
        to.PrevZakat = null;
        to.PrevAllowableDeductions = null;
        to.PrevByCategoryJson = null;
        to.PrevIncludesPriorThisOrgPeriod = false;
    }

    // The target's "previous employment" for the year: everything the person
    // earned this year anywhere EXCEPT at the target itself.
    //
    //   elsewhere = what the source carried in from employers before it
    //               (net of the source's own months if it was flagged as
    //               including them) + what the source itself paid;
    //   prev      = elsewhere − what the TARGET already paid them this year
    //               (only non-zero on a return), floored at zero.
    //
    // Written as plain prior-employer figures with the flag OFF. Leaning on
    // PrevIncludesPriorThisOrgPeriod instead is wrong after a return: payroll
    // nets the target's GROWING own YTD against the carried total, so each
    // month back at the target eats into the other employer's months until
    // PCB drops to zero.
    public static void CarryYearToDate(
        EmployeeProfile source, PayrollYtdTotals? sourceYtd, PayrollYtdTotals? targetOwnYtd,
        int year, EmployeeProfile to)
    {
        var carried = source.PrevEmploymentYear == year;
        var sourceFlagged = carried && source.PrevIncludesPriorThisOrgPeriod;

        // A figure from before the source, net of the source's own months when
        // the source's prev was flagged as already including them.
        decimal Prior(decimal? declared, decimal sourceOwn) =>
            !carried ? 0m : sourceFlagged ? Math.Max(0m, (declared ?? 0m) - sourceOwn) : declared ?? 0m;

        decimal Prev(decimal? declared, Func<PayrollYtdTotals, decimal> pick)
        {
            var own = sourceYtd is null ? 0m : pick(sourceYtd);
            var elsewhere = Prior(declared, own) + own;
            var atTarget = targetOwnYtd is null ? 0m : pick(targetOwnYtd);
            return Math.Max(0m, elsewhere - atTarget);
        }

        var byCategory = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (carried)
        {
            foreach (var item in PreviousEmployerItems.Parse(source.PrevByCategoryJson))
                byCategory[item.Category] = byCategory.GetValueOrDefault(item.Category) + item.Amount;
            if (sourceFlagged && sourceYtd is not null)
            {
                foreach (var (category, amount) in sourceYtd.AllowanceByCategory)
                    if (byCategory.ContainsKey(category))
                        byCategory[category] = Math.Max(0m, byCategory[category] - amount);
            }
        }
        if (sourceYtd is not null)
        {
            foreach (var (category, amount) in sourceYtd.AllowanceByCategory)
                byCategory[category] = byCategory.GetValueOrDefault(category) + amount;
        }
        if (targetOwnYtd is not null)
        {
            foreach (var (category, amount) in targetOwnYtd.AllowanceByCategory)
                if (byCategory.ContainsKey(category))
                    byCategory[category] = Math.Max(0m, byCategory[category] - amount);
        }
        var items = byCategory.Where(kv => kv.Value > 0m).ToList();

        to.PrevEmploymentYear = year;
        to.PrevRemuneration = Prev(source.PrevRemuneration, y => y.Taxable);
        to.PrevEpf = Prev(source.PrevEpf, y => y.Epf);
        to.PrevPcb = Prev(source.PrevPcb, y => y.Pcb);
        to.PrevZakat = Prev(source.PrevZakat, y => y.Zakat);
        to.PrevAllowableDeductions = Prev(source.PrevAllowableDeductions, y => y.AllowableDeductions);
        to.PrevByCategoryJson = items.Count == 0
            ? null
            : JsonSerializer.Serialize(items.Select(kv => new { category = kv.Key, amount = kv.Value }), CamelCase);
        to.PrevIncludesPriorThisOrgPeriod = false;
    }

    // ---- Helpers ----

    // Every other org where the signed-in admin is Admin/Owner with the
    // Employees module, that has a live policy to place the person on.
    private async Task<List<TransferTargetDto>> ListTargetsAsync()
    {
        var adminId = _currentUser.UserId;
        var currentOrg = _currentUser.OrganizationId;
        if (string.IsNullOrEmpty(adminId)) return [];

        var orgIds = (await _memberships.GetByUserAsync(adminId))
            .Where(m => m.OrganizationId != currentOrg
                        && OrgRoles.IsAdministrative(m.Role)
                        && (m.Modules is null
                            || OrgModules.Split(m.Modules).Contains(OrgModules.Employees, StringComparer.OrdinalIgnoreCase)))
            .Select(m => m.OrganizationId)
            .Distinct()
            .ToList();
        if (orgIds.Count == 0) return [];

        var policiesByOrg = (await _policies.GetActiveForOrgsAsync(orgIds))
            .GroupBy(p => p.OrganizationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var targets = new List<TransferTargetDto>();
        foreach (var orgId in orgIds)
        {
            // An org with no live policy is a dead end in the dialog — leave it out.
            if (!policiesByOrg.TryGetValue(orgId, out var policies) || policies.Count == 0) continue;

            var org = await _organizations.GetByIdAsync(orgId);
            if (org is null) continue;

            targets.Add(new TransferTargetDto
            {
                Id = org.Id,
                Name = org.Name,
                Policies = policies
                    .Select(p => new TransferTargetPolicyDto { Id = p.Id, Name = p.Name, IsDefault = p.IsDefault })
                    .ToList(),
            });
        }

        return targets.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Admin/Owner at the org with the Employees module — the same rule
    // ListTargetsAsync applies to the signed-in admin.
    private async Task<bool> AdministersTargetAsync(string userId, string orgId)
    {
        var m = await _memberships.GetAsync(orgId, userId);
        return m is not null
               && OrgRoles.IsAdministrative(m.Role)
               && (m.Modules is null
                   || OrgModules.Split(m.Modules).Contains(OrgModules.Employees, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<bool> ActiveAtTargetAsync(string targetOrgId, string userId)
    {
        if (await _memberships.GetAsync(targetOrgId, userId) is null) return false;
        var profile = await _profiles.GetByUserInOrgAsync(targetOrgId, userId);
        return profile is null || !profile.IsArchived;
    }

    // Keep the staff number; if the target already uses it, suffix "-T" as the
    // monolith did, and leave it blank if even that is taken.
    private async Task<string?> TargetEmployeeNumberAsync(string targetOrgId, string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return null;
        if (!await _memberships.EmployeeNumberExistsAsync(targetOrgId, number)) return number;

        var suffixed = Truncate($"{number}-T", 40);
        return await _memberships.EmployeeNumberExistsAsync(targetOrgId, suffixed) ? null : suffixed;
    }

    private async Task AuditExecutedAsync(EmployeeTransfer transfer, string targetName, string who, string sourceOrgId)
    {
        // Both companies' logs: the source sees them leave, the target sees
        // them arrive. The daily job has no signed-in user, so it names itself.
        var actor = _currentUser.IsAuthenticated ? null : "Employee transfer (scheduled)";
        var metadata = new
        {
            TransferId = transfer.Id,
            SourceOrganizationId = sourceOrgId,
            transfer.TargetOrganizationId,
            EffectiveDate = transfer.EffectiveDate.ToString("yyyy-MM-dd"),
            transfer.CopyPayrollInfo,
        };

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.EmployeeTransferExecute,
            $"{who} moved to {targetName}",
            TargetType: "Employee",
            TargetId: transfer.UserId,
            Metadata: metadata,
            OrganizationId: sourceOrgId,
            ActorName: actor));

        var sourceName = (await _organizations.GetByIdAsync(sourceOrgId))?.Name ?? "another company";
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.EmployeeTransferExecute,
            $"{who} joined by transfer from {sourceName}",
            TargetType: "Employee",
            TargetId: transfer.UserId,
            Metadata: metadata,
            OrganizationId: transfer.TargetOrganizationId,
            ActorName: actor));
    }

    private async Task<EmployeeTransferDto> ToDtoAsync(EmployeeTransfer t, IReadOnlyList<TransferTargetDto> targets)
    {
        // The admin may since have lost access to the target; fall back to a lookup.
        var name = targets.FirstOrDefault(x => x.Id == t.TargetOrganizationId)?.Name
                   ?? (await _organizations.GetByIdAsync(t.TargetOrganizationId))?.Name
                   ?? t.TargetOrganizationId;

        return new EmployeeTransferDto
        {
            Id = t.Id,
            UserId = t.UserId,
            TargetOrganizationId = t.TargetOrganizationId,
            TargetOrganizationName = name,
            TargetPolicyId = t.TargetPolicyId,
            EffectiveDate = t.EffectiveDate,
            CopyPayrollInfo = t.CopyPayrollInfo,
            Notes = t.Notes,
            Status = t.Status,
            ErrorMessage = t.ErrorMessage,
            CreatedAt = t.CreatedAt,
            ExecutedAt = t.ExecutedAt,
        };
    }

    private async Task<string> NameOfAsync(string userId)
    {
        var user = await _users.GetByIdAsync(userId);
        return user is null ? "an employee" : string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name;
    }

    // The org's business day — what the admin means by "today" in the date picker.
    private static DateTime Today() => AttendanceTime.StartOfLocalDay(DateTime.UtcNow);

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] : s;

    private static TransferResult Fail(string error) => new(false, null, error);
}

// A reason the move can't go ahead that the admin can act on (shown as-is).
public class TransferException : Exception
{
    public TransferException(string message) : base(message) { }
}
