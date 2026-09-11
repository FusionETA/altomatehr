using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll.Dtos;

public class PayrollSettingsDto
{
    public WorkingDaysRule WorkingDaysRule { get; set; }

    public decimal DefaultEpfEmployeeRate { get; set; }
    public decimal DefaultEpfEmployerRate { get; set; }

    public bool HrdfEnabled { get; set; }
    public decimal? HrdfRate { get; set; }

    public bool AutoApplySocsoEisRelief { get; set; }

    public bool SyncClaimsToXeroOnSubmit { get; set; }
    public bool SyncPayrollToXeroOnSubmit { get; set; }
    public string? XeroMappingJson { get; set; }

    public string? PayrollBankName { get; set; }
    public string? PayorAccountHolderName { get; set; }
    public string? PayorOrganisationCode { get; set; }
    public string? EcpPayorAccountNo { get; set; }
    public string? EcpPayorBic { get; set; }

    // False until the admin saves for the first time — the GET returns the
    // statutory defaults rather than 404, and the UI uses this to show whether
    // it is looking at real configuration or a starting point.
    public bool IsConfigured { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

public class SavePayrollSettingsDto
{
    [Required]
    public WorkingDaysRule WorkingDaysRule { get; set; } = WorkingDaysRule.TWENTY_SIX;

    // The employee floor is statutory (11% under Part A) and the calculator
    // clamps up to it regardless, but rejecting an out-of-range figure here
    // stops a confusing value being stored and echoed back.
    [Range(0, 100)] public decimal DefaultEpfEmployeeRate { get; set; } = 11.00m;
    [Range(0, 100)] public decimal DefaultEpfEmployerRate { get; set; } = 13.00m;

    public bool HrdfEnabled { get; set; }

    // HRD Corp's levy is 1% (Part I) or 0.5% (Part II); the range is wider so a
    // future rate change doesn't need a code deploy.
    [Range(0, 100)] public decimal? HrdfRate { get; set; }

    public bool AutoApplySocsoEisRelief { get; set; } = true;

    public bool SyncClaimsToXeroOnSubmit { get; set; }
    public bool SyncPayrollToXeroOnSubmit { get; set; }

    public string? XeroMappingJson { get; set; }

    [MaxLength(120)] public string? PayrollBankName { get; set; }
    [MaxLength(160)] public string? PayorAccountHolderName { get; set; }
    [MaxLength(60)] public string? PayorOrganisationCode { get; set; }
    [MaxLength(20)] public string? EcpPayorAccountNo { get; set; }
    [MaxLength(20)] public string? EcpPayorBic { get; set; }
}
