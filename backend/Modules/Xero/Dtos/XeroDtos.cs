namespace AltomateHR.Api.Modules.Xero.Dtos;

public class XeroConnectUrlDto
{
    public string Url { get; set; } = string.Empty;
}

public class XeroStatusDto
{
    public bool Connected { get; set; }
    public string? TenantId { get; set; }
    public string? TenantName { get; set; }
    public DateTime? ConnectedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? AccessTokenExpiresAt { get; set; }
}

public class XeroSyncAccountsResultDto
{
    public int Imported { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
}

public class XeroSyncProjectsResultDto
{
    public int Imported { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
}
