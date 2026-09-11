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

    // Connected, but the stored tokens are unusable — only fresh consent fixes
    // it. Lets the UI say "reconnect" instead of waiting for a sync to fail.
    public bool NeedsReconnect { get; set; }
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
// A currency the Xero organisation is actually subscribed to. Billing in
// anything else is refused outright — "Organisation is not subscribed to
// currency USD" — so this is the real list of what a claim may be filed in.
public sealed record XeroCurrencyResponse(string Code, string Description);

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


// ---- Manual journals (payroll) ----

// A payroll run posts to Xero as ONE manual journal: the expense side debited,
// the liabilities to EPF / SOCSO / EIS / LHDN and the net owed to staff
// credited. A bill would imply a supplier invoice, which payroll is not.
//
// Xero gates manual journals behind a different permission from invoices — the
// same token that posts bills happily can be refused here. See XeroClient's
// 401 handling.
public sealed record XeroManualJournalRequest(
    // What appears as the journal's description in Xero. The period belongs in
    // here — an accountant scanning the journal list identifies a run by it.
    string Narration,
    DateTime Date,
    IReadOnlyList<XeroManualJournalLine> Lines,
    // Sent as Xero's Idempotency-Key. Derived from the run id, so a retry
    // after a timeout re-posts the SAME key and Xero returns the original
    // journal instead of creating a duplicate.
    string IdempotencyKey);

// One side of one line. A positive amount debits, a negative one credits —
// Xero's own convention, and the journal is rejected unless they net to zero.
public sealed record XeroManualJournalLine(
    decimal Amount,
    string Description,
    // The chart-of-account CODE, as on a bill line.
    string? AccountCode,
    // At most two, which is Xero's hard limit per line. Anything beyond that
    // is dropped by the client rather than failing the whole run.
    IReadOnlyList<XeroTrackingRef>? Tracking = null);

// Xero identifies a tracking selection by the category NAME and the option
// NAME, not by id, on a journal line.
public sealed record XeroTrackingRef(string Name, string Option);

public sealed record XeroManualJournalResponse(string ManualJournalId, string? Narration);


// ---- Tracking categories ----

// Xero's "second dimension" on a transaction — an org typically uses one for
// department or project. Payroll reads them so an admin can map the project
// dimension onto every payroll journal line.
public sealed record XeroTrackingCategoryResponse(
    string TrackingCategoryId,
    string Name,
    string Status,
    IReadOnlyList<XeroTrackingOptionResponse> Options);

public sealed record XeroTrackingOptionResponse(
    string TrackingOptionId,
    string Name,
    string Status);
