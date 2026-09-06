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
// What state the bill lands in. Xero's own wording: a DRAFT sits in the
// accountant's queue untouched, AUTHORISED ("Awaiting payment" in Xero's UI)
// is a live liability that shows up in aged payables and can be paid.
//
// The distinction is the admin's to make, not ours — some finance teams want
// every bill reviewed before it counts, others treat an approved claim as
// already owed.
public enum XeroBillStatus { AwaitingPayment, Draft }

public sealed record XeroBillRequest(
    // Who the org owes. Xero matches or creates a contact by name.
    string ContactName,
    // Shown on the bill so it can be traced back to the claim.
    string Reference,
    DateTime Date,
    DateTime DueDate,
    string CurrencyCode,
    XeroBillStatus Status,
    IReadOnlyList<XeroBillLine> Lines);

public sealed record XeroBillLine(
    string Description,
    decimal Amount,
    // Xero's chart-of-account CODE, not our internal account id. Null lets Xero
    // fall back to its own default rather than rejecting the whole bill.
    string? AccountCode);

public sealed record XeroBillResponse(string BillId, string? Reference);


// ---- Spend money (bank transactions) ----

// A company-paid claim did not create a debt — the money already left a
// company account. In Xero that is a SPEND bank transaction against that
// account, not a bill, so it is a separate call with a separate shape.
public sealed record XeroSpendRequest(
    // Whoever was paid — the merchant, not the employee. The employee was
    // never out of pocket.
    string ContactName,
    string Reference,
    DateTime Date,
    string CurrencyCode,
    // Xero AccountID of the BANK account the money left. An id rather than a
    // code because Xero bank accounts often have no code. Required: a spend has
    // to come from somewhere, and guessing would misstate a balance.
    string BankAccountId,
    IReadOnlyList<XeroBillLine> Lines);

public sealed record XeroSpendResponse(string TransactionId);
