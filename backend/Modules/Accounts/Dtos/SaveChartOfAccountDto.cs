using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Accounts.Dtos;

public class SaveChartOfAccountDto
{
    [Required, MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [Required, RegularExpression("EXPENSE|BANK", ErrorMessage = "Type must be 'EXPENSE' or 'BANK'.")]
    public string Type { get; set; } = "EXPENSE";

    public bool IsSelectable { get; set; } = true;

    [Range(0, 100_000_000)]
    public decimal? LimitAmount { get; set; }

    public bool AllowMileageClaim { get; set; }

    [Range(0, 100)]
    public decimal? MileageRate { get; set; }
}
