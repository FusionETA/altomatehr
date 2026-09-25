using System.Globalization;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;   // SalaryType
using AltomateHR.Api.Modules.Shifts;

namespace AltomateHR.Api.Modules.Employees;

// The one employee spreadsheet: creates people, and bulk-updates everything on
// their record — membership, profile, statutory and salary details.
//
// Everything goes through services, never the repositories: IEmployeeService
// owns email uniqueness, "already a member", role rules, policy/shift and the
// join-date accrual recompute; IEmployeeProfileService owns the salary-history
// row and marking payroll drafts stale. A second copy of those rules reachable
// only by spreadsheet is exactly how the two drift.
//
// Each row is read and checked IN FULL before anything is saved, so a row that
// fails leaves that person exactly as they were. (The payroll-details import
// this replaced mutated the tracked profile before its amount checks, and the
// next row's save persisted the half-applied changes.)
public class EmployeeImportService : IEmployeeImportService
{
    private readonly IEmployeeService _employees;
    private readonly IEmployeeProfileService _profiles;
    private readonly IDirectoryService _directory;
    private readonly IPolicyService _policies;
    private readonly IShiftService _shifts;

    public EmployeeImportService(
        IEmployeeService employees,
        IEmployeeProfileService profiles,
        IDirectoryService directory,
        IPolicyService policies,
        IShiftService shifts)
    {
        _employees = employees;
        _profiles = profiles;
        _directory = directory;
        _policies = policies;
        _shifts = shifts;
    }

    public TabularExportResult BuildTemplate(TabularFormat format) =>
        From(EmployeeImportSheet.BuildTemplate(), format, "employees-import-template");

