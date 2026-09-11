using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class PayrollEmployeeImportService : IPayrollEmployeeImportService
{
    private readonly IEmployeeProfileRepository _profiles;
    private readonly IEmployeeRowResolver _employees;
    private readonly IDirectoryService _directory;
    private readonly IAuditService _audit;

    public PayrollEmployeeImportService(
        IEmployeeProfileRepository profiles,
        IEmployeeRowResolver employees,
        IDirectoryService directory,
        IAuditService audit)
    {
        _profiles = profiles;
        _employees = employees;
        _directory = directory;
        _audit = audit;
    }

    public TabularExportResult BuildTemplate(TabularFormat format) =>
        TabularExportResult.From(
            PayrollEmployeeSheet.Template(), format, "payroll-employees-template");

    // The same shape the import reads, so an admin can export, edit in a
    // spreadsheet, and bring it back. A round trip that changed shape in the
    // middle would not be one.
    public async Task<TabularExportResult> ExportAsync(TabularFormat format)
    {
        var columns = PayrollEmployeeSheet.ImportColumns;
        var sheet = new TabularSheet(
            "Payroll employees", [.. columns.Select(c => c.Label)]);

        var profiles = await _directory.GetProfilesForCurrentOrgAsync();
        var users = (await _directory.GetUsersAsync())
            .ToDictionary(u => u.Id, u => u, StringComparer.Ordinal);

        foreach (var profile in profiles.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            users.TryGetValue(profile.UserId, out var user);

            sheet.AddRow(
            [
                user?.Email ?? string.Empty,
                user?.Name ?? string.Empty,
                profile.IdNumber ?? string.Empty,
                profile.IdType?.ToString() ?? string.Empty,
                profile.Nationality ?? string.Empty,
                profile.Gender?.ToString() ?? string.Empty,
                profile.MaritalStatus?.ToString() ?? string.Empty,
                Date(profile.DateOfBirth),
                Date(profile.JoinDate),
                Date(profile.LeaveDate),
                profile.Department ?? string.Empty,
                profile.SalaryType.ToString(),
                Amount(profile.MonthlySalary),
                Amount(profile.HourlyRate),
                profile.EpfNumber ?? string.Empty,
                profile.EpfEmployeeRate.ToString("0.##"),
                profile.ContributeToEpf ? "Yes" : "No",
                profile.SocsoNumber ?? string.Empty,
                profile.ContributeToEis ? "Yes" : "No",
                profile.IncomeTaxNumber ?? string.Empty,
                profile.BankName ?? string.Empty,
                profile.BankAccountNumber ?? string.Empty,
                profile.BankAccountHolderName ?? string.Empty,
            ]);
        }

        return TabularExportResult.From(sheet, format, "payroll-employees");
    }

    public async Task<TabularImportResult> ImportAsync(byte[] content, TabularFormat format)
    {
        IReadOnlyList<IReadOnlyList<string>> rows;

        try
        {
            rows = TabularReader.Read(content, format);
        }
        catch (InvalidDataException ex)
        {
            return TabularImportResult.FileError(ex.Message);
        }

        if (rows.Count == 0) return TabularImportResult.FileError("The file is empty.");

        var columns = PayrollEmployeeSheet.ImportColumns;
        var (map, missing) = TabularHeaderMap.Build(
            rows[0], columns, EmployeeImportColumns.IdentityGroup);

        if (map is null)
        {
            return TabularImportResult.FileError(
                $"Missing required column(s): {string.Join(", ", missing)}.");
        }

        if (rows.Count == 1)
        {
            return TabularImportResult.FileError("The file has a header row but no data rows.");
        }

        var result = new TabularImportResult();
        var employees = await _employees.GetSnapshotAsync();

        var profilesByUser = (await _directory.GetProfilesForCurrentOrgAsync())
            .ToDictionary(p => p.UserId, p => p, StringComparer.Ordinal);

        var updated = 0;

        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 1;   // 1-based with the header, matching the spreadsheet

            if (TabularTemplate.IsExampleRow(map, row, columns))
            {
                result.CountSkipped();
                continue;
            }

            var email = map.Cell(row, EmployeeImportColumns.EmailKey);
            var name = map.Cell(row, EmployeeImportColumns.NameKey);
            var (userId, ambiguous) = employees.Resolve(email, name);

            if (ambiguous)
            {
                result.Fail(rowNumber,
                    $"More than one employee is named '{name}'. Use the Employee Email column.");
                continue;
            }

            if (userId is null)
            {
                result.Fail(rowNumber,
                    $"No employee in this organization matches "
                    + $"'{(email.Length > 0 ? email : name)}'.");
                continue;
            }

            if (!profilesByUser.TryGetValue(userId, out var profile))
            {
                // A member with no payroll profile yet. Created here rather
                // than refused: the whole point of the import is filling
                // these in for a roster that has none.
                profile = new EmployeeProfile { UserId = userId };
                profile = await _profiles.AddAsync(profile);
                profilesByUser[userId] = profile;
            }

            if (!Apply(map, row, profile, rowNumber, result)) continue;

            await _profiles.UpdateAsync(profile);
            result.CountImported();
            updated++;
        }

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollEmployeeImport,
            $"Imported payroll details for {updated} employee(s)",
            TargetType: "EmployeeProfile",
            Metadata: new { Updated = updated, result.Skipped, Failed = result.Errors.Count }));

        return result;
    }

    // A blank cell leaves the field UNCHANGED. That is what makes a partial
    // sheet — "here are everyone's bank details" — safe to import over a
    // roster that already has statutory numbers on it.
    private static bool Apply(
        TabularHeaderMap map,
        IReadOnlyList<string> row,
        EmployeeProfile profile,
        int rowNumber,
        TabularImportResult result)
    {
        string? Text(string key, int max) => TabularCell.Text(map.Cell(row, key), max);

        profile.IdNumber = Text("idNumber", 40) ?? profile.IdNumber;
        profile.Nationality = Text("nationality", 60) ?? profile.Nationality;
        profile.Department = Text("department", 120) ?? profile.Department;
        profile.EpfNumber = Text("epfNumber", 40) ?? profile.EpfNumber;
        profile.SocsoNumber = Text("socsoNumber", 40) ?? profile.SocsoNumber;
        profile.IncomeTaxNumber = Text("incomeTaxNumber", 40) ?? profile.IncomeTaxNumber;
        profile.BankName = Text("bankName", 120) ?? profile.BankName;
        profile.BankAccountNumber = Text("bankAccountNumber", 60) ?? profile.BankAccountNumber;
        profile.BankAccountHolderName =
            Text("bankAccountHolderName", 120) ?? profile.BankAccountHolderName;

        profile.IdType = TabularCell.Enum<IdType>(map.Cell(row, "idType")) ?? profile.IdType;
        profile.Gender = TabularCell.Enum<Gender>(map.Cell(row, "gender")) ?? profile.Gender;
        profile.MaritalStatus =
            TabularCell.Enum<MaritalStatus>(map.Cell(row, "maritalStatus")) ?? profile.MaritalStatus;
        profile.SalaryType =
            TabularCell.Enum<SalaryType>(map.Cell(row, "salaryType")) ?? profile.SalaryType;

        profile.DateOfBirth = TabularCell.Date(map.Cell(row, "dateOfBirth")) ?? profile.DateOfBirth;
        profile.JoinDate = TabularCell.Date(map.Cell(row, "joinDate")) ?? profile.JoinDate;
        profile.LeaveDate = TabularCell.Date(map.Cell(row, "leaveDate")) ?? profile.LeaveDate;

        profile.ContributeToEpf = Flag(map, row, "contributeToEpf") ?? profile.ContributeToEpf;
        profile.ContributeToEis = Flag(map, row, "contributeToEis") ?? profile.ContributeToEis;

        // Money is the one thing that gets reported rather than shrugged off:
        // a mistyped salary silently ignored is someone paid the wrong amount
        // with nothing on screen to say why.
        if (!TryAmount(map, row, "monthlySalary", rowNumber, result, out var monthly)) return false;
        if (!TryAmount(map, row, "hourlyRate", rowNumber, result, out var hourly)) return false;
        if (!TryAmount(map, row, "epfEmployeeRate", rowNumber, result, out var epfRate)) return false;

        if (monthly is not null) profile.MonthlySalary = monthly;
        if (hourly is not null) profile.HourlyRate = hourly;
        if (epfRate is not null) profile.EpfEmployeeRate = epfRate.Value;

        profile.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    private static bool TryAmount(
        TabularHeaderMap map,
        IReadOnlyList<string> row,
        string key,
        int rowNumber,
        TabularImportResult result,
        out decimal? amount)
    {
        amount = null;

        var raw = map.Cell(row, key).Trim();
        if (raw.Length == 0) return true;

        // Tolerant of what a spreadsheet produces — "RM 5,000.00" is a number
        // an admin typed in good faith.
        var cleaned = new string([.. raw.Where(c => char.IsDigit(c) || c is '.' or '-')]);

        if (!decimal.TryParse(
                cleaned,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed))
        {
            result.Fail(rowNumber, $"'{raw}' is not a valid amount.");
            return false;
        }

        if (parsed < 0m)
        {
            result.Fail(rowNumber, $"'{raw}' cannot be negative.");
            return false;
        }

        amount = parsed;
        return true;
    }

    // Blank leaves the flag alone; anything else is read generously, because
    // the sheet came from a human or another system.
    private static bool? Flag(TabularHeaderMap map, IReadOnlyList<string> row, string key)
    {
        var raw = map.Cell(row, key).Trim().ToLowerInvariant();

        return raw switch
        {
            "" => null,
            "yes" or "y" or "true" or "1" => true,
            "no" or "n" or "false" or "0" => false,
            _ => null,
        };
    }

    private static string Date(DateTime? value) =>
        value?.ToString("yyyy-MM-dd") ?? string.Empty;

    private static string Amount(decimal? value) =>
        value?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
}
