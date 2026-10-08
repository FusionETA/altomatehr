using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AltomateHR.Api.Modules.Documents;

// Where a merge field's value comes from. Decides how a gap is fixed: an
// employee field can be filled in (and optionally saved back to the record), a
// company or signatory field is fixed in Payroll → Company Info, an input is
// typed for each letter.
public enum MergeFieldSource { Employee, Company, Signatory, System, Input }

// How a value is entered and printed. Dates and money are typed in a neutral
// form (yyyy-MM-dd, 1234.50) and printed in letter form (8 October 2026,
// RM 1,234.50).
public enum MergeFieldKind { Text, Multiline, Date, Number, Money }

public sealed record MergeFieldDefinition(
    string Key,
    string Label,
    MergeFieldSource Source,
    MergeFieldKind Kind,
    string Description,
    // A gap in this field can be written back to the employee's record from
    // the generate dialog ("Also save to employee record").
    bool WritableToEmployee = false);

// The ONE list of merge fields — the editor's side panel reads it from
// GET /documents/merge-fields, template validation checks against it, and the
// resolver fills exactly these keys. Add a field here and in
// GeneratedLetterService.ResolveRecordValues, nowhere else.
//
// Besides these, a template may use ad-hoc {{input.someName}} fields: nothing
// on file answers them, so the generate dialog asks for each one.
public static class MergeFields
{
    public const string InputPrefix = "input.";

    public static readonly IReadOnlyList<MergeFieldDefinition> All =
    [
        new("employee.name", "Employee name", MergeFieldSource.Employee, MergeFieldKind.Text,
            "Full name, as on the employee's account."),
        new("employee.employeeNumber", "Employee ID", MergeFieldSource.Employee, MergeFieldKind.Text,
            "Staff number in this company."),
        new("employee.idNumber", "IC / passport no.", MergeFieldSource.Employee, MergeFieldKind.Text,
            "NRIC or passport number.", WritableToEmployee: true),
        new("employee.address", "Home address", MergeFieldSource.Employee, MergeFieldKind.Multiline,
            "Home address, one part per line.", WritableToEmployee: true),
        new("employee.email", "Employee email", MergeFieldSource.Employee, MergeFieldKind.Text,
            "Sign-in email address."),
        new("employee.jobTitle", "Job title", MergeFieldSource.Employee, MergeFieldKind.Text,
            "Job title in this company.", WritableToEmployee: true),
        new("employee.department", "Department", MergeFieldSource.Employee, MergeFieldKind.Text,
            "Department.", WritableToEmployee: true),
        new("employee.location", "Work location", MergeFieldSource.Employee, MergeFieldKind.Text,
            "Where they work.", WritableToEmployee: true),
        new("employee.employmentStatus", "Employment status", MergeFieldSource.Employee, MergeFieldKind.Text,
            "Permanent, contract, part-time …"),
        new("employee.joinDate", "Join date", MergeFieldSource.Employee, MergeFieldKind.Date,
            "First day of employment."),
        new("employee.leaveDate", "Leave date", MergeFieldSource.Employee, MergeFieldKind.Date,
            "Last day of employment, once set."),
        new("employee.probationMonths", "Probation (months)", MergeFieldSource.Employee, MergeFieldKind.Number,
            "Length of probation in months.", WritableToEmployee: true),
        new("employee.confirmationDate", "Confirmation date", MergeFieldSource.Employee, MergeFieldKind.Date,
            "Date employment is confirmed.", WritableToEmployee: true),
        new("employee.monthlySalary", "Monthly salary", MergeFieldSource.Employee, MergeFieldKind.Money,
            "Basic monthly salary, e.g. RM 4,500.00."),

        new("company.name", "Company name", MergeFieldSource.Company, MergeFieldKind.Text,
            "Registered company name."),
        new("company.registrationNo", "Company registration no.", MergeFieldSource.Company, MergeFieldKind.Text,
            "SSM registration number."),
        new("company.address", "Company address", MergeFieldSource.Company, MergeFieldKind.Multiline,
            "Registered address."),
        new("company.phone", "Company phone", MergeFieldSource.Company, MergeFieldKind.Text,
            "Office phone number."),
        new("company.email", "Company email", MergeFieldSource.Company, MergeFieldKind.Text,
            "Company email address."),

        new("signatory.name", "Signatory name", MergeFieldSource.Signatory, MergeFieldKind.Text,
            "Who signs the letter. Defaults to the declarant in Company Info."),
        new("signatory.position", "Signatory position", MergeFieldSource.Signatory, MergeFieldKind.Text,
            "The signatory's position, e.g. HR Manager."),

        new("today", "Letter date", MergeFieldSource.System, MergeFieldKind.Date,
            "The date printed on the letter. Today, unless changed."),
    ];

