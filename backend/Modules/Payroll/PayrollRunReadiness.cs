namespace AltomateHR.Api.Modules.Payroll;

// Whether a run has the fields its statutory files will need.
//
// Checked BEFORE a submission is accepted, so an admin fixes a missing SSM
// number while the run is still a draft rather than discovering it weeks
// later when the file will not generate — by which point the run is filed and
// reverting it cascades through the rest of the year.
//
// What counts as required is defined by the GENERATORS, which is why this
// lives beside them rather than with the state machine.
public static class PayrollRunReadiness
{
    // Org-level. Every one of these appears in a file the org must submit.
    private static readonly (Func<Entities.PayrollCompanyInfo?, string?> Read, string Label)[] OrgFields =
    [
        (c => c?.EmployerName, "Employer name"),
        (c => c?.EmployerTin, "Employer LHDN E-number"),
        (c => c?.RegistrationNo, "SSM registration number"),
        (c => c?.PerkesoEmployerCode, "PERKESO employer code"),
    ];

    public static Result Check(StatutoryRunPayload payload)
    {
        var orgIssues = OrgFields
            .Where(f => string.IsNullOrWhiteSpace(f.Read(payload.CompanyInfo)))
            .Select(f => f.Label)
            .ToList();

        var employeeIssues = new List<EmployeeIssue>();

        foreach (var row in payload.Rows)
        {
            var missing = EmployeeGaps(row.EmployeeCode, row.IdNumber, row.IsLocalOrPr);

            if (missing.Count > 0)
            {
                employeeIssues.Add(new EmployeeIssue(row.EmployeeName, row.EmployeeCode, missing));
            }
        }

        return new Result(orgIssues, employeeIssues);
    }

    // What one person is missing, off the three facts the files need.
    //
    // Pulled out of the loop above because the payroll roster runs the same
    // check against LIVE profiles, before any run exists — an admin should be
    // able to fix a missing IC in January rather than discover it when
    // March's submission is refused. Two copies of this rule would drift.
    public static IReadOnlyList<string> EmployeeGaps(
        string? employeeCode, string? idNumber, bool isLocalOrPr)
    {
        var missing = new List<string>();

        // LHDN's CP39 has a mandatory employee-number column.
        if (string.IsNullOrWhiteSpace(employeeCode)) missing.Add("Employee number");

        // PERKESO and LHDN both key on this; which one it is depends on
        // whether they are local. Labelled accordingly so the admin knows
        // which box to fill.
        if (isLocalOrPr)
        {
            if (StatutoryFileFields.DigitsOnly(idNumber).Length == 0) missing.Add("IC number");
        }
        else if (string.IsNullOrWhiteSpace(idNumber))
        {
            missing.Add("Passport number");
        }

        // The income tax number is deliberately NOT required here. PCB
        // computes without one, and a new joiner whose TIN has not been
        // issued yet must not block the whole month's payroll. The CP39
        // renderer checks it at generation time instead — and only for
        // employees who actually had tax withheld.

        return missing;
    }

    public sealed record EmployeeIssue(
        string Name, string EmployeeCode, IReadOnlyList<string> Missing);

    public sealed record Result(
        IReadOnlyList<string> OrgIssues, IReadOnlyList<EmployeeIssue> EmployeeIssues)
    {
        public bool Ok => OrgIssues.Count == 0 && EmployeeIssues.Count == 0;

        public int TotalMissingCount =>
            OrgIssues.Count + EmployeeIssues.Sum(e => e.Missing.Count);

        // One sentence an admin can act on. Names a handful of people rather
        // than all of them — the banner has the full list.
        public string Describe()
        {
            var parts = new List<string>();

            if (OrgIssues.Count > 0)
            {
                parts.Add($"Company Info is missing: {string.Join(", ", OrgIssues)}.");
            }

            if (EmployeeIssues.Count > 0)
            {
                var names = string.Join(", ", EmployeeIssues.Take(5).Select(e => e.Name));
                var more = EmployeeIssues.Count > 5
                    ? $" and {EmployeeIssues.Count - 5} more"
                    : string.Empty;

                parts.Add(
                    $"{EmployeeIssues.Count} employee(s) need required fields filled ({names}{more}).");
            }

            return string.Join(" ", parts);
        }
    }
}
