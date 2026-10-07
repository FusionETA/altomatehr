using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Employees;

// Read/upsert the rich per-org employee profile. Only members of the active org
// have one; the membership check enforces that (and blocks cross-org access, since
// both repos are tenant-filtered).
public class EmployeeProfileService : IEmployeeProfileService
{
    private readonly IDirectoryService _directory;
    private readonly IEmployeeProfileRepository _profiles;
    private readonly IOrganizationMembershipRepository _memberships;

    public EmployeeProfileService(
        IEmployeeProfileRepository profiles,
        IOrganizationMembershipRepository memberships,
        IDirectoryService directory,
        Payroll.ISalaryChangeService salaryChanges,
        Payroll.IPayrollDraftStaleness? drafts = null,
        IEmploymentHistory? history = null)
    {
        _drafts = drafts;
        _history = history;
        _profiles = profiles;
        _memberships = memberships;
        _directory = directory;
        _salaryChanges = salaryChanges;
    }

    private readonly Payroll.ISalaryChangeService _salaryChanges;
    // Optional so hand-built instances in tests need not supply it; the app
    // always does. See PayrollDraftStaleness for why saves here mark drafts.
    private readonly Payroll.IPayrollDraftStaleness? _drafts;
    // Optional for the same reason. Records a tenure when archiving ends it
    // and when a restore starts a new one — see EmploymentPeriod.
    private readonly IEmploymentHistory? _history;

    public async Task<EmployeeProfileDto?> GetAsync(string userId)
    {
        if (await _memberships.GetForUserInCurrentOrgAsync(userId) is null)
            return null;   // not a member of this org → 404

        var user = await _directory.GetUserAsync(userId);
        var profile = await _profiles.GetByUserAsync(userId);

        // No profile saved yet → return a context-only shell so the form still loads.
        return profile is null
            ? new EmployeeProfileDto { Id = userId, Email = user?.Email ?? "", Name = user?.Name ?? "" }
            : ToDto(profile, user);
    }

    public async Task<EmployeeProfileDto?> SaveAsync(string userId, EmployeeProfileDto dto)
    {
        if (await _memberships.GetForUserInCurrentOrgAsync(userId) is null)
            return null;   // not a member of this org → 404

        var profile = await _profiles.GetByUserAsync(userId);
        if (profile is null)
        {
            profile = new EmployeeProfile { UserId = userId };
            Apply(dto, profile);
            profile = await _profiles.AddAsync(profile);   // StampTenant sets OrganizationId
        }
        else
        {
            // v2 has one current salary, applied straight away: a raise dated
            // next month would already be paid in this month's run. So the
            // date a change took effect can be today or earlier, never later.
            if (!dto.SalaryChangeIsCorrection
                && dto.SalaryChangeEffectiveDate is { } effective
                && effective.Date > TodayInMalaysia())
            {
                throw new ArgumentException(
                    "The effective date can't be in the future — the new salary applies straight away. "
                    + "Save it on or after the date it takes effect.");
            }

            // The salary as it stood BEFORE this edit. Captured as a copy
            // because Apply mutates the tracked entity in place — reading it
            // afterwards would compare the new values with themselves.
            var before = SalarySnapshot(profile);

            // The tenure as it stood, for the history: Apply overwrites it.
            var (wasArchived, oldJoin, oldLeave, oldReason) =
                (profile.IsArchived, profile.JoinDate, profile.LeaveDate, profile.ArchiveReason);

            Apply(dto, profile);

            // Archiving ends a tenure; restoring starts a new one. Staged here
            // and written by the UpdateAsync below, in the same commit.
            if (_history is not null && wasArchived != profile.IsArchived)
            {
                if (profile.IsArchived)
                {
                    await _history.CloseAsync(
                        profile.OrganizationId, userId, profile.JoinDate, profile.LeaveDate,
                        string.IsNullOrWhiteSpace(profile.ArchiveReason) ? "Archived" : profile.ArchiveReason);
                }
                else
                {
                    await _history.RecordEndedIfMissingAsync(profile.OrganizationId, userId, oldJoin, oldLeave, oldReason);
                    await _history.OpenAsync(profile.OrganizationId, userId, profile.JoinDate, "Restored");
                    await UnhideForEmployeeAsync(userId);
                }
            }

            await _profiles.UpdateAsync(profile);

            // Only writes a row when the salary actually moved — a no-op
            // change would fill the history an IR dispute reads with noise —
            // and, as in the previous system, not for:
            //   • a correction the admin marked as a typo, and
            //   • a first salary (nothing, or RM 0, was set before): filling
            //     in an empty field is not a change to anyone's pay.
            if (!dto.SalaryChangeIsCorrection && HadASalary(before))
            {
                await _salaryChanges.RecordAsync(before, profile, new Payroll.Dtos.RecordSalaryChangeDto
                {
                    EffectiveDate = dto.SalaryChangeEffectiveDate?.Date ?? DateTime.UtcNow.Date,
                    Reason = dto.SalaryChangeReason ?? Payroll.Entities.SalaryChangeReason.RAISE,
                    Notes = dto.SalaryChangeNotes,
                });
            }
        }

        // Any of ~30 fields here feeds the calculation (salary, EPF/SOCSO
        // flags, reliefs, DOB, residency). Not diffed field by field, as in
        // the previous system: a false positive costs one re-run, a miss ships
        // wrong statutory figures.
        if (_drafts is not null) await _drafts.MarkAllDraftsAsync();

        var user = await _directory.GetUserAsync(userId);
        return ToDto(profile, user);
    }

