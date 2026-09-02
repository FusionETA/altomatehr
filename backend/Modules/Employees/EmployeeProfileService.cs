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
        IDirectoryService directory)
    {
        _profiles = profiles;
        _memberships = memberships;
        _directory = directory;
    }

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
            Apply(dto, profile);
            await _profiles.UpdateAsync(profile);
        }

        var user = await _directory.GetUserAsync(userId);
        return ToDto(profile, user);
    }

    // Copy the editable fields dto → entity. Context fields (Id/Email/Name) are
    // ignored — they come from the route + User, never from the client.
    private static void Apply(EmployeeProfileDto d, EmployeeProfile e)
    {
        e.Phone = d.Phone; e.AlternateEmail = d.AlternateEmail;
        e.Gender = d.Gender; e.DateOfBirth = d.DateOfBirth;
        e.Nationality = d.Nationality; e.Race = d.Race; e.HasPr = d.HasPr;
        e.IdType = d.IdType; e.IdNumber = d.IdNumber;
        e.MaritalStatus = d.MaritalStatus; e.IsResident = d.IsResident; e.IsOku = d.IsOku;
        e.City = d.City; e.Postcode = d.Postcode; e.State = d.State;
        e.EmergencyContactName = d.EmergencyContactName;
        e.EmergencyContactPhone = d.EmergencyContactPhone;
        e.EmergencyContactRelation = d.EmergencyContactRelation;

        e.JoinDate = d.JoinDate; e.LeaveDate = d.LeaveDate;
        e.Department = d.Department; e.Location = d.Location; e.WorkSchedule = d.WorkSchedule;

        e.SpouseWorking = d.SpouseWorking; e.SpouseDisabled = d.SpouseDisabled;
        e.SpousePcbNumber = d.SpousePcbNumber; e.SpouseIdNumber = d.SpouseIdNumber;
        e.ChildReliefJson = d.ChildReliefJson;

        e.PrevEmploymentYear = d.PrevEmploymentYear;
        e.PrevRemuneration = d.PrevRemuneration; e.PrevEpf = d.PrevEpf;
        e.PrevAllowableDeductions = d.PrevAllowableDeductions;
        e.PrevPcb = d.PrevPcb; e.PrevZakat = d.PrevZakat;
        e.PrevIncludesPriorThisOrgPeriod = d.PrevIncludesPriorThisOrgPeriod;

        e.ContributeToEpf = d.ContributeToEpf; e.EpfNumber = d.EpfNumber;
        e.EpfEmployeeRate = d.EpfEmployeeRate;
        e.EpfEmployeeVoluntary = d.EpfEmployeeVoluntary;
        e.EpfEmployerVoluntary = d.EpfEmployerVoluntary;

        e.SocsoNumber = d.SocsoNumber; e.SocsoScheme = d.SocsoScheme;
        e.ContributeToEis = d.ContributeToEis; e.ContributeToSkbbk = d.ContributeToSkbbk;

        e.IncomeTaxNumber = d.IncomeTaxNumber; e.PcbBorneByEmployer = d.PcbBorneByEmployer;
        e.SsfwNumber = d.SsfwNumber; e.ReportedToLhdn = d.ReportedToLhdn;

        e.PaymentMethod = d.PaymentMethod; e.BankName = d.BankName;
        e.BankAccountHolderName = d.BankAccountHolderName; e.BankAccountNumber = d.BankAccountNumber;

        e.SalaryType = d.SalaryType; e.MonthlySalary = d.MonthlySalary;
        e.HourlyRate = d.HourlyRate; e.FixedAllowancesJson = d.FixedAllowancesJson;

        e.PayrollPolicy = d.PayrollPolicy; e.PayrollCycle = d.PayrollCycle;
        e.LeaveEntitlementJson = d.LeaveEntitlementJson; e.PayrollDocumentsJson = d.PayrollDocumentsJson;

        e.IsArchived = d.IsArchived; e.ArchivedAt = d.ArchivedAt;
        e.ArchiveReason = d.ArchiveReason; e.TemporaryReviewDate = d.TemporaryReviewDate;
    }

    private static EmployeeProfileDto ToDto(EmployeeProfile e, User? user) => new()
    {
        Id = e.UserId,
        Email = user?.Email ?? "",
        Name = user?.Name ?? "",

        Phone = e.Phone, AlternateEmail = e.AlternateEmail,
        Gender = e.Gender, DateOfBirth = e.DateOfBirth,
        Nationality = e.Nationality, Race = e.Race, HasPr = e.HasPr,
        IdType = e.IdType, IdNumber = e.IdNumber,
        MaritalStatus = e.MaritalStatus, IsResident = e.IsResident, IsOku = e.IsOku,
        City = e.City, Postcode = e.Postcode, State = e.State,
        EmergencyContactName = e.EmergencyContactName,
        EmergencyContactPhone = e.EmergencyContactPhone,
        EmergencyContactRelation = e.EmergencyContactRelation,

        JoinDate = e.JoinDate, LeaveDate = e.LeaveDate,
        Department = e.Department, Location = e.Location, WorkSchedule = e.WorkSchedule,

        SpouseWorking = e.SpouseWorking, SpouseDisabled = e.SpouseDisabled,
        SpousePcbNumber = e.SpousePcbNumber, SpouseIdNumber = e.SpouseIdNumber,
        ChildReliefJson = e.ChildReliefJson,

        PrevEmploymentYear = e.PrevEmploymentYear,
        PrevRemuneration = e.PrevRemuneration, PrevEpf = e.PrevEpf,
        PrevAllowableDeductions = e.PrevAllowableDeductions,
        PrevPcb = e.PrevPcb, PrevZakat = e.PrevZakat,
        PrevIncludesPriorThisOrgPeriod = e.PrevIncludesPriorThisOrgPeriod,

        ContributeToEpf = e.ContributeToEpf, EpfNumber = e.EpfNumber,
        EpfEmployeeRate = e.EpfEmployeeRate,
        EpfEmployeeVoluntary = e.EpfEmployeeVoluntary,
        EpfEmployerVoluntary = e.EpfEmployerVoluntary,

        SocsoNumber = e.SocsoNumber, SocsoScheme = e.SocsoScheme,
        ContributeToEis = e.ContributeToEis, ContributeToSkbbk = e.ContributeToSkbbk,

        IncomeTaxNumber = e.IncomeTaxNumber, PcbBorneByEmployer = e.PcbBorneByEmployer,
        SsfwNumber = e.SsfwNumber, ReportedToLhdn = e.ReportedToLhdn,

        PaymentMethod = e.PaymentMethod, BankName = e.BankName,
        BankAccountHolderName = e.BankAccountHolderName, BankAccountNumber = e.BankAccountNumber,

        SalaryType = e.SalaryType, MonthlySalary = e.MonthlySalary,
        HourlyRate = e.HourlyRate, FixedAllowancesJson = e.FixedAllowancesJson,

        PayrollPolicy = e.PayrollPolicy, PayrollCycle = e.PayrollCycle,
        LeaveEntitlementJson = e.LeaveEntitlementJson, PayrollDocumentsJson = e.PayrollDocumentsJson,

        IsArchived = e.IsArchived, ArchivedAt = e.ArchivedAt,
        ArchiveReason = e.ArchiveReason, TemporaryReviewDate = e.TemporaryReviewDate,
    };
}
