using AltomateHR.Api.Modules.Accounts.Entities;

namespace AltomateHR.Api.Modules.Accounts;

// The starter list a company gets when it isn't using Xero, so employees can
// file claims from day one instead of waiting for an admin to type a chart of
// accounts in. Codes follow Xero's standard Malaysian chart, so a company that
// connects Xero later recognises them. On connecting, these are archived (Xero
// owns the chart then) and restored on disconnect — as hand-made projects are.
public static class DefaultClaimItemTypes
{
    public sealed record Item(string Code, string Name, string Type = ChartOfAccountTypes.Expense);

    public static readonly IReadOnlyList<Item> All =
    [
        new("400", "Advertising & Marketing"),
        new("404", "Bank Fees"),
        new("412", "Consulting & Accounting"),
        new("420", "Entertainment"),
        new("425", "Freight & Courier"),
        new("429", "General Expenses"),
        new("433", "Insurance"),
        new("441", "Legal Expenses"),
        new("445", "Utilities"),
        // Mileage claims stay off until the company sets its own per-km rate.
        new("449", "Motor Vehicle / Mileage"),
        new("453", "Office Expenses"),
        new("461", "Printing & Stationery"),
        new("469", "Rent"),
        new("473", "Repairs & Maintenance"),
        new("477", "Staff Welfare & Refreshments"),
        new("478", "Training & Development"),
        new("479", "Medical (Staff)"),
        new("485", "Subscriptions"),
        new("489", "Telephone & Internet"),
        new("493", "Travel – Local (incl. parking & toll)"),
        new("494", "Travel – Overseas"),
        // What a company-paid claim was paid FROM.
        new("090", "Business Bank Account", ChartOfAccountTypes.Bank),
    ];

    public static ChartOfAccount ToAccount(Item item, string organizationId, DateTime now) => new()
    {
        OrganizationId = organizationId,
        Code = item.Code,
        Name = item.Name,
        Type = item.Type,
        IsCustom = true,
        IsDefault = true,
        IsSelectable = true,
        CreatedAt = now,
    };
}
