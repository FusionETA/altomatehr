using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll.Dtos;

// One one-off row an admin typed for this employee on this run.
//
// `Kind` is absent on purpose: it is derived from the category on save, so the
// stored kind cannot contradict the catalogue the calculator dispatches on.
public class ManualLineItemDto
{
    // A `PayrollAdjustmentCategories` code. Validated on save — the calculator
    // silently skips an unrecognised code, so accepting one here would look to
    // the admin like their row simply disappeared at generation time.
    [Required]
    [MaxLength(60)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Label { get; set; }

    // Negative amounts are refused rather than flipped: a deduction is already
    // a deduction by its category, so a negative one would add to pay.
    [Range(0, 99999999.99)]
    public decimal Amount { get; set; }

    // Only meaningful on an additional-remuneration category — see
    // `FixedAllowance.TreatAsRecurring`.
    public bool TreatAsRecurring { get; set; }
}

// A per-run override of one row in the employee's profile fixed allowances.
public class FixedAllowanceOverrideDto
{
    // Null = keep the profile's amount.
    [Range(0, 99999999.99)]
    public decimal? Amount { get; set; }

    // Zero this row out for this run only.
    public bool Skip { get; set; }
}

// The full adjustment row. A save REPLACES it wholesale rather than patching
// field by field: the admin edits one form holding all of it, and a partial
// update would make "I cleared the overtime" indistinguishable from "I did not
// mention the overtime".
public class SavePayrollRunAdjustmentDto
{
    [Range(0, 999.99)] public decimal OtNormalHours { get; set; }
    [Range(0, 999.99)] public decimal OtRestHours { get; set; }
    [Range(0, 999.99)] public decimal OtPublicHours { get; set; }

    public List<ManualLineItemDto> ManualLineItems { get; set; } = [];

    // Keyed by the profile fixed-allowance's array INDEX as a string.
    public Dictionary<string, FixedAllowanceOverrideDto> FixedAllowanceOverrides { get; set; } = [];

    [Range(0, 9999.99)] public decimal? WorkedHours { get; set; }
    [Range(0, 9999.99)] public decimal? ExpectedHours { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }
}

public class PayrollRunAdjustmentDto
{
    public string Id { get; set; } = string.Empty;
    public string PayrollRunId { get; set; } = string.Empty;
    public string EmployeeProfileId { get; set; } = string.Empty;

    public decimal OtNormalHours { get; set; }
    public decimal OtRestHours { get; set; }
    public decimal OtPublicHours { get; set; }

    public List<ManualLineItemResponseDto> ManualLineItems { get; set; } = [];
    public Dictionary<string, FixedAllowanceOverrideDto> FixedAllowanceOverrides { get; set; } = [];

    public decimal? WorkedHours { get; set; }
    public decimal? ExpectedHours { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// What was stored, kind included — the client renders the row without having to
// carry its own copy of the category catalogue.
public class ManualLineItemResponseDto
{
    public PayslipLineKind Kind { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Label { get; set; }
    public decimal Amount { get; set; }
    public bool TreatAsRecurring { get; set; }
}

public sealed record PayrollRunAdjustmentSaveResult(
    bool Found, bool Ok, PayrollRunAdjustmentDto? Adjustment, string? Error);
