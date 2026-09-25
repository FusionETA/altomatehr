namespace AltomateHR.Api.Modules.Payroll.Pdf;

// The whole run, ready to render. Built once by the service so every
// document renderer stays a pure function of its input — which is what makes
// them testable without a database.
public sealed record PayrollDocumentModel
{
    public required Entities.PayrollRun Run { get; init; }

    public required string OrganizationName { get; init; }
    public required string PeriodLabel { get; init; }

    // "Draft", "Awaiting approval", "Submitted" — printed on the page so a
    // document taken off a draft run cannot be mistaken for a final one.
    public required string StatusLabel { get; init; }

    public required DateTime IssueDate { get; init; }

    public IReadOnlyList<StatutoryEmployeeRow> Rows { get; init; } = [];

    // Each payslip's line items, by payslip id, in calculation order. Only the
    // summary itemises them; empty for documents that do not.
    public IReadOnlyDictionary<string, IReadOnlyList<Entities.PayslipLineItem>> LineItems { get; init; } =
        new Dictionary<string, IReadOnlyList<Entities.PayslipLineItem>>();

    // When the document was produced, Malaysian time — printed in the summary's
    // footer.
    public DateTime GeneratedAt { get; init; }
}