    private static readonly Dictionary<string, MergeFieldDefinition> ByKey =
        All.ToDictionary(f => f.Key, StringComparer.Ordinal);

    // Printed by the PDF on every letter (date line, signature block), so they
    // count as used even when the body never mentions them.
    public static readonly IReadOnlyList<string> AlwaysUsed =
        ["today", "company.name", "signatory.name", "signatory.position"];

    private static readonly Regex FieldPattern =
        new(@"\{\{\s*([A-Za-z0-9_.]+)\s*\}\}", RegexOptions.Compiled);

    private static readonly Regex InputPattern =
        new(@"^input\.[A-Za-z][A-Za-z0-9_]{0,59}$", RegexOptions.Compiled);

    public static Regex Pattern => FieldPattern;

    public static MergeFieldDefinition? Find(string key) => ByKey.GetValueOrDefault(key);

    public static bool IsInput(string key) => key.StartsWith(InputPrefix, StringComparison.Ordinal);

    public static bool IsKnown(string key) => ByKey.ContainsKey(key) || InputPattern.IsMatch(key);

    // Every distinct key the text mentions, in first-use order.
    public static IReadOnlyList<string> KeysIn(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        return FieldPattern.Matches(text)
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<string> UnknownKeysIn(string? text) =>
        KeysIn(text).Where(k => !IsKnown(k)).ToList();

    // The keys a letter from this body needs a value for: what it mentions,
    // then what every letter prints. Registry fields keep the body's order.
    public static IReadOnlyList<string> UsedKeys(string? body) =>
        KeysIn(body).Where(IsKnown).Concat(AlwaysUsed).Distinct(StringComparer.Ordinal).ToList();

    // An input field's definition, made up from its name:
    // input.lastWorkingDay → "Last working day", entered as a date.
    public static MergeFieldDefinition DescribeInput(string key)
    {
        var name = key[InputPrefix.Length..];
        var lower = name.ToLowerInvariant();
        var kind =
            lower.EndsWith("date") || lower.EndsWith("day") ? MergeFieldKind.Date
            : lower.EndsWith("amount") || lower.EndsWith("salary") ? MergeFieldKind.Money
            : MergeFieldKind.Text;
        return new MergeFieldDefinition(key, Humanise(name), MergeFieldSource.Input, kind,
            "Typed in for each letter.");
    }

    public static MergeFieldDefinition Describe(string key) =>
        Find(key) ?? DescribeInput(key);

    // lastWorkingDay / last_working_day → "Last working day".
    private static string Humanise(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '_') { sb.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append(' ');
            sb.Append(sb.Length == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }
        return sb.ToString().Trim();
    }

    // ─── Formatting ──────────────────────────────────────────────────────

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string FormatDate(DateTime date) => date.ToString("d MMMM yyyy", Invariant);

    public static string FormatMoney(decimal amount) => "RM " + amount.ToString("#,##0.00", Invariant);

    // What an admin typed, in letter form: an ISO date becomes "8 October 2026",
    // a number in a money field becomes "RM 1,234.50". Anything else is printed
    // as typed — "two (2) weeks" in a date field is the admin's call.
    public static string FormatTyped(MergeFieldKind kind, string typed)
    {
        var text = typed.Trim();
        switch (kind)
        {
            case MergeFieldKind.Date
                when DateTime.TryParseExact(text, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var d):
                return FormatDate(d);
            case MergeFieldKind.Money
                when decimal.TryParse(text.Replace(",", ""), NumberStyles.Number, Invariant, out var m):
                return FormatMoney(m);
            default:
                return text;
        }
    }
}
