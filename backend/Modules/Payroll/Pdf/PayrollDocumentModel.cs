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
}
