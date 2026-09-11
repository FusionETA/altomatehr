using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// One payslip, as the API returns it.
//
// Shared by the admin run detail and the employee's own payslip view. ONE
// mapper on purpose: the two surfaces show the same figures to different
// audiences, and a second copy is how an employee's payslip ends up
// disagreeing with the one their employer is looking at.
public static class PayslipMapper
{
    public static PayslipDto ToDto(Payslip p, IReadOnlyList<PayslipLineItem> lineItems) => new()
    {
        Id = p.Id,
        EmployeeProfileId = p.EmployeeProfileId,
        UserId = p.UserId,

        SnapshotName = p.SnapshotName,
        SnapshotEmployeeNumber = p.SnapshotEmployeeNumber,
        SnapshotPosition = p.SnapshotPosition,
        SnapshotNationality = p.SnapshotNationality,
        SnapshotIsResident = p.SnapshotIsResident,

        SnapshotSalaryType = p.SnapshotSalaryType,
        SnapshotMonthlySalary = p.SnapshotMonthlySalary,
        SnapshotHourlyRate = p.SnapshotHourlyRate,
        SnapshotEpfRatesJson = p.SnapshotEpfRatesJson,

        TotalWorkingDays = p.TotalWorkingDays,
        ProratedDays = p.ProratedDays,
        ProrationDaysInPeriod = p.ProrationDaysInPeriod,
        ProratedFactor = p.ProratedFactor,
        WorkedHours = p.WorkedHours,
        ExpectedHours = p.ExpectedHours,
        UnpaidLeaveDays = p.UnpaidLeaveDays,

        BasicPay = p.BasicPay,
        ProratedPay = p.ProratedPay,
        OtNormalHours = p.OtNormalHours,
        OtRestHours = p.OtRestHours,
        OtPublicHours = p.OtPublicHours,
        OtPay = p.OtPay,
        TotalAllowances = p.TotalAllowances,
        TotalReimbursements = p.TotalReimbursements,
        TotalDeductions = p.TotalDeductions,
        TotalBenefitsInKind = p.TotalBenefitsInKind,

        EpfEmployee = p.EpfEmployee,
        EpfEmployer = p.EpfEmployer,
        SocsoEmployee = p.SocsoEmployee,
        SocsoEmployer = p.SocsoEmployer,
        EisEmployee = p.EisEmployee,
        EisEmployer = p.EisEmployer,
        SkbbkEmployee = p.SkbbkEmployee,
        SkbbkWage = p.SkbbkWage,
        Pcb = p.Pcb,
        PcbNormal = p.PcbNormal,
        PcbAdditional = p.PcbAdditional,
        PcbCalculationJson = p.PcbCalculationJson,
        Cp38 = p.Cp38,
        Zakat = p.Zakat,
        Hrdf = p.Hrdf,
        HrdfWage = p.HrdfWage,

        GrossPay = p.GrossPay,
        NetPay = p.NetPay,
        TotalCostToEmployer = p.TotalCostToEmployer,

        StatutoryWarnings = string.IsNullOrWhiteSpace(p.StatutoryWarnings)
            ? []
            : p.StatutoryWarnings.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),

        LineItems = lineItems.Select(li => new PayslipLineItemDto
        {
            Id = li.Id,
            Kind = li.Kind,
            Label = li.Label,
            Amount = li.Amount,
            Category = li.Category,
            PcbTaxableAmount = li.PcbTaxableAmount,
            ClaimId = li.ClaimId,
            SubjectToEpf = li.SubjectToEpf,
            SubjectToSocso = li.SubjectToSocso,
            SubjectToEis = li.SubjectToEis,
            SubjectToPcb = li.SubjectToPcb,
        }).ToList(),
    };
}
