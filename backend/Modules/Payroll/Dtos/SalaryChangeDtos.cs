using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Payroll.Dtos;

public class SalaryChangeDto
{
    public string Id { get; set; } = string.Empty;
    public string EmployeeProfileId { get; set; } = string.Empty;

    public DateTime EffectiveDate { get; set; }

    public SalaryType PreviousSalaryType { get; set; }
    public decimal? PreviousMonthlySalary { get; set; }
    public decimal? PreviousHourlyRate { get; set; }

    public SalaryType NewSalaryType { get; set; }
    public decimal? NewMonthlySalary { get; set; }
    public decimal? NewHourlyRate { get; set; }

    public SalaryChangeReason Reason { get; set; }
    public string ReasonLabel { get; set; } = string.Empty;

    // Null across a salary-type switch, where a percentage is meaningless.
    public decimal? RaisePercent { get; set; }

    public string? Notes { get; set; }

    public string? ChangedByUserId { get; set; }
    public string? ChangedByName { get; set; }

    public DateTime CreatedAt { get; set; }
}

// What the caller supplies alongside a salary edit. The before/after figures
// come from the profile itself, so they cannot disagree with what was saved.
public class RecordSalaryChangeDto
{
    // Defaults to today when the caller does not say. A raise agreed on the
    // 20th and typed on the 22nd should carry the date it takes effect.
    [Required]
    public DateTime EffectiveDate { get; set; } = DateTime.UtcNow.Date;

    public SalaryChangeReason Reason { get; set; } = SalaryChangeReason.RAISE;

    [MaxLength(500)]
    public string? Notes { get; set; }
}
