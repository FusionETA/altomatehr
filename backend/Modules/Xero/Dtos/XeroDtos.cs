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


// ---- Bills (accounts payable) ----

// One bill to push to Xero. Deliberately flat and provider-neutral: the caller
// (claims) describes what is owed and to whom, and knows nothing about Xero's
// invoice shape.
public sealed record XeroBillRequest(
    // Who the org owes. Xero matches or creates a contact by name.
    string ContactName,
    // Shown on the bill so it can be traced back to the claim.
    string Reference,
    DateTime Date,
    DateTime DueDate,
    string CurrencyCode,
    IReadOnlyList<XeroBillLine> Lines);

public sealed record XeroBillLine(
    string Description,
    decimal Amount,
    // Xero's chart-of-account CODE, not our internal account id. Null lets Xero
    // fall back to its own default rather than rejecting the whole bill.
    string? AccountCode);

public sealed record XeroBillResponse(string BillId, string? Reference);
