using System.Globalization;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Reading a year of historical payroll out of a spreadsheet.
//
// This is what makes a mid-year migration possible: PCB annualises against
// the year to date, so an org that switches systems in July gets every
// remaining month wrong unless January through June are on file. The months
// are imported as SUBMITTED runs marked `IMPORTED`, and their figures are
// taken EXACTLY as typed — never recomputed. They were paid; what this
// engine would have calculated is beside the point.
//
// Sheet shape, one block per employee:
//
//     Employee Name | Personal ID | Basic Salary | PCB | ... (the header row,
//                                                  carrying YTD totals)
//     January       |             | 5000.00      | 110 | ...
//     February      |             | 5000.00      | 110 | ...
//     … twelve month rows …
//
// Pure: no EF, no clock. Byte-level column matching lives here so the
// service can stay about persistence.
public static class YtdImportParser
{
    // Matched case-insensitively with whitespace collapsed, so "Employee EPF"
    // and "employee  epf" are the same column. Order does not matter — the
    // header text is the contract, not the position.
    private static readonly IReadOnlyDictionary<string, string> MandatoryColumns =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["basic salary"] = nameof(YtdMonthAmounts.BasicSalary),
            ["pcb"] = nameof(YtdMonthAmounts.Pcb),
            ["employee epf"] = nameof(YtdMonthAmounts.EpfEmployee),
            ["employee socso"] = nameof(YtdMonthAmounts.SocsoEmployee),
            ["employee eis"] = nameof(YtdMonthAmounts.EisEmployee),
            ["employer epf"] = nameof(YtdMonthAmounts.EpfEmployer),
            ["employer socso"] = nameof(YtdMonthAmounts.SocsoEmployer),
            ["employer eis"] = nameof(YtdMonthAmounts.EisEmployer),
        };

    // Everything else routes through a payroll category, so an imported
    // bonus feeds next month's additional-remuneration base exactly as a
    // computed one would. Several spellings map to one category on purpose —
    // the sheet came from another system, not from us.
    private static readonly IReadOnlyDictionary<string, string> OptionalColumns =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bonus"] = PayrollAdjustmentCategories.WagesBonusAnnual,
            ["annual bonus"] = PayrollAdjustmentCategories.WagesBonusAnnual,
            ["commission"] = PayrollAdjustmentCategories.WagesCommission,
            ["overtime"] = PayrollAdjustmentCategories.WagesOvertime,
            ["ot"] = PayrollAdjustmentCategories.WagesOvertime,
            ["service charge"] = PayrollAdjustmentCategories.WagesServiceCharge,
            ["arrears"] = PayrollAdjustmentCategories.WagesArrears,
            ["director fee"] = PayrollAdjustmentCategories.WagesDirectorFee,
            ["travel allowance"] = PayrollAdjustmentCategories.AllowanceTravelPrivate,
            ["travel/petrol allowance"] = PayrollAdjustmentCategories.AllowanceTravelPrivate,
            ["petrol allowance"] = PayrollAdjustmentCategories.AllowanceTravelPrivate,
            ["parking allowance"] = PayrollAdjustmentCategories.AllowanceParking,
            ["meal allowance"] = PayrollAdjustmentCategories.AllowanceMeal,
            ["phone allowance"] = PayrollAdjustmentCategories.AllowancePhoneFixed,
            ["other allowance"] = PayrollAdjustmentCategories.AllowanceStandard,
        };

    // HRDF and zakat are employer/statutory columns rather than categories,
    // so they land on their own payslip fields like the mandatory set.
    private const string HrdfHeader = "hrdf";
    private const string ZakatHeader = "zakat";
    private const string Cp38Header = "cp38";

    private static readonly string[] MonthNames =
    [
        "january", "february", "march", "april", "may", "june",
        "july", "august", "september", "october", "november", "december",
    ];

    public sealed record Result(
        IReadOnlyList<YtdEmployeeRows> Employees,
        IReadOnlyList<string> Errors,
        // Headers present in the sheet that nothing recognised. Reported
        // rather than ignored: a mistyped "Bonis" column silently dropping a
        // year of bonuses is exactly the failure this import must not have.
        IReadOnlyList<string> UnrecognisedColumns)
    {
        public bool Ok => Errors.Count == 0;
    }

    public sealed record YtdEmployeeRows(
        string EmployeeName,
        string? PersonalId,
        // One entry per month that carried any figure at all. A month the
        // employee had not joined yet is simply absent.
        IReadOnlyList<YtdMonthAmounts> Months);

    public sealed record YtdMonthAmounts
    {
        public required int Month { get; init; }

        public decimal BasicSalary { get; init; }
        public decimal Pcb { get; init; }
        public decimal Cp38 { get; init; }
        public decimal Zakat { get; init; }
        public decimal EpfEmployee { get; init; }
        public decimal EpfEmployer { get; init; }
        public decimal SocsoEmployee { get; init; }
        public decimal SocsoEmployer { get; init; }
        public decimal EisEmployee { get; init; }
        public decimal EisEmployer { get; init; }
        public decimal Hrdf { get; init; }

        // Category code → amount, from the optional columns.
        public IReadOnlyDictionary<string, decimal> CategoryAmounts { get; init; }
            = new Dictionary<string, decimal>();

        // What the month paid in total, before deductions. The categories
        // that are non-cash are excluded, exactly as a computed payslip
        // excludes them.
        public decimal Gross =>
            BasicSalary + CategoryAmounts
                .Where(kv => PayrollAdjustmentCategories.Find(kv.Key)?.NonCash != true)
                .Sum(kv => kv.Value);

        public decimal Net =>
            Gross - (EpfEmployee + SocsoEmployee + EisEmployee + Pcb + Cp38 + Zakat);
    }

    public static Result Parse(byte[] content, TabularFormat format)
    {
        IReadOnlyList<IReadOnlyList<string>> rows;

        try
        {
            rows = TabularReader.Read(content, format);
        }
        catch (Exception ex)
        {
            return new Result([], [$"That file could not be read: {ex.Message}"], []);
        }

        if (rows.Count < 2)
        {
            return new Result([], ["The sheet is empty."], []);
        }

        var headerIndex = FindHeaderRow(rows);
        if (headerIndex < 0)
        {
            return new Result([], [
                "No header row found. The sheet needs a row containing 'Employee Name' and "
                + "'Basic Salary'.",
            ], []);
        }

        var header = rows[headerIndex];
        var (columns, unrecognised) = MapColumns(header);

        // WHICH column holds the employee name is read from the header too.
        // That column does double duty: on a block's first row it carries the
        // name, and on the twelve rows under it the month. Detecting the row
        // TYPE by position would mean column order only appeared not to
        // matter — reorder the sheet and every row would be misread.
        var nameColumn = IndexOf(header, "employee name");
        var idColumn = IndexOf(header, "personal id");

        var missing = MandatoryColumns.Values
            .Where(field => !columns.Values.Any(c => c.Field == field))
            .ToList();

        if (missing.Count > 0)
        {
            var labels = MandatoryColumns
                .Where(kv => missing.Contains(kv.Value))
                .Select(kv => kv.Key);

            return new Result([], [
                "These required columns are missing: " + string.Join(", ", labels) + ".",
            ], unrecognised);
        }

        return ReadEmployees(rows, headerIndex, nameColumn, idColumn, columns, unrecognised);
    }

    // The header row is wherever "employee name" and "basic salary" both
    // appear — the template carries title and instruction rows above it, and
    // an admin may well add their own.
    private static int FindHeaderRow(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        for (var i = 0; i < Math.Min(rows.Count, 20); i++)
        {
            var cells = rows[i].Select(Normalise).ToList();

            if (cells.Contains("employee name") && cells.Contains("basic salary")) return i;
        }

        return -1;
    }

    private sealed record Column(string Field, bool IsCategory);

    private static int IndexOf(IReadOnlyList<string> header, string name)
    {
        for (var i = 0; i < header.Count; i++)
        {
            if (Normalise(header[i]) == name) return i;
        }

        return -1;
    }

    private static (Dictionary<int, Column> Columns, List<string> Unrecognised) MapColumns(
        IReadOnlyList<string> header)
    {
        var columns = new Dictionary<int, Column>();
        var unrecognised = new List<string>();

        for (var i = 0; i < header.Count; i++)
        {
            var name = Normalise(header[i]);
            if (name.Length == 0) continue;

            // The two identity columns are positional in every other sense
            // but are still matched by name.
            if (name is "employee name" or "personal id" or "month") continue;

            if (MandatoryColumns.TryGetValue(name, out var field))
            {
                columns[i] = new Column(field, IsCategory: false);
                continue;
            }

            if (name == HrdfHeader) { columns[i] = new(nameof(YtdMonthAmounts.Hrdf), false); continue; }
            if (name == ZakatHeader) { columns[i] = new(nameof(YtdMonthAmounts.Zakat), false); continue; }
            if (name == Cp38Header) { columns[i] = new(nameof(YtdMonthAmounts.Cp38), false); continue; }

            if (OptionalColumns.TryGetValue(name, out var category))
            {
                columns[i] = new Column(category, IsCategory: true);
                continue;
            }

            // A column nobody recognised. Reported, never silently dropped.
            unrecognised.Add(header[i].Trim());
        }

        return (columns, unrecognised);
    }

    private static Result ReadEmployees(
        IReadOnlyList<IReadOnlyList<string>> rows,
        int headerIndex,
        int nameColumn,
        int idColumn,
        Dictionary<int, Column> columns,
        List<string> unrecognised)
    {
        var employees = new List<YtdEmployeeRows>();
        var errors = new List<string>();

        string? currentName = null;
        string? currentId = null;
        var months = new List<YtdMonthAmounts>();

        void Flush()
        {
            if (currentName is null) return;

            employees.Add(new YtdEmployeeRows(currentName, currentId, [.. months]));
            months = [];
        }

        for (var r = headerIndex + 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.Count == 0) continue;

            if (row.All(c => c.Trim().Length == 0)) continue;

            var label = nameColumn < row.Count ? row[nameColumn].Trim() : string.Empty;
            if (label.Length == 0) continue;

            var monthIndex = MonthIndex(label);

            if (monthIndex is null)
            {
                // A non-month first cell starts a new employee block. The
                // header row of a block carries YTD totals we deliberately
                // ignore — they are for the admin's eye, and recomputing
                // them from the month rows is what makes the import
                // self-consistent.
                Flush();
                currentName = label;
                currentId = idColumn >= 0 && idColumn < row.Count ? NullIfBlank(row[idColumn]) : null;
                continue;
            }

            if (currentName is null)
            {
                errors.Add($"Row {r + 1}: a month row appears before any employee name.");
                continue;
            }

            var amounts = ReadMonth(row, monthIndex.Value, columns, r, errors);

            // A month with nothing in it is a month the employee was not
            // paid — before they joined, say. Skipped rather than imported
            // as a zero payslip nobody was given.
            if (amounts is not null && amounts.Gross > 0m) months.Add(amounts);
        }

        Flush();

        if (employees.Count == 0 && errors.Count == 0)
        {
            errors.Add("No employees found in the sheet.");
        }

        return new Result(employees, errors, unrecognised);
    }

    private static YtdMonthAmounts? ReadMonth(
        IReadOnlyList<string> row,
        int month,
        Dictionary<int, Column> columns,
        int rowNumber,
        List<string> errors)
    {
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var categories = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var (index, column) in columns)
        {
            if (index >= row.Count) continue;

            var raw = row[index];
            if (string.IsNullOrWhiteSpace(raw)) continue;

            if (!TryAmount(raw, out var amount))
            {
                errors.Add($"Row {rowNumber + 1}: '{raw.Trim()}' is not a number.");
                continue;
            }

            if (column.IsCategory)
            {
                // Two headers can map to one category — sum rather than
                // overwrite, or the second column silently wins.
                categories.TryGetValue(column.Field, out var running);
                categories[column.Field] = running + amount;
            }
            else
            {
                values[column.Field] = amount;
            }
        }

        decimal Get(string field) => values.GetValueOrDefault(field);

        return new YtdMonthAmounts
        {
            Month = month,
            BasicSalary = Get(nameof(YtdMonthAmounts.BasicSalary)),
            Pcb = Get(nameof(YtdMonthAmounts.Pcb)),
            Cp38 = Get(nameof(YtdMonthAmounts.Cp38)),
            Zakat = Get(nameof(YtdMonthAmounts.Zakat)),
            EpfEmployee = Get(nameof(YtdMonthAmounts.EpfEmployee)),
            EpfEmployer = Get(nameof(YtdMonthAmounts.EpfEmployer)),
            SocsoEmployee = Get(nameof(YtdMonthAmounts.SocsoEmployee)),
            SocsoEmployer = Get(nameof(YtdMonthAmounts.SocsoEmployer)),
            EisEmployee = Get(nameof(YtdMonthAmounts.EisEmployee)),
            EisEmployer = Get(nameof(YtdMonthAmounts.EisEmployer)),
            Hrdf = Get(nameof(YtdMonthAmounts.Hrdf)),
            CategoryAmounts = categories,
        };
    }

    // Tolerant on purpose: the sheet came from another payroll system or a
    // hand-kept spreadsheet, and "RM 5,000.00" is a number an admin typed in
    // good faith.
    private static bool TryAmount(string raw, out decimal amount)
    {
        var cleaned = new string([.. raw
            .Where(c => char.IsDigit(c) || c is '.' or '-')]);

        if (cleaned.Length == 0 || cleaned == "-")
        {
            amount = 0m;
            return true;
        }

        return decimal.TryParse(
            cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }

    // "January", "Jan", "1", "01" all read as month 1.
    private static int? MonthIndex(string cell)
    {
        var name = Normalise(cell);
        if (name.Length == 0) return null;

        for (var i = 0; i < MonthNames.Length; i++)
        {
            if (name == MonthNames[i] || name == MonthNames[i][..3]) return i + 1;
        }

        return int.TryParse(name, out var number) && number is >= 1 and <= 12 ? number : null;
    }

    private static string Normalise(string raw) =>
        string.Join(' ', raw.ToLowerInvariant()
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // The template's column order, so the generated sheet and the parser
    // cannot disagree about what a valid file looks like.
    public static IReadOnlyList<string> TemplateHeaders() =>
    [
        "Employee Name", "Personal ID",
        "Basic Salary", "PCB", "CP38", "Zakat",
        "Employee EPF", "Employer EPF",
        "Employee SOCSO", "Employer SOCSO",
        "Employee EIS", "Employer EIS",
        "HRDF",
        "Bonus", "Commission", "Overtime", "Other Allowance",
    ];

    public static IReadOnlyList<string> MonthLabels() =>
        [.. MonthNames.Select(m => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(m))];
}
