using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Employees;

namespace AltomateHR.Api.Modules.Claims;

// The claims summary as a spreadsheet, plus the column contract for importing
// historical claims back in.
//
// Kept out of ClaimsService so the SHAPE of a report can change without
// touching the claim rules — same split as LeaveCsvExporter / LeaveSummaryPdf.
public static class ClaimsSummarySheet
{
    public const string SheetName = "Claims";
    public const string ImportSheetName = "Claims Import";

    private static readonly string[] ExportHeaders =
    [
        "Claim #", "Employee", "Employee Email", "Title", "Description",
        "Claim Type", "Category", "Payment Type", "Amount", "Currency",
        "Spent On", "Submitted On", "Status", "Approval Step",
        "Project", "Account Code", "Account Name", "Merchant", "Spending With",
        "Over Limit", "Distance", "Mileage Rate", "Review Notes",
    ];

    // ---- Export ----
    //
    // One row per claim, in the order the caller supplied (the service sorts).
    // Ids are resolved to human labels — a spreadsheet full of GUIDs is useless
    // to the finance team who actually opens this.
    public static TabularSheet BuildExport(
        IEnumerable<Claim> claims,
        EmployeeRowIndex employees,
        IReadOnlyDictionary<string, string> projectNames,
        IReadOnlyDictionary<string, (string Code, string Name)> accounts)
    {
        var sheet = new TabularSheet(SheetName, ExportHeaders);

        foreach (var claim in claims)
        {
            accounts.TryGetValue(claim.ChartOfAccountId ?? string.Empty, out var account);

            sheet.AddRow(
                claim.ClaimNumber,
                employees.NameOf(claim.EmployeeId),
                employees.EmailOf(claim.EmployeeId),
                claim.Title,
                claim.Description,
                claim.ClaimType.ToString(),
                claim.Category.ToString(),
                claim.PaymentType.ToString(),
                TabularSheet.Money(claim.Amount),
                claim.Currency,
                TabularSheet.Date(claim.SpentAt),
                TabularSheet.Date(claim.SubmittedAt),
                claim.Status.ToString(),
                // 1-based for the reader: "step 1 of the chain", not "step 0".
                TabularSheet.Number(claim.CurrentStep + 1),
                claim.ProjectId is null ? "" : projectNames.GetValueOrDefault(claim.ProjectId, ""),
                account.Code ?? "",
                account.Name ?? "",
                claim.SpendingAt ?? "",
                claim.SpendingWith ?? "",
                TabularSheet.Bool(claim.ExceedsLimit),
                claim.Distance is null ? "" : TabularSheet.Money(claim.Distance.Value),
                claim.MileageRateUsed is null ? "" : claim.MileageRateUsed.Value.ToString("0.0000"),
                claim.ReviewNotes ?? "");
        }

        return sheet;
    }

    // ---- Export: the printable report ----
    //
    // A NARROWER set than the spreadsheet, because A4 landscape has room for
    // roughly a dozen legible columns and the full 23 would print as confetti.
    // Production makes the same cut for the same reason. What's dropped is what
    // a reader can't act on from paper: the long description, mileage snapshot,
    // over-limit flag and approval step.
    private static readonly string[] PrintHeaders =
    [
        "Claim #", "Employee", "Title", "Category", "Payment",
        "Amount", "Currency", "Spent On", "Status", "Project",
        "Account", "Review Notes",
    ];

    public static TabularSheet BuildPrintable(
        IReadOnlyCollection<Claim> claims,
        EmployeeRowIndex employees,
        IReadOnlyDictionary<string, string> projectNames,
        IReadOnlyDictionary<string, (string Code, string Name)> accounts,
        string caption)
    {
        var sheet = new TabularSheet(SheetName, PrintHeaders, caption);

        foreach (var claim in claims)
        {
            accounts.TryGetValue(claim.ChartOfAccountId ?? string.Empty, out var account);

            sheet.AddRow(
                claim.ClaimNumber,
                employees.NameOf(claim.EmployeeId) is { Length: > 0 } name
                    ? name
                    : employees.EmailOf(claim.EmployeeId),
                claim.Title,
                claim.Category.ToString(),
                claim.PaymentType.ToString(),
                TabularSheet.Money(claim.Amount),
                claim.Currency,
                TabularSheet.Date(claim.SpentAt),
                claim.Status.ToString(),
                claim.ProjectId is null ? "" : projectNames.GetValueOrDefault(claim.ProjectId, ""),
                account.Code is null ? "" : $"{account.Code} {account.Name}".Trim(),
                claim.ReviewNotes ?? "");
        }

        // Totals per currency, because summing MYR and USD into one number would
        // be worse than useless on a finance report. One line each, joined.
        var totals = claims
            .GroupBy(c => c.Currency, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key} {TabularSheet.Money(g.Sum(c => c.Amount))}")
            .ToList();

        sheet.SetTotals(
            $"{claims.Count} claim(s)", "", "", "", "TOTAL",
            string.Join("  ·  ", totals), "", "", "", "", "", "");

        return sheet;
    }

    // ---- Import ----
    //
    // A NARROWER column set than the export on purpose: the export carries
    // derived and server-owned fields (over-limit flag, approval step, mileage
    // snapshot) that an import must never be able to dictate. Everything here is
    // either source data or a lookup key.
    public static readonly IReadOnlyList<TabularColumn> ImportColumns =
    [
        new("employeeEmail", "Employee Email", true, "ahmad@company.com",
            ["email", "employee", "staff email"]),
        new("employeeName", "Employee Name", false, "Ahmad Ali",
            ["name", "full name", "member"]),
        new("title", "Title", true, "Client lunch"),
        new("category", "Category", true, "MEAL",
            ["expense category"]),
        new("amount", "Amount", true, "85.50",
            ["total", "claim amount"]),
        new("currency", "Currency", false, "MYR"),
        new("spentOn", "Spent On", true, "2026-01-15",
            ["date", "spent at", "expense date"]),
        new("status", "Status", false, "APPROVED"),
        new("claimType", "Claim Type", false, "EXPENSE"),
        new("paymentType", "Payment Type", false, "PERSONAL",
            ["payment source"]),
        new("accountCode", "Account Code", false, "5100",
            ["chart of account", "account"]),
        new("description", "Description", false, "Lunch with the Acme team"),
        new("reviewNotes", "Review Notes", false, "Migrated from the old system"),
        new("claimNumber", "Claim #", false, "CLM-20260115-A1B2C3",
            ["claim number", "reference"]),
    ];

    public static TabularSheet BuildImportTemplate() =>
        TabularTemplate.Build(ImportSheetName, ImportColumns);
}
