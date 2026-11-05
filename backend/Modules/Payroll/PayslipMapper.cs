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
        OtCustom = PayrollRunAdjustments.ParseOtCustomLines(p.OtCustomJson),
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
        VoluntaryPcb = p.VoluntaryPcb,
        Zakat = p.Zakat,
        Hrdf = p.Hrdf,
        HrdfWage = p.HrdfWage,

        GrossPay = p.GrossPay,
        GrossShown = PayrollGrossShown.Of(p),
        NetPay = p.NetPay,
        TotalCostToEmployer = p.TotalCostToEmployer,
        PcbBorneByEmployer = p.PcbBorneByEmployer,

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

        OvertimeRows = PayslipDisplayLines.Overtime(p).Select(r => new PayslipOvertimeRowDto
        {
            Label = r.Label,
            Multiplier = r.Multiplier,
            Rate = Math.Round(r.Rate, 4, MidpointRounding.AwayFromZero),
            Hours = r.Hours,
            Amount = r.Amount,
        }).ToList(),
        EarningLines = DisplayLines(
            PayslipDisplayLines.Earnings(lineItems), l => PayslipDisplayLines.EarningsReconcile(p, l)),
        // Plus an imported payslip's zakat / CP38, which no line item stands for.
        DeductionLines = DisplayLines(
            PayslipDisplayLines.Deductions(lineItems), l => PayslipDisplayLines.DeductionsReconcile(p, l))
            ?.Concat(PayslipDisplayLines.OutsideTotalDeductions(p, lineItems)
                .Select(l => new PayslipDisplayLineDto { Label = l.Label, Amount = l.Amount }))
            .ToList(),
        NotInPayLines = PayslipDisplayLines.NotInPay(lineItems)
            .Select(l => new PayslipDisplayLineDto { Label = l.Label, Amount = l.Amount })
            .ToList(),
    };

    private static List<PayslipDisplayLineDto>? DisplayLines(
        List<PayslipDisplayLines.Line> lines, Func<List<PayslipDisplayLines.Line>, bool> reconciles) =>
        reconciles(lines)
            ? lines.Select(l => new PayslipDisplayLineDto { Label = l.Label, Amount = l.Amount }).ToList()
            : null;
}
