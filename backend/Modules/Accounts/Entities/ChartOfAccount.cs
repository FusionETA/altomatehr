using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Accounts.Entities;

// An account in the org's chart of accounts. Claims are filed against selectable
// accounts; some carry a spend limit or a mileage rate. (Xero-linked accounts come later.)
public class ChartOfAccount : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;             // e.g. "6100"

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;             // e.g. "Travel Expenses"

    [MaxLength(20)]
    public string Type { get; set; } = ChartOfAccountTypes.Expense; // see ChartOfAccountTypes (loose string, like the real app)

    [MaxLength(80)]
    public string? XeroAccountId { get; set; }                   // Xero AccountID, when synced from Xero

    [MaxLength(40)]
    public string? XeroStatus { get; set; }                      // ACTIVE | ARCHIVED | ...

    public DateTime? XeroSyncedAt { get; set; }

    // Hand-made here rather than pulled from Xero. Provenance only — it changes
    // no behaviour, because the Xero sync matches on XeroAccountId and so never
    // touches a row that has none. Kept because the distinction is otherwise
    // only inferable, and "XeroAccountId is null" stops being a reliable proxy
    // the moment an account is created locally and later linked to Xero.
    public bool IsCustom { get; set; }

    public bool IsSelectable { get; set; } = true;               // employees can file claims against it

    [Precision(12, 2)]
    public decimal? LimitAmount { get; set; }                    // optional spend limit

    public bool AllowMileageClaim { get; set; }

    [Precision(10, 4)]
    public decimal? MileageRate { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; }
}

// The local account types. Xero has a dozen; these are the only three this app
// has a use for, and each has exactly one job:
//
//   EXPENSE    — what a claim is coded to, and the debit side of the payroll
//                journal. Xero's whole expense family collapses into it.
//   BANK       — what a company-paid claim was spent FROM.
//   LIABILITY  — the credit side of the payroll journal: EPF / SOCSO / EIS /
//                PCB / net salary payable. Never offered for a claim, and not
//                listed on the claims chart of accounts — only the payroll
//                Xero mapping asks for these.
public static class ChartOfAccountTypes
{
    public const string Expense = "EXPENSE";
    public const string Bank = "BANK";
    public const string Liability = "LIABILITY";

    public static bool IsLiability(string? type) =>
        string.Equals(type, Liability, StringComparison.OrdinalIgnoreCase);
}
