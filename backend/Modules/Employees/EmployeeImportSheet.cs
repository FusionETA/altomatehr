using System.Security.Cryptography;
using AltomateHR.Api.Common.Tabular;

namespace AltomateHR.Api.Modules.Employees;

// Bulk-adding people to an org: the roster columns Manage Employee shows, plus
// the two assignments it lets you set.
//
// Distinct from Payroll → Employees import, which fills in PAYROLL fields for
// people who already exist and refuses a row it cannot match. This one is the
// step before that: it creates the account and the membership. Onboarding
// twenty hires should not mean opening the Add employee dialog twenty times.
public static class EmployeeImportSheet
{
    public const string SheetName = "Employees";

    public const string EmailKey = "email";
    public const string NameKey = "name";
    public const string RoleKey = "role";
    public const string EmployeeNumberKey = "employeeNumber";
    public const string JobTitleKey = "jobTitle";
    public const string JoinDateKey = "joinDate";
    public const string PolicyKey = "policy";
    public const string ShiftKey = "shift";

    // Email is the only required column: it is the identity the row is matched
    // on, and everything else has a sensible absence.
    public static readonly IReadOnlyList<TabularColumn> Columns =
    [
        new(EmailKey, "Email", true, "aisyah@example.com", ["employee email"]),
        new(NameKey, "Name", false, "Aisyah Binti Rahman", ["employee name", "full name"]),
        new(RoleKey, "Role", false, "Employee", ["access"]),
        new(EmployeeNumberKey, "Employee No", false, "EMP-001", ["employee number", "staff id"]),
        new(JobTitleKey, "Job Title", false, "Site Engineer", ["position"]),
        new(JoinDateKey, "Join Date", false, "2026-01-15", ["start date"]),

        // Matched by NAME, not id: an admin filling a spreadsheet knows
        // "Full-time", not a cuid. An unrecognised name fails the row rather
        // than silently leaving the person on the default.
        new(PolicyKey, "Policy", false, "Full-time", ["leave policy"]),
        new(ShiftKey, "Shift", false, "Office Hours", ["work shift"]),
    ];

    public static TabularSheet BuildTemplate() =>
        TabularTemplate.Build(SheetName, Columns);

    // Mirrors the frontend's generator in AddEmployeeModal: twelve characters
    // from an alphabet with no 0/O/1/l/I, because this gets read off a screen
    // and typed by someone else.
    //
    // Generated per person rather than derived from anything about them. The
    // reference app builds it from the employee's email and date of birth,
    // which makes every account guessable by anyone who knows the person.
    private const string PasswordAlphabet =
        "ABCDEFGHJKMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

    public static string GeneratePassword()
    {
        var chars = new char[12];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
        }
        return new string(chars);
    }
}
