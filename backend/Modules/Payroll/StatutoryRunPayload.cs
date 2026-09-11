using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Everything the statutory file renderers need, loaded once.
//
// The renderers are pure functions over this — no EF, no clock, no HTTP —
// which is what makes byte-exact tests possible for formats that are parsed
// by column position.
public sealed record StatutoryRunPayload
{
    public required PayrollRun Run { get; init; }

    // Null when the org has never filled Company Info in. Each renderer
    // refuses with its own message naming the field it needed.
    public PayrollCompanyInfo? CompanyInfo { get; init; }

    // One row per payslip, ordered by employee code so regenerating a file
    // produces the same bytes.
    public IReadOnlyList<StatutoryEmployeeRow> Rows { get; init; } = [];
}

// One employee's payslip joined to the identity a submission needs.
//
// The MONEY comes from the payslip snapshot — that is the month's filed
// figure and must never move. The IDENTIFIERS are read LIVE from the employee
// profile, on purpose: an EPF number corrected after the run was generated is
// a correction to a fact about the person, not a change to what they were
// paid, and regenerating the file should pick it up.
public sealed record StatutoryEmployeeRow
{
    public required Payslip Payslip { get; init; }

    public required string EmployeeName { get; init; }

    // The employer's own payroll number. LHDN requires it on every CP39 row.
    public string EmployeeCode { get; init; } = string.Empty;

    public string? IdNumber { get; init; }
    public IdType? IdType { get; init; }
    public string? EpfNumber { get; init; }
    public string? SocsoNumber { get; init; }
    public string? SsfwNumber { get; init; }
    public string? IncomeTaxNumber { get; init; }

    public string? Nationality { get; init; }
    public bool HasPr { get; init; }
    public Gender? Gender { get; init; }
    public MaritalStatus? MaritalStatus { get; init; }

    // ---- Payment ----
    // Where the net pay goes. The payment schedule and the bank file both
    // read these; a payslip shows the account masked.
    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.BANK_TRANSFER;
    public string? BankName { get; init; }
    public string? BankAccountNumber { get; init; }
    public string? BankAccountHolderName { get; init; }

    public DateTime? JoinDate { get; init; }

    // PERKESO and LHDN both route on this: locals and PRs are keyed by IC,
    // everyone else by their SOCSO/foreign-worker number or passport.
    public bool IsLocalOrPr =>
        HasPr || string.Equals(Nationality?.Trim(), "Malaysian", StringComparison.OrdinalIgnoreCase);
}

// A generated file, or the reason one could not be produced.
//
// A missing IC is an admin's data problem with a specific fix, not an
// exceptional condition — so it comes back as a message naming the person,
// which the controller turns into a 409 rather than a 500.
public sealed record StatutoryFileResult(
    bool Ok, string? FileName, byte[]? Content, string? ContentType, string? Error)
{
    public static StatutoryFileResult Text(string fileName, string content, string contentType) =>
        new(true, fileName, System.Text.Encoding.UTF8.GetBytes(content), contentType, null);

    public static StatutoryFileResult Refused(string error) => new(false, null, null, null, error);
}
