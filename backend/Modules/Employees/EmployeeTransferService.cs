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
        IEmploymentHistory history)
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
    }

    public async Task<EmployeeTransferOptionsDto?> GetOptionsAsync(string userId)
    {
        if (await _memberships.GetForUserInCurrentOrgAsync(userId) is null) return null;

        var targets = await ListTargetsAsync();
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

        // This year's figures at the source, for PCB continuity at the target.
        // Read BEFORE anything changes.
        PayrollYtdTotals? ytd = null;
        if (transfer.CopyPayrollInfo && source is not null)
        {
            var byEmployee = await _payslips.GetYtdByEmployeeAsync(effective.Year, excludeRunId: null);
            ytd = byEmployee.GetValueOrDefault(source.Id);
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
        var isReturning = targetMembership is not null;
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
            CarryYearToDate(source, ytd, effective.Year, isReturning, targetProfile);
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
        to.SalaryType = from.SalaryType;
        to.MonthlySalary = from.MonthlySalary;
        to.HourlyRate = from.HourlyRate;
        to.FixedAllowancesJson = from.FixedAllowancesJson;
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

    // The target's "previous employment" for the year = what the source itself
    // carried in from before (if it was for this year) + what the source paid.
    // That is LHDN's view of a same-owner move: continuous for MTD purposes.
    private static void CarryYearToDate(
        EmployeeProfile source, PayrollYtdTotals? ytd, int year, bool isReturning, EmployeeProfile to)
    {
        var carried = source.PrevEmploymentYear == year;
        decimal Prior(decimal? v) => carried ? v ?? 0m : 0m;

        var byCategory = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (carried)
        {
            foreach (var item in PreviousEmployerItems.Parse(source.PrevByCategoryJson))
                byCategory[item.Category] = byCategory.GetValueOrDefault(item.Category) + item.Amount;
        }
        if (ytd is not null)
        {
            foreach (var (category, amount) in ytd.AllowanceByCategory)
                byCategory[category] = byCategory.GetValueOrDefault(category) + amount;
        }

        to.PrevEmploymentYear = year;
        to.PrevRemuneration = Prior(source.PrevRemuneration) + (ytd?.Taxable ?? 0m);
        to.PrevEpf = Prior(source.PrevEpf) + (ytd?.Epf ?? 0m);
        to.PrevPcb = Prior(source.PrevPcb) + (ytd?.Pcb ?? 0m);
        to.PrevZakat = Prior(source.PrevZakat) + (ytd?.Zakat ?? 0m);
        to.PrevAllowableDeductions = Prior(source.PrevAllowableDeductions) + (ytd?.AllowableDeductions ?? 0m);
        to.PrevByCategoryJson = byCategory.Count == 0
            ? null
            : JsonSerializer.Serialize(
                byCategory.Select(kv => new { category = kv.Key, amount = kv.Value }), CamelCase);

        // Coming BACK to a company already worked at this year: the outbound
        // transfer folded this company's months into the source's prev figures,
        // so they are in what we just carried. Flag it so payroll takes this
        // company's own YTD back out instead of counting those months twice.
        // (Zero own YTD this year makes the flag harmless.)
        to.PrevIncludesPriorThisOrgPeriod = isReturning;
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
