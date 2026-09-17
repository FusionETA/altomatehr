using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Shifts;

namespace AltomateHR.Api.Modules.Employees;

// Bulk onboarding. Creates the account and the org membership, or fills in the
// record of someone already there.
//
// Everything goes through IEmployeeService, never the repositories: email
// uniqueness, "already a member of this org", the policy and shift assignment
// and the join-date accrual recompute all live there, and a second copy of
// those rules reachable only by spreadsheet is exactly how the two drift.
public class EmployeeImportService : IEmployeeImportService
{
    private readonly IEmployeeService _employees;
    private readonly IPolicyService _policies;
    private readonly IShiftService _shifts;

    public EmployeeImportService(
        IEmployeeService employees, IPolicyService policies, IShiftService shifts)
    {
        _employees = employees;
        _policies = policies;
        _shifts = shifts;
    }

    public TabularExportResult BuildTemplate(TabularFormat format) =>
        TabularExportResult.From(
            EmployeeImportSheet.BuildTemplate(), format, "employees-import-template");

    public async Task<EmployeeImportResult> ImportAsync(byte[] content, TabularFormat format)
    {
        IReadOnlyList<IReadOnlyList<string>> rows;
        try
        {
            rows = TabularReader.Read(content, format);
        }
        catch (InvalidDataException ex)
        {
            return EmployeeImportResult.FileError(ex.Message);
        }

        if (rows.Count == 0) return EmployeeImportResult.FileError("The file is empty.");

        var (map, missing) = TabularHeaderMap.Build(rows[0], EmployeeImportSheet.Columns);
        if (map is null)
            return EmployeeImportResult.FileError(
                $"Missing required column(s): {string.Join(", ", missing)}.");
        if (rows.Count == 1)
            return EmployeeImportResult.FileError("The file has a header row but no data rows.");

        // Names, not ids, because that is what an admin types. Resolved once.
        var policies = (await _policies.GetAllAsync())
            .ToDictionary(p => p.Name.Trim(), p => p.Id, StringComparer.OrdinalIgnoreCase);
        var shifts = (await _shifts.GetAllAsync())
            .ToDictionary(s => s.Name.Trim(), s => s.Id, StringComparer.OrdinalIgnoreCase);
        var existing = (await _employees.GetAllAsync())
            .ToDictionary(e => e.Email.Trim(), e => e, StringComparer.OrdinalIgnoreCase);

        var errors = new List<TabularImportError>();
        var createdAccounts = new List<CreatedAccount>();
        var created = 0;
        var updated = 0;

        for (var i = 1; i < rows.Count; i++)
        {
            var rowNumber = i + 1;   // header is row 1, as the admin sees it
            var row = rows[i];

            if (TabularTemplate.IsExampleRow(map, row, EmployeeImportSheet.Columns)) continue;

            var email = TabularCell.Text(map.Cell(row, EmployeeImportSheet.EmailKey));
            if (email is null) continue;   // a blank spacer line, not an error

            var policyId = Lookup(
                map, row, EmployeeImportSheet.PolicyKey, policies, "policy", rowNumber, errors);
            var shiftId = Lookup(
                map, row, EmployeeImportSheet.ShiftKey, shifts, "shift", rowNumber, errors);
            if (policyId.Failed || shiftId.Failed) continue;

            var name = TabularCell.Text(map.Cell(row, EmployeeImportSheet.NameKey), 160);
            var role = TabularCell.Text(map.Cell(row, EmployeeImportSheet.RoleKey), 20);
            var number = TabularCell.Text(map.Cell(row, EmployeeImportSheet.EmployeeNumberKey), 40);
            var jobTitle = TabularCell.Text(map.Cell(row, EmployeeImportSheet.JobTitleKey), 120);
            var joinDate = TabularCell.Date(map.Cell(row, EmployeeImportSheet.JoinDateKey));

            if (existing.TryGetValue(email, out var member))
            {
                // A blank cell LEAVES THE FIELD ALONE, so every unset value is
                // carried over from what the person already has.
                //
                // This is the opposite of the adjustment import's replace
                // semantics, and deliberately so: that one owns a set of lines,
                // this one edits people. Sending the DTO's own defaults instead
                // would reset an employee's policy to the org default and wipe
                // an admin's module grants, neither of which is in the file.
                var save = new UpdateEmployeeDto
                {
                    Name = name,                                   // null → unchanged
                    Email = null,                                  // matched on it; never rewritten
                    EmployeeNumber = number ?? member.EmployeeNumber,
                    JobTitle = jobTitle ?? member.JobTitle,
                    JoinDate = joinDate ?? member.JoinDate,
                    Role = role ?? member.Role,
                    PolicyId = policyId.Id ?? member.PolicyId,
                    ShiftId = shiftId.Id ?? member.ShiftId,
                    Modules = member.Modules?.ToList(),
                };

                var result = await _employees.UpdateAsync(member.Id, save);
                if (!result.Ok)
                {
                    errors.Add(new TabularImportError(rowNumber, result.Error ?? "Could not update."));
                    continue;
                }

                updated++;
                continue;
            }

            // A new account needs a name; the service refuses without one, and
            // saying so here names the row rather than the person.
            if (name is null)
            {
                errors.Add(new TabularImportError(
                    rowNumber, $"\"{email}\" is new, so the Name column is required."));
                continue;
            }

            var password = EmployeeImportSheet.GeneratePassword();
            var create = await _employees.CreateAsync(new CreateEmployeeDto
            {
                Email = email,
                Name = name,
                Password = password,
                Role = role ?? "Employee",
                EmployeeNumber = number,
                JobTitle = jobTitle,
                JoinDate = joinDate,
                PolicyId = policyId.Id,
                ShiftId = shiftId.Id,
            });

            if (!create.Ok)
            {
                errors.Add(new TabularImportError(rowNumber, create.Error ?? "Could not create."));
                continue;
            }

            created++;
            createdAccounts.Add(new CreatedAccount(email, name, password));
        }

        // Rows are applied as they are read rather than held back until the end:
        // unlike the adjustment import, which owns a whole run's set of lines,
        // each row here is an independent person. Failing all twenty because one
        // has a typo would be worse than onboarding nineteen and naming the one.
        return new EmployeeImportResult
        {
            Ok = errors.Count == 0,
            Created = created,
            Updated = updated,
            Errors = errors,
            CreatedAccounts = createdAccounts,
        };
    }

    // A name the org does not have is a mistake worth stopping on: silently
    // leaving the person on the default policy is how someone ends up on the
    // wrong leave entitlement for a year.
    private static (string? Id, bool Failed) Lookup(
        TabularHeaderMap map,
        IReadOnlyList<string> row,
        string key,
        IReadOnlyDictionary<string, string> byName,
        string what,
        int rowNumber,
        List<TabularImportError> errors)
    {
        var name = TabularCell.Text(map.Cell(row, key));
        if (name is null) return (null, false);

        if (byName.TryGetValue(name, out var id)) return (id, false);

        errors.Add(new TabularImportError(
            rowNumber,
            $"No {what} named \"{name}\". Use one of: {string.Join(", ", byName.Keys)}."));
        return (null, true);
    }
}