    // A restored employee works here again: if they had removed this company
    // from their own list as a former employee, it comes back.
    private async Task UnhideForEmployeeAsync(string userId)
    {
        var membership = await _memberships.GetForUserInCurrentOrgAsync(userId);
        if (membership?.HiddenByEmployeeAt is null) return;
        membership.HiddenByEmployeeAt = null;
        await _memberships.UpdateAsync(membership);
    }

    private static readonly TimeZoneInfo Myt = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

    public async Task<IReadOnlyList<WorkPermitDto>> GetWorkPermitsAsync() =>
        [.. (await _profiles.GetAllForCurrentOrgAsync())
            .Where(p => p.WorkPermitExpiry is not null)
            .Select(p => new WorkPermitDto
            {
                UserId = p.UserId,
                Nationality = p.Nationality,
                HasPr = p.HasPr,
                IsArchived = p.IsArchived,
                LeaveDate = p.LeaveDate,
                WorkPermitNumber = p.WorkPermitNumber,
                WorkPermitExpiry = p.WorkPermitExpiry!.Value.Date,
            })];

    private static DateTime TodayInMalaysia() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Myt).Date;

    private static bool HadASalary(EmployeeProfile before) =>
        before.SalaryType == Policies.Entities.SalaryType.HOURLY
            ? before.HourlyRate is > 0m
            : before.MonthlySalary is > 0m;

    // Just the three salary fields, detached from the tracked entity.
    private static EmployeeProfile SalarySnapshot(EmployeeProfile profile) => new()
    {
        Id = profile.Id,
        SalaryType = profile.SalaryType,
        MonthlySalary = profile.MonthlySalary,
        HourlyRate = profile.HourlyRate,
    };

    // Copy the editable fields dto → entity. Context fields (Id/Email/Name) are
    // ignored — they come from the route + User, never from the client.
    private static void Apply(EmployeeProfileDto d, EmployeeProfile e)
    {
        e.Phone = d.Phone; e.AlternateEmail = d.AlternateEmail;
        e.Gender = d.Gender; e.DateOfBirth = d.DateOfBirth;
        // "Malaysia" from a partner system saves as the dropdown's "Malaysian".
        e.Nationality = Payroll.Nationalities.Normalise(d.Nationality); e.Race = d.Race; e.HasPr = d.HasPr;
        e.IdType = d.IdType; e.IdNumber = d.IdNumber;
        e.MaritalStatus = d.MaritalStatus; e.IsResident = d.IsResident; e.IsOku = d.IsOku;
        e.AddressLine1 = d.AddressLine1; e.AddressLine2 = d.AddressLine2;
        e.City = d.City; e.Postcode = d.Postcode; e.State = d.State;
        e.EmergencyContactName = d.EmergencyContactName;
        e.EmergencyContactPhone = d.EmergencyContactPhone;
        e.EmergencyContactRelation = d.EmergencyContactRelation;

        e.JoinDate = d.JoinDate; e.LeaveDate = d.LeaveDate;
        e.Department = d.Department; e.Location = d.Location; e.WorkSchedule = d.WorkSchedule;
        e.EmploymentStatus = d.EmploymentStatus; e.ContractEndDate = d.ContractEndDate?.Date;

        e.SpouseWorking = d.SpouseWorking; e.SpouseDisabled = d.SpouseDisabled;
        e.SpousePcbNumber = d.SpousePcbNumber; e.SpouseIdNumber = d.SpouseIdNumber;
        e.ChildReliefJson = d.ChildReliefJson;

        e.PrevEmploymentYear = d.PrevEmploymentYear;
        e.PrevRemuneration = d.PrevRemuneration; e.PrevEpf = d.PrevEpf;
        e.PrevAllowableDeductions = d.PrevAllowableDeductions;
        e.PrevPcb = d.PrevPcb; e.PrevZakat = d.PrevZakat;
        e.PrevIncludesPriorThisOrgPeriod = d.PrevIncludesPriorThisOrgPeriod;
        e.PrevByCategoryJson = d.PrevByCategoryJson;

        e.ContributeToEpf = d.ContributeToEpf; e.EpfNumber = d.EpfNumber;
        e.EpfEmployeeRate = d.EpfEmployeeRate;
        e.EpfEmployeeVoluntary = d.EpfEmployeeVoluntary;
        e.EpfEmployerVoluntary = d.EpfEmployerVoluntary;
        e.EpfMemberBefore1998 = d.EpfMemberBefore1998;

        e.SocsoNumber = d.SocsoNumber; e.SocsoScheme = d.SocsoScheme;
        e.SpecialTaxScheme = d.SpecialTaxScheme;
        // Stored as the first of the month — the approval is by month.
        e.SpecialTaxFrom = d.SpecialTaxScheme is null ? null : FirstOfMonth(d.SpecialTaxFrom);
        e.SpecialTaxTo = d.SpecialTaxScheme is null ? null : FirstOfMonth(d.SpecialTaxTo);
        e.ContributeToEis = d.ContributeToEis; e.ContributeToSkbbk = d.ContributeToSkbbk;

        e.IncomeTaxNumber = d.IncomeTaxNumber; e.PcbBorneByEmployer = d.PcbBorneByEmployer;
        e.SsfwNumber = d.SsfwNumber;
        e.WorkPermitNumber = d.WorkPermitNumber; e.WorkPermitExpiry = d.WorkPermitExpiry?.Date;

        e.PaymentMethod = d.PaymentMethod; e.BankName = d.BankName;
        e.BankAccountHolderName = d.BankAccountHolderName; e.BankAccountNumber = d.BankAccountNumber;

        e.SalaryType = d.SalaryType; e.MonthlySalary = d.MonthlySalary;
        e.HourlyRate = d.HourlyRate; e.FixedAllowancesJson = d.FixedAllowancesJson;

        e.PayrollPolicy = d.PayrollPolicy; e.PayrollCycle = d.PayrollCycle;
        e.LeaveEntitlementJson = d.LeaveEntitlementJson; e.PayrollDocumentsJson = d.PayrollDocumentsJson;

        // ArchivedAt is stamped here, not taken from the client: it records WHEN
        // the archive happened, so it must come from the server's clock and must
        // not be re-stamped by later saves to an already-archived profile.
        if (d.IsArchived && !e.IsArchived) e.ArchivedAt = DateTime.UtcNow;
        else if (!d.IsArchived) e.ArchivedAt = null;
        e.IsArchived = d.IsArchived;
        e.ArchiveReason = d.IsArchived ? d.ArchiveReason : null;
        e.TemporaryReviewDate = d.TemporaryReviewDate;
    }

    private static EmployeeProfileDto ToDto(EmployeeProfile e, User? user) => new()
    {
        Id = e.UserId,
        EmployeeProfileId = e.Id,
        Email = user?.Email ?? "",
        Name = user?.Name ?? "",

        Phone = e.Phone, AlternateEmail = e.AlternateEmail,
        Gender = e.Gender, DateOfBirth = e.DateOfBirth,
        Nationality = e.Nationality, Race = e.Race, HasPr = e.HasPr,
        IdType = e.IdType, IdNumber = e.IdNumber,
        MaritalStatus = e.MaritalStatus, IsResident = e.IsResident, IsOku = e.IsOku,
        AddressLine1 = e.AddressLine1, AddressLine2 = e.AddressLine2,
        City = e.City, Postcode = e.Postcode, State = e.State,
        EmergencyContactName = e.EmergencyContactName,
        EmergencyContactPhone = e.EmergencyContactPhone,
        EmergencyContactRelation = e.EmergencyContactRelation,

        JoinDate = e.JoinDate, LeaveDate = e.LeaveDate,
        Department = e.Department, Location = e.Location, WorkSchedule = e.WorkSchedule,
        EmploymentStatus = e.EmploymentStatus, ContractEndDate = e.ContractEndDate,

        SpouseWorking = e.SpouseWorking, SpouseDisabled = e.SpouseDisabled,
        SpousePcbNumber = e.SpousePcbNumber, SpouseIdNumber = e.SpouseIdNumber,
        ChildReliefJson = e.ChildReliefJson,

        PrevEmploymentYear = e.PrevEmploymentYear,
        PrevRemuneration = e.PrevRemuneration, PrevEpf = e.PrevEpf,
        PrevAllowableDeductions = e.PrevAllowableDeductions,
        PrevPcb = e.PrevPcb, PrevZakat = e.PrevZakat,
        PrevIncludesPriorThisOrgPeriod = e.PrevIncludesPriorThisOrgPeriod,
        PrevByCategoryJson = e.PrevByCategoryJson,

        ContributeToEpf = e.ContributeToEpf, EpfNumber = e.EpfNumber,
        EpfEmployeeRate = e.EpfEmployeeRate,
        EpfEmployeeVoluntary = e.EpfEmployeeVoluntary,
        EpfEmployerVoluntary = e.EpfEmployerVoluntary,
        EpfMemberBefore1998 = e.EpfMemberBefore1998,

        SocsoNumber = e.SocsoNumber, SocsoScheme = e.SocsoScheme,
        SpecialTaxScheme = e.SpecialTaxScheme, SpecialTaxFrom = e.SpecialTaxFrom, SpecialTaxTo = e.SpecialTaxTo,
        ContributeToEis = e.ContributeToEis, ContributeToSkbbk = e.ContributeToSkbbk,

        IncomeTaxNumber = e.IncomeTaxNumber, PcbBorneByEmployer = e.PcbBorneByEmployer,
        SsfwNumber = e.SsfwNumber,
        WorkPermitNumber = e.WorkPermitNumber, WorkPermitExpiry = e.WorkPermitExpiry,

        PaymentMethod = e.PaymentMethod, BankName = e.BankName,
        BankAccountHolderName = e.BankAccountHolderName, BankAccountNumber = e.BankAccountNumber,

        SalaryType = e.SalaryType, MonthlySalary = e.MonthlySalary,
        HourlyRate = e.HourlyRate, FixedAllowancesJson = e.FixedAllowancesJson,

        PayrollPolicy = e.PayrollPolicy, PayrollCycle = e.PayrollCycle,
        LeaveEntitlementJson = e.LeaveEntitlementJson, PayrollDocumentsJson = e.PayrollDocumentsJson,

        IsArchived = e.IsArchived, ArchivedAt = e.ArchivedAt,
        ArchiveReason = e.ArchiveReason, TemporaryReviewDate = e.TemporaryReviewDate,
    };

    private static DateTime? FirstOfMonth(DateTime? value) =>
        value is { } v ? new DateTime(v.Year, v.Month, 1, 0, 0, 0, DateTimeKind.Unspecified) : null;
}