    // Every column, filled from the live record, in the import's order — so an
    // admin edits what is there instead of retyping it. Date of Birth IS
    // exported: with "erase blank cells" chosen on the way back in, a column
    // left blank here would wipe every birthday on re-import.
    public async Task<TabularExportResult> ExportAsync(TabularFormat format)
    {
        var sheet = new TabularSheet(
            EmployeeImportSheet.SheetName,
            [.. EmployeeImportSheet.Columns.Select(c => c.Label)]);

        var policies = (await _policies.GetAllAsync())
            .ToDictionary(p => p.Id, p => p.Name, StringComparer.Ordinal);
        var shifts = (await _shifts.GetAllAsync())
            .ToDictionary(s => s.Id, s => s.Name, StringComparer.Ordinal);
        var profiles = (await _directory.GetProfilesForCurrentOrgAsync())
            .ToDictionary(p => p.UserId, StringComparer.Ordinal);

        foreach (var employee in (await _employees.GetAllAsync())
                     .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            var p = profiles.GetValueOrDefault(employee.Id) ?? new EmployeeProfile();
            var values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [EmployeeImportSheet.EmailKey] = employee.Email,
                [EmployeeImportSheet.NameKey] = employee.Name,
                [EmployeeImportSheet.RoleKey] = employee.Role,
                [EmployeeImportSheet.EmployeeNumberKey] = employee.EmployeeNumber,
                [EmployeeImportSheet.JobTitleKey] = employee.JobTitle,
                [EmployeeImportSheet.JoinDateKey] = TabularSheet.Date(employee.JoinDate ?? p.JoinDate),
                [EmployeeImportSheet.DateOfBirthKey] = TabularSheet.Date(p.DateOfBirth),
                // Names, not ids, because that is what the import reads back.
                [EmployeeImportSheet.PolicyKey] = employee.PolicyId is null ? null : policies.GetValueOrDefault(employee.PolicyId),
                [EmployeeImportSheet.ShiftKey] = employee.ShiftId is null ? null : shifts.GetValueOrDefault(employee.ShiftId),

                ["idNumber"] = p.IdNumber,
                ["idType"] = p.IdType?.ToString(),
                ["nationality"] = p.Nationality,
                ["gender"] = p.Gender?.ToString(),
                ["race"] = p.Race,
                ["maritalStatus"] = p.MaritalStatus?.ToString(),
                ["hasPr"] = TabularSheet.Bool(p.HasPr),
                ["isResident"] = TabularSheet.Bool(p.IsResident),
                ["isOku"] = TabularSheet.Bool(p.IsOku),
                ["phone"] = p.Phone,
                ["alternateEmail"] = p.AlternateEmail,
                ["addressLine1"] = p.AddressLine1,
                ["addressLine2"] = p.AddressLine2,
                ["city"] = p.City,
                ["postcode"] = p.Postcode,
                ["state"] = p.State,
                ["emergencyContactName"] = p.EmergencyContactName,
                ["emergencyContactPhone"] = p.EmergencyContactPhone,
                ["emergencyContactRelation"] = p.EmergencyContactRelation,
                ["leaveDate"] = TabularSheet.Date(p.LeaveDate),
                ["department"] = p.Department,
                ["location"] = p.Location,
                ["workSchedule"] = p.WorkSchedule,
                ["spouseWorking"] = p.SpouseWorking is { } sw ? TabularSheet.Bool(sw) : null,
                ["spouseDisabled"] = p.SpouseDisabled is { } sd ? TabularSheet.Bool(sd) : null,
                ["spouseIdNumber"] = p.SpouseIdNumber,
                ["spousePcbNumber"] = p.SpousePcbNumber,
                ["salaryType"] = p.SalaryType.ToString(),
                ["monthlySalary"] = p.MonthlySalary is { } ms ? TabularSheet.Money(ms) : null,
                ["hourlyRate"] = p.HourlyRate is { } hr ? TabularSheet.Money(hr) : null,
                ["epfNumber"] = p.EpfNumber,
                ["epfEmployeeRate"] = Percent(p.EpfEmployeeRate),
                ["contributeToEpf"] = TabularSheet.Bool(p.ContributeToEpf),
                ["epfEmployeeVoluntary"] = Percent(p.EpfEmployeeVoluntary),
                ["epfEmployerVoluntary"] = Percent(p.EpfEmployerVoluntary),
                ["epfMemberBefore1998"] = TabularSheet.Bool(p.EpfMemberBefore1998),
                ["socsoNumber"] = p.SocsoNumber,
                ["socsoScheme"] = p.SocsoScheme?.ToString(),
                ["contributeToEis"] = TabularSheet.Bool(p.ContributeToEis),
                ["contributeToSkbbk"] = TabularSheet.Bool(p.ContributeToSkbbk),
                ["incomeTaxNumber"] = p.IncomeTaxNumber,
                ["pcbBorneByEmployer"] = TabularSheet.Bool(p.PcbBorneByEmployer),
                ["paymentMethod"] = p.PaymentMethod.ToString(),
                ["bankName"] = p.BankName,
                ["bankAccountNumber"] = p.BankAccountNumber,
                ["bankAccountHolderName"] = p.BankAccountHolderName,
            };

            // Keyed, then laid out by the column list, so the export cannot
            // drift out of step with the order the import reads.
            sheet.AddRow(EmployeeImportSheet.Columns.Select(c => values.GetValueOrDefault(c.Key)));
        }

