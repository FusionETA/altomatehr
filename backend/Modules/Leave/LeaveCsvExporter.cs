using System.Globalization;
using System.Text;
using AltomateHR.Api.Modules.Leave.Dtos;

namespace AltomateHR.Api.Modules.Leave;

// Renders leave balances as CSV. Kept separate from the service so the export
// FORMAT can change (or gain a PDF sibling) without touching the balance rules.
//
// Production emits PDF here; CSV is the V2 stand-in — same data, no rendering
// dependency, and it opens straight in Excel for payroll.
public static class LeaveCsvExporter
{
    private static readonly string[] Headers =
    [
        "Code", "Leave Type", "Paid", "Year", "Accrual Method", "Opened",
        "Entitled Days", "Accrued Days", "Carried Days", "Carry Expires",
        "Carry Expired", "Taken Days", "Pending Days", "Remaining Days",
    ];

    public static byte[] BalancesToCsv(IEnumerable<LeaveBalanceDto> balances, string? employeeLabel = null)
    {
        var sb = new StringBuilder();

        // A leading comment row would break naive parsers, so identity goes in
        // its own labelled column pair instead.
        if (employeeLabel is not null)
            sb.Append(Cell("Employee")).Append(',').Append(Cell(employeeLabel)).Append('\n');

        sb.AppendJoin(',', Headers).Append('\n');

        foreach (var b in balances)
        {
            AppendBalance(sb, b);
            sb.Append('\n');
        }

        return WithBom(sb);
    }

    private static void AppendBalance(StringBuilder sb, LeaveBalanceDto b) =>
        sb.AppendJoin(',',
            Cell(b.Code),
            Cell(b.Name),
            Cell(b.Paid ? "Yes" : "No"),
            Num(b.Year),
            Cell(b.AccrualMethod),
            Cell(b.IsOpened ? "Yes" : "No"),
            Num(b.EntitlementDays),
            Num(b.AccruedDays),
            Num(b.CarriedDays),
            Cell(b.CarriedExpiresAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? ""),
            Cell(b.CarriedExpired ? "Yes" : "No"),
            Num(b.TakenDays),
            Num(b.PendingDays),
            Num(b.RemainingDays));

    // UTF-8 BOM so Excel opens non-ASCII names correctly instead of mojibake.
    private static byte[] WithBom(StringBuilder sb) =>
        Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();

    // Org-wide grid: the same columns, prefixed by who the row belongs to, so
    // one file covers every employee instead of one download per head.
    public static byte[] OrgBalancesToCsv(IEnumerable<EmployeeLeaveBalancesDto> employees)
    {
        var sb = new StringBuilder();
        sb.Append("Employee,Role,").AppendJoin(',', Headers).Append('\n');

        foreach (var e in employees)
            foreach (var b in e.Balances)
            {
                sb.Append(Cell(e.Email)).Append(',').Append(Cell(e.Role)).Append(',');
                AppendBalance(sb, b);
                sb.Append('\n');
            }

        return WithBom(sb);
    }

    // RFC 4180: quote when the value contains a comma, quote or newline, and
    // escape embedded quotes by doubling them.
    private static string Cell(string value)
    {
        if (!value.Any(c => c is ',' or '"' or '\n' or '\r')) return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
}
