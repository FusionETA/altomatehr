using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Leave.Dtos;

namespace AltomateHR.Api.Modules.Leave;

// The leave balances summary as a spreadsheet, plus the column contract for
// importing historical leave applications.
//
// Replaces the older CSV-only LeaveCsvExporter. Two things changed with it:
//   - CSV and XLSX now come off ONE definition (TabularWriter renders it), so
//     the two formats can't drift apart;
//   - the single-employee export no longer opens with a bare "Employee,<email>"
//     row above the header. That row broke naive parsers — its own comment said
//     so — and identity is now just the two leading columns that the org-wide
//     export already had. One shape, both files.
public static class LeaveBalancesSheet
{
    public const string SheetName = "Leave Balances";
    public const string ImportSheetName = "Leave History Import";

    private static readonly string[] Headers =
    [
        "Employee", "Role", "Code", "Leave Type", "Paid", "Year", "Accrual Method",
        "Opened", "Entitled Days", "Accrued Days", "Carried Days", "Carry Expires",
        "Carry Expired", "Taken Days", "Pending Days", "Remaining Days",
    ];

    // ---- Export ----

    // One employee. Their identity repeats down the leading columns, which is
    // what makes this file and the org-wide one interchangeable to a consumer.
    public static TabularSheet BuildBalances(
        IEnumerable<LeaveBalanceDto> balances, string employeeLabel, string role = "")
    {
        var sheet = new TabularSheet(SheetName, Headers);
        foreach (var balance in balances)
            AppendBalance(sheet, employeeLabel, role, balance);
        return sheet;
    }

    // Every employee in the org, one row per employee per leave type.
    public static TabularSheet BuildOrgBalances(IEnumerable<EmployeeLeaveBalancesDto> employees)
    {
        var sheet = new TabularSheet(SheetName, Headers);
        foreach (var employee in employees)
            foreach (var balance in employee.Balances)
                AppendBalance(sheet, employee.Email, employee.Role, balance);
        return sheet;
    }

    private static void AppendBalance(
        TabularSheet sheet, string employeeLabel, string role, LeaveBalanceDto b) =>
        sheet.AddRow(
            employeeLabel,
            role,
            b.Code,
            b.Name,
            TabularSheet.Bool(b.Paid),
            TabularSheet.Number(b.Year),
            b.AccrualMethod,
            TabularSheet.Bool(b.IsOpened),
            TabularSheet.Number(b.EntitlementDays),
            TabularSheet.Number(b.AccruedDays),
            TabularSheet.Number(b.CarriedDays),
            TabularSheet.Date(b.CarriedExpiresAt),
            TabularSheet.Bool(b.CarriedExpired),
            TabularSheet.Number(b.TakenDays),
            TabularSheet.Number(b.PendingDays),
            TabularSheet.Number(b.RemainingDays));

    // ---- Import ----
    //
    // Historical leave APPLICATIONS, not balances. That asymmetry is deliberate
    // and matches production: `taken` is derived from approved applications
    // (see LeaveService), so importing the requests reconstructs the balances
    // for free — whereas importing balance numbers directly would create
    // figures no application backs, which then disagree with the audit trail.
    //
    // The entitlement side of a migration (annual days, carry-forward) is
    // separate config, set through /leave/entitlements.
    public static readonly IReadOnlyList<TabularColumn> ImportColumns =
    [
        new("employeeEmail", "Employee Email", true, "ahmad@company.com",
            ["email", "staff email"]),
        new("employeeName", "Employee Name", false, "Ahmad Ali",
            ["name", "full name", "employee", "member"]),
        new("leaveType", "Leave Type", true, "Annual Leave",
            ["type", "leave type name", "leave code"]),
        new("startDate", "Start Date", true, "2026-01-15",
            ["from", "start"]),
        new("endDate", "End Date", true, "2026-01-16",
            ["to", "end"]),
        new("days", "Days", true, "2",
            ["total days", "no of days", "duration days"]),
        new("status", "Status", true, "APPROVED"),
        new("reason", "Reason", false, "Family matter",
            ["remark", "notes", "comment"]),
    ];

    public static TabularSheet BuildImportTemplate() =>
        TabularTemplate.Build(ImportSheetName, ImportColumns);
}