        return From(sheet, format, "employees");
    }

    public async Task<EmployeeImportResult> ImportAsync(
        byte[] content, TabularFormat format, EmployeeImportBlankCells blanks = EmployeeImportBlankCells.Keep)
    {
        IReadOnlyList<TabularSheetContent> sheets;
        try
        {
            sheets = TabularReader.ReadAllSheets(content, format);
        }
        catch (InvalidDataException ex)
        {
            return EmployeeImportResult.FileError(ex.Message);
        }

        // The template leads with READ ME, so the data is found by name — or,
        // for a renamed sheet or a CSV, the first sheet that has an Email header.
        var rows = PickDataSheet(sheets);
        if (rows is null || rows.Count == 0) return EmployeeImportResult.FileError("The file is empty.");

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
        // Accounts that exist in ANOTHER company. Adding one here links that
        // identity, which keeps its own password — so none is handed out.
        var knownEmails = (await _directory.GetUsersAsync())
            .Select(u => u.Email.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var erase = blanks == EmployeeImportBlankCells.Erase;
        var errors = new List<TabularImportError>();
        var createdAccounts = new List<CreatedAccount>();
        var created = 0;
        var updated = 0;

        for (var i = 1; i < rows.Count; i++)
        {
            var rowNumber = i + 1;   // header is row 1, as the admin sees it
            var row = rows[i];

            if (TabularTemplate.IsExampleRow(map, row, EmployeeImportSheet.Columns)) continue;
            if (row.All(TabularCell.IsBlank)) continue;   // a spacer line, not an error

            var email = TabularCell.Text(map.Cell(row, EmployeeImportSheet.EmailKey));
            if (email is null)
            {
                errors.Add(new TabularImportError(rowNumber, "Email is blank — every row needs one."));
                continue;
            }

            var cells = new RowReader(map, row, erase);

            if (existing.TryGetValue(email, out var member))
            {
                var profile = await _profiles.GetAsync(member.Id);
                if (profile is null)
                {
                    errors.Add(new TabularImportError(rowNumber, $"\"{email}\" is no longer in this company."));
                    continue;
                }

                var save = MembershipUpdate(member, cells, policies, shifts);
                ApplyProfile(cells, profile);
                if (cells.Problems.Count > 0)
                {
                    errors.Add(new TabularImportError(rowNumber, string.Join(" ", cells.Problems.Distinct())));
                    continue;
                }

                var result = await _employees.UpdateAsync(member.Id, save);
                if (!result.Ok)
                {
                    errors.Add(new TabularImportError(rowNumber, result.Error ?? "Could not update."));
                    continue;
                }

                if (cells.TouchesProfile) await _profiles.SaveAsync(member.Id, profile);
                updated++;
                continue;
            }

            // ---- A new person ----
            // Blank means "not set" here whichever mode was chosen: there is
            // nothing existing to keep or erase.
            var adding = new RowReader(map, row, erase: false);
            var name = adding.Text(EmployeeImportSheet.NameKey, 160);
            var number = adding.Text(EmployeeImportSheet.EmployeeNumberKey, 40);
            var dateOfBirth = adding.Date(EmployeeImportSheet.DateOfBirthKey);
            var joinDate = adding.Date(EmployeeImportSheet.JoinDateKey);
            var policyId = adding.Lookup(EmployeeImportSheet.PolicyKey, policies, "policy");
            var shiftId = adding.Lookup(EmployeeImportSheet.ShiftKey, shifts, "shift");

            if (name is null) adding.Problems.Add($"\"{email}\" is new, so Name is required.");
            if (number is null) adding.Problems.Add($"\"{email}\" is new, so Employee No is required.");
            var password = DefaultPassword.For(email, dateOfBirth);
            if (password is null && !adding.HasProblemWith(EmployeeImportSheet.DateOfBirthKey))
                adding.Problems.Add(
                    $"\"{email}\" is new, so Date of Birth is required — the first password is the "
                    + "email followed by the birthday as MMDD.");

            // Checked now, against a blank profile, so a bad salary fails the
            // row BEFORE an account exists rather than after.
            var draft = new EmployeeProfileDto();
            ApplyProfile(adding, draft);
            if (adding.Problems.Count > 0)
            {
                errors.Add(new TabularImportError(rowNumber, string.Join(" ", adding.Problems.Distinct())));
                continue;
            }

            var create = await _employees.CreateAsync(new CreateEmployeeDto
            {
                Email = email,
                Name = name,
                Password = password,
                Role = adding.Text(EmployeeImportSheet.RoleKey, 20) ?? "Employee",
                EmployeeNumber = number,
                JobTitle = adding.Text(EmployeeImportSheet.JobTitleKey, 120),
                JoinDate = joinDate,
                DateOfBirth = dateOfBirth,
                PolicyId = policyId,
                ShiftId = shiftId,
            });
            if (!create.Ok || create.Employee is null)
            {
                errors.Add(new TabularImportError(rowNumber, create.Error ?? "Could not create."));
                continue;
            }

            if (adding.TouchesProfile && await _profiles.GetAsync(create.Employee.Id) is { } fresh)
            {
                ApplyProfile(new RowReader(map, row, erase: false), fresh);
                await _profiles.SaveAsync(create.Employee.Id, fresh);
            }

            created++;
            existing[email] = create.Employee;   // a repeated email further down updates, not re-adds
            if (!knownEmails.Contains(email))
                createdAccounts.Add(new CreatedAccount(email, name!, password!));
        }

        // Rows are applied as they are read rather than held back until the
        // end: each is an independent person, and failing all twenty because
        // one has a typo would be worse than saving nineteen and naming the one.
        return new EmployeeImportResult
        {
            Ok = errors.Count == 0,
            Created = created,
            Updated = updated,
            Errors = errors,
            CreatedAccounts = createdAccounts,
        };
    }

    // The membership half of an existing person's row. Starts from what they
    // have — UpdateEmployeeDto overwrites policy, shift, role and modules
    // unconditionally — and changes only the columns in the file.
    private static UpdateEmployeeDto MembershipUpdate(
        EmployeeDto member,
        RowReader cells,
        IReadOnlyDictionary<string, string> policies,
        IReadOnlyDictionary<string, string> shifts)
    {
        var save = new UpdateEmployeeDto
        {
            Name = null,          // null → unchanged
            Email = null,         // matched on it; never rewritten
            EmployeeNumber = null,
            JobTitle = null,
            JoinDate = null,
            Role = member.Role,
            PolicyId = member.PolicyId,
            ShiftId = member.ShiftId,
            Modules = member.Modules?.ToList(),
        };

        cells.Required(EmployeeImportSheet.NameKey, "Name", 160, v => save.Name = v);
        cells.Required(EmployeeImportSheet.RoleKey, "Role", 20, v => save.Role = v);
        cells.Required(EmployeeImportSheet.EmployeeNumberKey, "Employee No", 40, v => save.EmployeeNumber = v);
        // The service reads "" as "clear" for these two, null as "unchanged".
        cells.Text(EmployeeImportSheet.JobTitleKey, 120, v => save.JobTitle = v ?? string.Empty);
        cells.Date(EmployeeImportSheet.JoinDateKey, v =>
        {
            if (v is null) save.ClearJoinDate = true;
            else save.JoinDate = v;
        });
        cells.Lookup(EmployeeImportSheet.PolicyKey, policies, "policy", v => save.PolicyId = v);
        cells.Lookup(EmployeeImportSheet.ShiftKey, shifts, "shift", v => save.ShiftId = v);
        return save;
    }

    // The profile half, onto the full profile as it stands (or a blank one for
    // a new person). Only columns present in the file are touched.
    private static void ApplyProfile(RowReader c, EmployeeProfileDto p)
    {
        c.Date(EmployeeImportSheet.JoinDateKey, v => p.JoinDate = v);
        c.Date(EmployeeImportSheet.DateOfBirthKey, v => p.DateOfBirth = v);

        c.Text("idNumber", 40, v => p.IdNumber = v);
        c.Enum<IdType>("idType", "ID Type", v => p.IdType = v);
        c.Text("nationality", 60, v => p.Nationality = v);
        c.Enum<Gender>("gender", "Gender", v => p.Gender = v);
        c.Text("race", 60, v => p.Race = v);
        c.Enum<MaritalStatus>("maritalStatus", "Marital Status", v => p.MaritalStatus = v);
        c.Flag("hasPr", "Malaysian PR", false, v => p.HasPr = v);
        c.Flag("isResident", "Tax Resident", true, v => p.IsResident = v);
        c.Flag("isOku", "OKU", false, v => p.IsOku = v);

        c.Text("phone", 40, v => p.Phone = v);
        c.Text("alternateEmail", 120, v => p.AlternateEmail = v);
        c.Text("addressLine1", 160, v => p.AddressLine1 = v);
        c.Text("addressLine2", 160, v => p.AddressLine2 = v);
        c.Text("city", 120, v => p.City = v);
        c.Text("postcode", 20, v => p.Postcode = v);
        c.Text("state", 60, v => p.State = v);
        c.Text("emergencyContactName", 120, v => p.EmergencyContactName = v);
        c.Text("emergencyContactPhone", 40, v => p.EmergencyContactPhone = v);
        c.Text("emergencyContactRelation", 60, v => p.EmergencyContactRelation = v);

        c.Date("leaveDate", v => p.LeaveDate = v);
        c.Text("department", 120, v => p.Department = v);
        c.Text("location", 120, v => p.Location = v);
        c.Text("workSchedule", 120, v => p.WorkSchedule = v);

        // Tri-state: blank must stay null, not No — payroll readiness tests
        // SpouseWorking for null on a married employee.
        c.OptionalFlag("spouseWorking", "Spouse Working", v => p.SpouseWorking = v);
        c.OptionalFlag("spouseDisabled", "Spouse Disabled", v => p.SpouseDisabled = v);
        c.Text("spouseIdNumber", 40, v => p.SpouseIdNumber = v);
        c.Text("spousePcbNumber", 40, v => p.SpousePcbNumber = v);

        c.RequiredEnum("salaryType", "Salary Type", SalaryType.MONTHLY, v => p.SalaryType = v);
        c.Amount("monthlySalary", "Monthly Salary", v => p.MonthlySalary = v);
        c.Amount("hourlyRate", "Hourly Rate", v => p.HourlyRate = v);

        c.Text("epfNumber", 40, v => p.EpfNumber = v);
        // 0 is how "use the statutory rate" is stored, so erasing means that.
        c.Percent("epfEmployeeRate", "EPF Employee Rate %", v => p.EpfEmployeeRate = v);
        c.Flag("contributeToEpf", "Contribute to EPF", true, v => p.ContributeToEpf = v);
        c.Percent("epfEmployeeVoluntary", "EPF Employee Voluntary %", v => p.EpfEmployeeVoluntary = v);
        c.Percent("epfEmployerVoluntary", "EPF Employer Voluntary %", v => p.EpfEmployerVoluntary = v);
        c.Flag("epfMemberBefore1998", "EPF Member Before 1998", false, v => p.EpfMemberBefore1998 = v);
        c.Text("socsoNumber", 40, v => p.SocsoNumber = v);
        c.Enum<SocsoScheme>("socsoScheme", "SOCSO Scheme", v => p.SocsoScheme = v);
        c.Flag("contributeToEis", "Contribute to EIS", true, v => p.ContributeToEis = v);
        c.Flag("contributeToSkbbk", "Contribute to SKBBK", false, v => p.ContributeToSkbbk = v);
        c.Text("incomeTaxNumber", 40, v => p.IncomeTaxNumber = v);
        c.Flag("pcbBorneByEmployer", "PCB Borne by Employer", false, v => p.PcbBorneByEmployer = v);

        c.RequiredEnum("paymentMethod", "Payment Method", PaymentMethod.BANK_TRANSFER, v => p.PaymentMethod = v);
        c.Text("bankName", 120, v => p.BankName = v);
        c.Text("bankAccountNumber", 60, v => p.BankAccountNumber = v);
        c.Text("bankAccountHolderName", 120, v => p.BankAccountHolderName = v);

        // Recorded on the salary history the profile service writes when pay moves.
        p.SalaryChangeReason = Payroll.Entities.SalaryChangeReason.OTHER;
        p.SalaryChangeNotes = "Updated by spreadsheet import";
    }

    private static IReadOnlyList<IReadOnlyList<string>>? PickDataSheet(IReadOnlyList<TabularSheetContent> sheets)
    {
        var named = sheets.FirstOrDefault(s =>
            string.Equals(s.Name.Trim(), EmployeeImportSheet.SheetName, StringComparison.OrdinalIgnoreCase));
        if (named is not null) return named.Rows;

        return sheets
            .Where(s => s.Rows.Count > 0)
            .FirstOrDefault(s => TabularHeaderMap.Build(s.Rows[0], EmployeeImportSheet.Columns).Map is not null)
            ?.Rows
            ?? sheets.FirstOrDefault()?.Rows;
    }

    // XLSX carries the READ ME and Columns guides around the data. A CSV can
    // hold one sheet only, and a labelled multi-sheet CSV would not re-import.
    private static TabularExportResult From(TabularSheet data, TabularFormat format, string fileName) =>
        format == TabularFormat.Xlsx
            ? TabularExportResult.From(EmployeeImportSheet.Workbook(data), format, fileName)
            : TabularExportResult.From(data, format, fileName);

    private static string Percent(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // Reads one row's cells under the chosen blank-cell rule, collecting every
    // problem on the row rather than stopping at the first — one pass, one
    // complete fix-list.
    //
    //   column absent from the file  → never called back: that field is untouched
    //   cell filled                  → parsed; a value that won't parse is a problem
    //   cell blank, "keep" mode      → untouched
    //   cell blank, "erase" mode     → cleared (or reset to the field's default),
    //                                  except the CannotBeBlank fields, which fail
    private sealed class RowReader(TabularHeaderMap map, IReadOnlyList<string> row, bool erase)
    {
        public List<string> Problems { get; } = [];
        private readonly HashSet<string> _problemKeys = new(StringComparer.Ordinal);

        // Whether this row carries any profile column at all — if not, the
        // profile save (and its payroll re-run flag) is skipped.
        public bool TouchesProfile => ProfileKeys.Any(map.Has);

        public bool HasProblemWith(string key) => _problemKeys.Contains(key);

        private static readonly string[] MembershipOnly =
        [
            EmployeeImportSheet.EmailKey, EmployeeImportSheet.NameKey, EmployeeImportSheet.RoleKey,
            EmployeeImportSheet.EmployeeNumberKey, EmployeeImportSheet.JobTitleKey,
            EmployeeImportSheet.PolicyKey, EmployeeImportSheet.ShiftKey,
        ];

        private static readonly string[] ProfileKeys =
            [.. EmployeeImportSheet.Columns.Select(c => c.Key).Except(MembershipOnly)];

        private static string Label(string key) =>
            EmployeeImportSheet.Columns.First(c => c.Key == key).Label;

        private void Problem(string key, string message)
        {
            _problemKeys.Add(key);
            Problems.Add(message);
        }

        // (present, raw). Absent → false: the caller does nothing.
        private bool Read(string key, out string raw)
        {
            raw = map.Has(key) ? map.Cell(row, key).Trim() : string.Empty;
            return map.Has(key);
        }

        // For the new-person path, where blank is simply "not given".
        public string? Text(string key, int max) => TabularCell.Text(map.Cell(row, key), max);

        public DateTime? Date(string key)
        {
            if (!Read(key, out var raw) || raw.Length == 0) return null;
            var parsed = TabularCell.Date(raw);
            if (parsed is null) Problem(key, $"\"{raw}\" in {Label(key)} isn't a date — use YYYY-MM-DD.");
            return parsed;
        }

        public string? Lookup(string key, IReadOnlyDictionary<string, string> byName, string what)
        {
            if (!Read(key, out var raw) || raw.Length == 0) return null;
            if (byName.TryGetValue(raw, out var id)) return id;
            Problem(key, $"No {what} named \"{raw}\". Use one of: {string.Join(", ", byName.Keys)}.");
            return null;
        }

        // ---- The keep/erase-aware setters ----

        public void Text(string key, int max, Action<string?> set)
        {
            if (!Read(key, out var raw)) return;
            if (raw.Length == 0) { if (erase) set(null); return; }
            set(raw.Length > max ? raw[..max] : raw);
        }

        public void Required(string key, string label, int max, Action<string> set)
        {
            if (!Read(key, out var raw)) return;
            if (raw.Length == 0)
            {
                if (erase) Problem(key, $"{label} can't be blank.");
                return;
            }
            set(raw.Length > max ? raw[..max] : raw);
        }

        public void Date(string key, Action<DateTime?> set)
        {
            if (!Read(key, out var raw)) return;
            if (raw.Length == 0) { if (erase) set(null); return; }
            var parsed = TabularCell.Date(raw);
            if (parsed is null) Problem(key, $"\"{raw}\" in {Label(key)} isn't a date — use YYYY-MM-DD.");
            else set(parsed.Value.Date);
        }

        public void Lookup(string key, IReadOnlyDictionary<string, string> byName, string what, Action<string?> set)
        {
            if (!Read(key, out var raw)) return;
            if (raw.Length == 0) { if (erase) set(null); return; }
            if (byName.TryGetValue(raw, out var id)) set(id);
            else Problem(key, $"No {what} named \"{raw}\". Use one of: {string.Join(", ", byName.Keys)}.");
        }

        public void Enum<T>(string key, string label, Action<T?> set) where T : struct, System.Enum
        {
            if (!Read(key, out var raw)) return;
            if (raw.Length == 0) { if (erase) set(null); return; }
            if (TryEnum<T>(raw, out var value)) set(value);
            else Problem(key, $"\"{raw}\" isn't a valid {label}. Use {string.Join(", ", System.Enum.GetNames<T>())}.");
        }

        public void RequiredEnum<T>(string key, string label, T fallback, Action<T> set) where T : struct, System.Enum
        {
            if (!Read(key, out var raw)) return;
            if (raw.Length == 0)
            {
                if (!erase) return;
                if (EmployeeImportSheet.CannotBeBlank.Contains(key)) Problem(key, $"{label} can't be blank.");
                else set(fallback);
                return;
            }
            if (TryEnum<T>(raw, out var value)) set(value);
            else Problem(key, $"\"{raw}\" isn't a valid {label}. Use {string.Join(", ", System.Enum.GetNames<T>())}.");
        }

        public void Flag(string key, string label, bool fallback, Action<bool> set)
        {
            if (!Read(key, out var raw)) return;
            if (raw.Length == 0)
            {
                if (!erase) return;
                if (EmployeeImportSheet.CannotBeBlank.Contains(key)) Problem(key, $"{label} can't be blank.");
                else set(fallback);
                return;
            }
            if (TryFlag(raw) is { } value) set(value);
            else Problem(key, $"\"{raw}\" in {label} isn't Yes or No.");
        }

        public void OptionalFlag(string key, string label, Action<bool?> set)
        {
            if (!Read(key, out var raw)) return;
            if (raw.Length == 0) { if (erase) set(null); return; }
            if (TryFlag(raw) is { } value) set(value);
            else Problem(key, $"\"{raw}\" in {label} isn't Yes or No.");
        }

        public void Amount(string key, string label, Action<decimal?> set)
        {
            if (!Read(key, out var raw)) return;
            if (raw.Length == 0) { if (erase) set(null); return; }
            if (TryNumber(raw, key, label) is { } value) set(value);
        }

        public void Percent(string key, string label, Action<decimal> set)
        {
            if (!Read(key, out var raw)) return;
            if (raw.Length == 0) { if (erase) set(0m); return; }
            if (TryNumber(raw.TrimEnd('%'), key, label) is { } value) set(value);
        }

        // Tolerant of what a spreadsheet produces — "RM 5,000.00" is a number
        // an admin typed in good faith. Money is reported, never shrugged off:
        // a mistyped salary silently ignored is someone paid the wrong amount.
        private decimal? TryNumber(string raw, string key, string label)
        {
            var cleaned = new string([.. raw.Where(ch => char.IsDigit(ch) || ch is '.' or '-')]);
            if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                Problem(key, $"\"{raw}\" in {label} isn't a number.");
                return null;
            }
            if (parsed < 0m)
            {
                Problem(key, $"{label} can't be negative.");
                return null;
            }
            return parsed;
        }

        private static bool? TryFlag(string raw) => raw.ToLowerInvariant() switch
        {
            "yes" or "y" or "true" or "1" => true,
            "no" or "n" or "false" or "0" => false,
            _ => null,
        };

        // Accepts "Employment injury only" as well as EMPLOYMENT_INJURY_ONLY.
        private static bool TryEnum<T>(string raw, out T value) where T : struct, System.Enum =>
            System.Enum.TryParse(raw.Replace(' ', '_').Replace('-', '_'), ignoreCase: true, out value)
            && System.Enum.IsDefined(value);
    }
}
