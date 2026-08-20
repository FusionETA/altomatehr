namespace AltomateHR.Api.Modules.Accounts.Dtos;

public class ChartOfAccountDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? XeroAccountId { get; set; }
    public string? XeroStatus { get; set; }
    public DateTime? XeroSyncedAt { get; set; }
    public bool IsSelectable { get; set; }
    public decimal? LimitAmount { get; set; }
    public bool AllowMileageClaim { get; set; }
    public decimal? MileageRate { get; set; }
    public bool IsArchived { get; set; }
}
