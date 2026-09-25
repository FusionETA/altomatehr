using System.Text;
using System.Text.Json;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Dtos;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Modules.Shifts;
using AltomateHR.Api.Modules.Shifts.Dtos;
using AltomateHR.Api.Modules.Shifts.Entities;

namespace AltomateHR.Api.Tests.Employees;

// The one employee spreadsheet: adds people, and bulk-updates their record —
// membership, profile, statutory and salary details.
//
// The things worth a test each because getting them wrong is silent:
//   - what a BLANK cell does, in each of the two modes the admin picks from;
//   - that a column MISSING from the file is left alone in both;
//   - that a row with any bad value saves nothing at all;
//   - that UpdateEmployeeDto's unconditional fields (policy, shift, role,
//     modules) are carried over rather than reset;
//   - and that an export imports back without moving anything.
public class EmployeeImportTests
{
    // ─── Fixtures ───────────────────────────────────────────────────────

    private static readonly string Header = string.Join(",", EmployeeImportSheet.Columns.Select(
        c => c.Required ? $"*{c.Label}" : c.Label));

    private static byte[] Csv(params string[] dataRows) =>
        Encoding.UTF8.GetBytes(Header + "\r\n" + string.Join("\r\n", dataRows) + "\r\n");

    // A file with only the named columns — for "a column that isn't there".
    private static byte[] CsvWith(string[] labels, params string[] dataRows) =>
        Encoding.UTF8.GetBytes(string.Join(",", labels) + "\r\n" + string.Join("\r\n", dataRows) + "\r\n");

    // The membership columns lead the sheet in this order; anything else is
    // set by key through `extra`, and every other column is blank.
    private static string Row(
        string email, string name = "", string role = "", string number = "",
        string jobTitle = "", string joinDate = "", string dateOfBirth = "1990-11-23",
        string policy = "", string shift = "", params (string Key, string Value)[] extra)
    {
        var values = new Dictionary<string, string>
        {
            [EmployeeImportSheet.EmailKey] = email,
            [EmployeeImportSheet.NameKey] = name,
            [EmployeeImportSheet.RoleKey] = role,
            [EmployeeImportSheet.EmployeeNumberKey] = number,
            [EmployeeImportSheet.JobTitleKey] = jobTitle,
            [EmployeeImportSheet.JoinDateKey] = joinDate,
            [EmployeeImportSheet.DateOfBirthKey] = dateOfBirth,
            [EmployeeImportSheet.PolicyKey] = policy,
            [EmployeeImportSheet.ShiftKey] = shift,
        };
        foreach (var (key, value) in extra) values[key] = value;
        return string.Join(",", EmployeeImportSheet.Columns.Select(c => values.GetValueOrDefault(c.Key, "")));
    }

    // A new person's row: the three things a new person needs, filled.
    private static string NewRow(string email, string name = "New Person", string number = "EMP-100",
        params (string Key, string Value)[] extra) =>
        Row(email, name, number: number, extra: extra);

    private sealed record Harness(
        EmployeeImportService Service, FakeEmployeeService Employees, FakeProfileService Profiles);

    private static Harness Make(
        IEnumerable<EmployeeDto>? existing = null,
        IEnumerable<EmployeeProfile>? profiles = null,
        IEnumerable<string>? accountsInOtherCompanies = null)
    {
        var members = existing?.ToList() ?? [];
        var employees = new FakeEmployeeService(members);
        var store = new FakeProfileService(profiles ?? []);
        var directory = new FakeDirectory(
            store,
            [.. members.Select(m => new User { Id = m.Id, Email = m.Email }),
             .. (accountsInOtherCompanies ?? []).Select(e => new User { Id = $"other-{e}", Email = e })]);
        return new Harness(
            new EmployeeImportService(employees, store, directory, new FakePolicyService(), new FakeShiftService()),
            employees, store);
    }

    private static Harness Make(params EmployeeDto[] existing) => Make(existing, null, null);

    private static EmployeeDto Member(
        string id, string email, string role = "Employee", string? policyId = "pol-full",
        string? shiftId = "shift-office", List<string>? modules = null) => new()
        {
            Id = id,
            Email = email,
            Name = "Existing Person",
            Role = role,
            EmployeeNumber = "E-001",
            JobTitle = "Site Engineer",
            PolicyId = policyId,
            ShiftId = shiftId,
            Modules = modules,
        };

    private static EmployeeProfile Profile(string userId) => new()
    {
        UserId = userId,
        IdNumber = "900101-14-5567",
        BankName = "Maybank",
        BankAccountNumber = "112233445566",
        MonthlySalary = 5000m,
        HasPr = true,
        IsResident = false,
        DateOfBirth = new DateTime(1990, 1, 1),
    };

    private static Task<EmployeeImportResult> Erase(EmployeeImportService service, byte[] file) =>
        service.ImportAsync(file, TabularFormat.Csv, EmployeeImportBlankCells.Erase);

    // ─── Creating ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreatesAPersonWhoIsNotInTheOrgYet()
    {
        var h = Make();

        // Deliberately NOT the template's example values — those are skipped
        // by IsExampleRow, which is its own test below.
        var result = await h.Service.ImportAsync(
            Csv(Row("chan@example.com", "Chan Mei Ling", "Employee", "EMP-042",
                "QS Executive", "2026-01-15", policy: "Full-time", shift: "Office Hours")),
            TabularFormat.Csv);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Updated);
        var created = Assert.Single(h.Employees.Created);
        Assert.Equal("chan@example.com", created.Email);
        Assert.Equal("Chan Mei Ling", created.Name);
        Assert.Equal("EMP-042", created.EmployeeNumber);
        Assert.Equal(new DateTime(2026, 1, 15), created.JoinDate);
        // Names in the sheet, ids in the call.
        Assert.Equal("pol-full", created.PolicyId);
        Assert.Equal("shift-office", created.ShiftId);
    }

    [Fact]
    public async Task ANewPersonsPayrollDetailsAreSavedToTheirProfile()
    {
        var h = Make();

        await h.Service.ImportAsync(
            Csv(NewRow("chan@example.com", extra: [("monthlySalary", "\"RM 4,200.00\""), ("bankName", "CIMB")])),
            TabularFormat.Csv);

        var saved = Assert.Single(h.Profiles.Saves);
        Assert.Equal(4200m, saved.Dto.MonthlySalary);
        Assert.Equal("CIMB", saved.Dto.BankName);
    }

    // The whole point of returning them: they are shown once and stored nowhere.
    [Fact]
    public async Task ReturnsThePasswordForEachAccountItCreated()
    {
        var h = Make();

        var result = await h.Service.ImportAsync(
            Csv(NewRow("a@example.com", number: "A-1"), NewRow("b@example.com", number: "B-1")),
            TabularFormat.Csv);

        Assert.Equal(2, result.CreatedAccounts.Count);
        Assert.Equal(
            result.CreatedAccounts.Select(a => a.Password),
            h.Employees.Created.Select(c => c.Password));
        Assert.All(result.CreatedAccounts, a => Assert.False(string.IsNullOrWhiteSpace(a.Password)));
    }

    // The house convention: email + birthday as MMDD — derived so the welcome
    // email can state the RULE and never the credential itself.
    [Fact]
    public async Task PasswordsFollowTheEmailPlusBirthdayConvention()
    {
        var h = Make();

        var result = await h.Service.ImportAsync(
            Csv(Row("a@example.com", "Person A", number: "A-1", dateOfBirth: "1990-11-23"),
                Row("b@example.com", "Person B", number: "B-1", dateOfBirth: "1985-01-05")),
            TabularFormat.Csv);

        var passwords = result.CreatedAccounts.Select(a => a.Password).ToList();
        Assert.Equal(["a@example.com1123", "b@example.com0105"], passwords);
    }

    // An account that already exists in another company is LINKED, and keeps
    // its own password — handing out the derived one would give the admin a
    // password that does not work.
    [Fact]
    public async Task APersonFromAnotherCompanyIsAddedWithoutAPasswordToHandOut()
    {
        var h = Make(existing: null, profiles: null, accountsInOtherCompanies: ["shared@example.com"]);

        var result = await h.Service.ImportAsync(Csv(NewRow("shared@example.com")), TabularFormat.Csv);

        Assert.Equal(1, result.Created);
        Assert.Empty(result.CreatedAccounts);
    }

    [Theory]
    [InlineData("name", "Name is required")]
    [InlineData("number", "Employee No is required")]
    [InlineData("dob", "Date of Birth is required")]
    public async Task ANewPersonMissingWhatTheyNeedFailsTheirRowOnly(string missing, string expected)
    {
        var h = Make();
        var bad = Row("bad@example.com",
            name: missing == "name" ? "" : "Person",
            number: missing == "number" ? "" : "X-1",
            dateOfBirth: missing == "dob" ? "" : "1990-11-23");

        var result = await h.Service.ImportAsync(Csv(bad, NewRow("fine@example.com")), TabularFormat.Csv);

        Assert.Contains(expected, Assert.Single(result.Errors).Message);
        // The good row still landed — each row is an independent person.
        Assert.Equal("fine@example.com", Assert.Single(h.Employees.Created).Email);
    }

    // ─── Updating: filled cells, and what must survive ──────────────────

    [Fact]
    public async Task AnExistingEmailIsUpdatedRatherThanDuplicated()
    {
        var h = Make(Member("usr-1", "aisyah@example.com"));

        var result = await h.Service.ImportAsync(
            Csv(Row("aisyah@example.com", jobTitle: "Safety Officer")), TabularFormat.Csv);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);
        Assert.Empty(h.Employees.Created);
        Assert.Equal("Safety Officer", Assert.Single(h.Employees.Updated).Dto.JobTitle);
    }

    // UpdateEmployeeDto writes policy, shift, modules and role unconditionally,
    // so they must be carried over, not left at the DTO's defaults.
    [Fact]
    public async Task KeepMode_BlankCellsDoNotClearPolicyShiftRoleOrModules()
    {
        var h = Make(Member(
            "usr-1", "aisyah@example.com",
            role: "Supervisor", policyId: "pol-full", shiftId: "shift-office",
            modules: ["claims", "leave"]));

        var result = await h.Service.ImportAsync(
            Csv(Row("aisyah@example.com", jobTitle: "Safety Officer")), TabularFormat.Csv);

        Assert.True(result.Ok, result.Message);
        var sent = Assert.Single(h.Employees.Updated).Dto;
        Assert.Equal("Supervisor", sent.Role);
        Assert.Equal("pol-full", sent.PolicyId);
        Assert.Equal("shift-office", sent.ShiftId);
        Assert.Equal(["claims", "leave"], sent.Modules);
        Assert.Null(sent.Name);             // null → unchanged
        Assert.Null(sent.EmployeeNumber);
        Assert.False(sent.ClearJoinDate);
    }

    [Fact]
    public async Task KeepMode_BlankProfileCellsLeaveTheExistingValue()
    {
        var h = Make([Member("usr-1", "aisyah@example.com")], [Profile("usr-1")]);

        var result = await h.Service.ImportAsync(
            // Not the template's example department — a row of nothing but
            // example values is skipped as the sample line.
            Csv(Row("aisyah@example.com", dateOfBirth: "", extra: [("department", "Finance")])), TabularFormat.Csv);

        Assert.True(result.Ok, string.Join(" | ", result.Errors.Select(e => e.Message)));
        var saved = Assert.Single(h.Profiles.Saves).Dto;
        Assert.Equal("Finance", saved.Department);
        Assert.Equal("Maybank", saved.BankName);
        Assert.Equal(5000m, saved.MonthlySalary);
        Assert.True(saved.HasPr);
        Assert.Equal(new DateTime(1990, 1, 1), saved.DateOfBirth);
    }

    [Fact]
    public async Task AFilledColumnStillOverridesTheExistingValue()
    {
        var h = Make(Member("usr-1", "aisyah@example.com", policyId: "pol-full"));

        await h.Service.ImportAsync(
            Csv(Row("aisyah@example.com", policy: "Probation")), TabularFormat.Csv);

        Assert.Equal("pol-probation", Assert.Single(h.Employees.Updated).Dto.PolicyId);
    }

    // Matching is on email, so the login must never be rewritten from the sheet.
    [Fact]
    public async Task TheEmailColumnIsNeverWrittenBack()
    {
        var h = Make(Member("usr-1", "aisyah@example.com"));

        await h.Service.ImportAsync(Csv(Row("aisyah@example.com", "New Name")), TabularFormat.Csv);

        var sent = Assert.Single(h.Employees.Updated).Dto;
        Assert.Null(sent.Email);
        Assert.Equal("New Name", sent.Name);
    }

    // ─── Erase mode ─────────────────────────────────────────────────────

    [Fact]
    public async Task EraseMode_BlankCellsClearTheExistingValues()
    {
        var h = Make([Member("usr-1", "aisyah@example.com")], [Profile("usr-1")]);

        var result = await Erase(h.Service, Csv(Row(
            "aisyah@example.com", "Aisyah", "Employee", "E-001", dateOfBirth: "",
            extra: [("salaryType", "MONTHLY"), ("contributeToEpf", "Yes"), ("contributeToEis", "Yes")])));

        Assert.True(result.Ok, string.Join(" | ", result.Errors.Select(e => e.Message)));
        var sent = Assert.Single(h.Employees.Updated).Dto;
        Assert.Equal(string.Empty, sent.JobTitle);   // "" is the service's "clear"
        Assert.True(sent.ClearJoinDate);
        Assert.Null(sent.PolicyId);                  // → the company default
        Assert.Null(sent.ShiftId);

        var saved = Assert.Single(h.Profiles.Saves).Dto;
        Assert.Null(saved.BankName);
        Assert.Null(saved.IdNumber);
        Assert.Null(saved.MonthlySalary);
        Assert.Null(saved.DateOfBirth);
        // Yes/No fields go back to their defaults, not to "unknown".
        Assert.False(saved.HasPr);
        Assert.True(saved.IsResident);
    }

    [Theory]
    [InlineData("name", "Name can't be blank")]
    [InlineData("role", "Role can't be blank")]
    [InlineData("number", "Employee No can't be blank")]
    [InlineData("salaryType", "Salary Type can't be blank")]
    [InlineData("contributeToEpf", "Contribute to EPF can't be blank")]
    [InlineData("contributeToEis", "Contribute to EIS can't be blank")]
    public async Task EraseMode_AFieldThatMustHaveAValueFailsTheRowInsteadOfClearing(string blank, string expected)
    {
        var h = Make([Member("usr-1", "aisyah@example.com")], [Profile("usr-1")]);
        var extra = new List<(string, string)>
        {
            ("salaryType", "MONTHLY"), ("contributeToEpf", "Yes"), ("contributeToEis", "Yes"),
        };
        extra.RemoveAll(e => e.Item1 == blank);

        var result = await Erase(h.Service, Csv(Row(
            "aisyah@example.com",
            name: blank == "name" ? "" : "Aisyah",
            role: blank == "role" ? "" : "Employee",
            number: blank == "number" ? "" : "E-001",
            extra: [.. extra])));

        Assert.Contains(expected, Assert.Single(result.Errors).Message);
        Assert.Empty(h.Employees.Updated);
        Assert.Empty(h.Profiles.Saves);
    }

    // The column-level rule: only headers IN the file are applied, so a sheet
    // trimmed to the bank columns cannot erase anything else — in either mode.
    [Fact]
    public async Task EraseMode_AColumnMissingFromTheFileIsLeftAlone()
    {
        var h = Make([Member("usr-1", "aisyah@example.com", role: "Supervisor")], [Profile("usr-1")]);

        var result = await Erase(h.Service, CsvWith(
            ["Email", "Bank Name"], "aisyah@example.com,RHB"));

        Assert.True(result.Ok, string.Join(" | ", result.Errors.Select(e => e.Message)));
        var sent = Assert.Single(h.Employees.Updated).Dto;
        Assert.Equal("Supervisor", sent.Role);
        Assert.Null(sent.JobTitle);                  // null → unchanged
        Assert.False(sent.ClearJoinDate);
        Assert.Equal("pol-full", sent.PolicyId);

        var saved = Assert.Single(h.Profiles.Saves).Dto;
        Assert.Equal("RHB", saved.BankName);
        Assert.Equal("900101-14-5567", saved.IdNumber);
        Assert.Equal(5000m, saved.MonthlySalary);
    }

    // ─── Bad values ─────────────────────────────────────────────────────

    // Every problem on the row is reported at once, and NOTHING on it is
    // saved — the replaced payroll import half-saved a row that failed.
    [Fact]
    public async Task ARowWithBadValuesSavesNothingAndNamesEveryProblem()
    {
        var h = Make([Member("usr-1", "aisyah@example.com")], [Profile("usr-1")]);

        var result = await h.Service.ImportAsync(Csv(Row(
            "aisyah@example.com", jobTitle: "Changed",
            extra: [("monthlySalary", "lots"), ("gender", "F?"), ("hasPr", "maybe"), ("leaveDate", "someday")])),
            TabularFormat.Csv);

        var message = Assert.Single(result.Errors).Message;
        Assert.Contains("Monthly Salary isn't a number", message);
        Assert.Contains("isn't a valid Gender", message);
        Assert.Contains("isn't Yes or No", message);
        Assert.Contains("Leave Date isn't a date", message);
        Assert.Empty(h.Employees.Updated);
        Assert.Empty(h.Profiles.Saves);
    }

    [Fact]
    public async Task AnUnknownPolicyNameFailsTheRowRatherThanSilentlyDefaulting()
    {
        var h = Make();

        var result = await h.Service.ImportAsync(
            Csv(Row("a@example.com", "Person A", number: "A-1", policy: "Platinum")), TabularFormat.Csv);

        Assert.False(result.Ok);
        Assert.Contains("No policy named \"Platinum\"", Assert.Single(result.Errors).Message);
        Assert.Empty(h.Employees.Created);
    }

    [Fact]
    public async Task AMissingEmailColumnRejectsTheFile()
    {
        var h = Make();
        var content = Encoding.UTF8.GetBytes("Name,Role\r\nAisyah,Employee\r\n");

        var result = await h.Service.ImportAsync(content, TabularFormat.Csv);

        Assert.False(result.Ok);
        Assert.Contains("Missing required column", result.Message);
    }

    [Fact]
    public async Task ARowWithDataButNoEmailIsReportedNotSkipped()
    {
        var h = Make();

        var result = await h.Service.ImportAsync(Csv(Row("", "Somebody", number: "S-1")), TabularFormat.Csv);

        Assert.Contains("Email is blank", Assert.Single(result.Errors).Message);
    }

    // ─── The template and the export ────────────────────────────────────

    [Fact]
    public async Task TheTemplatesExampleRowIsNotImported()
    {
        var h = Make();
        var template = h.Service.BuildTemplate(TabularFormat.Csv);

        var result = await h.Service.ImportAsync(template.Content, TabularFormat.Csv);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(0, result.Created);
        Assert.Empty(h.Employees.Created);
    }

    // The XLSX leads with the READ ME warning, so the import has to find the
    // data by sheet name rather than taking the first sheet.
    [Fact]
    public async Task TheXlsxTemplateLeadsWithTheGuide_AndStillImports()
    {
        var h = Make();
        var template = h.Service.BuildTemplate(TabularFormat.Xlsx);

        var sheets = TabularReader.ReadAllSheets(template.Content, TabularFormat.Xlsx);
        Assert.Equal(
            [EmployeeImportSheet.ReadMeSheetName, EmployeeImportSheet.SheetName, EmployeeImportSheet.ColumnsSheetName],
            sheets.Select(s => s.Name));
        Assert.Contains(sheets[0].Rows.SelectMany(r => r), c => c.Contains("ERASES"));
        Assert.Equal(EmployeeImportSheet.Columns.Count + 1, sheets[2].Rows.Count);   // header + one per column

        var result = await h.Service.ImportAsync(template.Content, TabularFormat.Xlsx);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(0, result.Created);
    }

    // Export, import back with ERASE chosen. If the export left any column
    // blank that the record actually has (Date of Birth used to be), this
    // would wipe it.
    [Fact]
    public async Task TheExportImportsBackUnchanged_EvenInEraseMode()
    {
        var h = Make(
            [Member("usr-1", "aisyah@example.com"), Member("usr-2", "chan@example.com", role: "Supervisor")],
            [Profile("usr-1"), Profile("usr-2")]);

        var export = await h.Service.ExportAsync(TabularFormat.Csv);
        var result = await Erase(h.Service, export.Content);

        Assert.True(result.Ok, string.Join(" | ", result.Errors.Select(e => e.Message)));
        Assert.Equal(0, result.Created);
        Assert.Equal(2, result.Updated);

        var back = h.Employees.Updated.Single(u => u.Id == "usr-2").Dto;
        Assert.Equal("Supervisor", back.Role);
        Assert.Equal("E-001", back.EmployeeNumber);
        Assert.Equal("Site Engineer", back.JobTitle);
        Assert.Equal("pol-full", back.PolicyId);

        var profile = h.Profiles.Saves.Single(s => s.UserId == "usr-2").Dto;
        Assert.Equal(new DateTime(1990, 1, 1), profile.DateOfBirth);
        Assert.Equal("Maybank", profile.BankName);
        Assert.Equal(5000m, profile.MonthlySalary);
        Assert.True(profile.HasPr);
        Assert.False(profile.IsResident);
    }

    [Fact]
    public async Task TheExportWritesPolicyAndShiftNamesNotIds()
    {
        var h = Make(Member("usr-1", "aisyah@example.com"));

        var text = Encoding.UTF8.GetString((await h.Service.ExportAsync(TabularFormat.Csv)).Content);

        Assert.Contains("Full-time", text);
        Assert.Contains("Office Hours", text);
        Assert.DoesNotContain("pol-full", text);
        Assert.DoesNotContain("shift-office", text);
    }

    // A password is generated per created account and returned once. An export
    // is a file that sits in Downloads; it must never carry one.
    [Fact]
    public async Task TheExportCarriesNoPasswordColumn()
    {
        var h = Make(Member("usr-1", "aisyah@example.com"));

        var export = await h.Service.ExportAsync(TabularFormat.Csv);

        Assert.DoesNotContain("assword", Encoding.UTF8.GetString(export.Content));
    }

    // ─── Doubles ────────────────────────────────────────────────────────

    private sealed class FakeEmployeeService : IEmployeeService
    {
        public Task<SetPasswordResult> SetPasswordAsync(string userId, string newPassword) =>
            Task.FromResult(new SetPasswordResult(true, null));

        private readonly List<EmployeeDto> _existing;

        public FakeEmployeeService(IEnumerable<EmployeeDto> existing) => _existing = [.. existing];

        public List<CreateEmployeeDto> Created { get; } = [];
        public List<(string Id, UpdateEmployeeDto Dto)> Updated { get; } = [];

        public Task<IEnumerable<EmployeeDto>> GetAllAsync() =>
            Task.FromResult<IEnumerable<EmployeeDto>>(_existing);

        public Task<EmployeeSaveResult> CreateAsync(CreateEmployeeDto dto)
        {
            Created.Add(dto);
            return Task.FromResult(new EmployeeSaveResult(
                true, new EmployeeDto { Id = $"new-{dto.Email}", Email = dto.Email }, null));
        }

        public Task<EmployeeSaveResult> UpdateAsync(string id, UpdateEmployeeDto dto)
        {
            Updated.Add((id, dto));
            return Task.FromResult(new EmployeeSaveResult(true, new EmployeeDto { Id = id }, null));
        }
    }

    // Profiles held as entities (what the export reads through the directory)
    // and handed out as DTOs (what the import edits) — converted through JSON,
    // since the two share their field names. GetAsync returns a COPY, so a row
    // that fails and is never saved can't leave its edits behind in the store.
    private sealed class FakeProfileService(IEnumerable<EmployeeProfile> profiles) : IEmployeeProfileService
    {
        public Dictionary<string, EmployeeProfile> Store { get; } =
            profiles.ToDictionary(p => p.UserId);

        public List<(string UserId, EmployeeProfileDto Dto)> Saves { get; } = [];

        public Task<EmployeeProfileDto?> GetAsync(string userId)
        {
            var dto = Store.TryGetValue(userId, out var entity)
                ? JsonSerializer.Deserialize<EmployeeProfileDto>(JsonSerializer.Serialize(entity))!
                : new EmployeeProfileDto();
            dto.Id = userId;
            return Task.FromResult<EmployeeProfileDto?>(dto);
        }

        public Task<EmployeeProfileDto?> SaveAsync(string userId, EmployeeProfileDto dto)
        {
            Saves.Add((userId, dto));
            var entity = JsonSerializer.Deserialize<EmployeeProfile>(JsonSerializer.Serialize(dto))!;
            entity.UserId = userId;
            Store[userId] = entity;
            return Task.FromResult<EmployeeProfileDto?>(dto);
        }
    }

    private sealed class FakeDirectory(FakeProfileService profiles, List<User> users) : IDirectoryService
    {
        public Task<List<EmployeeProfile>> GetProfilesForCurrentOrgAsync() =>
            Task.FromResult(profiles.Store.Values.ToList());

        public Task<List<User>> GetUsersAsync() => Task.FromResult(users);

        public Task<OrganizationMembership?> GetMembershipForUserAsync(string userId) => throw new NotSupportedException();
        public Task<List<OrganizationMembership>> GetMembershipsForCurrentOrgAsync() => throw new NotSupportedException();
        public Task<OrganizationMembership?> GetMembershipAsync(string organizationId, string userId) =>
            throw new NotSupportedException();
        public Task<List<OrganizationMembership>> GetMembershipsByUserAsync(string userId) =>
            throw new NotSupportedException();
        public Task<int> CountMembershipsByShiftAsync(string shiftId) => throw new NotSupportedException();
        public Task<User?> GetUserAsync(string id) => throw new NotSupportedException();
    }

    // Only GetAllAsync is exercised; the rest exist because the interface is
    // wide and the import only ever asks it one question.
    private sealed class FakePolicyService : IPolicyService
    {
        public Task<IEnumerable<PolicyDto>> GetAllAsync() =>
            Task.FromResult<IEnumerable<PolicyDto>>(
            [
                new() { Id = "pol-full", Name = "Full-time" },
                new() { Id = "pol-probation", Name = "Probation" },
            ]);

        public Task<PolicySaveResult> CreateAsync(SavePolicyDto dto) => throw new NotSupportedException();
        public Task<PolicySaveResult> UpdateAsync(string id, SavePolicyDto dto) => throw new NotSupportedException();
        public Task<PolicyDto?> SetArchivedAsync(string id, bool archived) => throw new NotSupportedException();
        public Task<PolicyDto?> SetDefaultAsync(string id) => throw new NotSupportedException();
        public Task<EmployeePolicy?> GetEffectivePolicyAsync(string employeeId) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, EmployeePolicy>> GetEffectivePoliciesForEmployeesAsync(
            IEnumerable<string> employeeIds) => throw new NotSupportedException();
        public Task<bool> RequiresGeofenceAsync(string employeeId) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, double>> GetLeaveEntitlementsAsync(string employeeId) =>
            throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>
            GetLeaveEntitlementsForEmployeesAsync(IEnumerable<string> employeeIds) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<EmployeePolicy>> GetAllAcrossOrgsAsync() => throw new NotSupportedException();
        public Task<IReadOnlyList<PolicyLeaveEntitlement>> GetAllPolicyEntitlementsAsync() =>
            throw new NotSupportedException();
    
    // Module access is not what these tests are about; full access keeps them
    // on topic.
    public Task<AltomateHR.Api.Modules.Policies.PolicyModuleAccess> GetModuleAccessAsync(string employeeId) =>
        Task.FromResult(AltomateHR.Api.Modules.Policies.PolicyModuleAccess.All);
}

    private sealed class FakeShiftService : IShiftService
    {
        public Task<IEnumerable<ShiftDto>> GetAllAsync() =>
            Task.FromResult<IEnumerable<ShiftDto>>(
                [new() { Id = "shift-office", Name = "Office Hours" }]);

        public Task<IEnumerable<ShiftDto>> GetForProjectAsync(string projectId) => throw new NotSupportedException();
        public Task<ShiftSaveResult> CreateAsync(CreateShiftDto dto) => throw new NotSupportedException();
        public Task<ShiftSaveResult> UpdateAsync(string id, UpdateShiftDto dto) => throw new NotSupportedException();
        public Task<ShiftDeleteResult> DeleteAsync(string id) => throw new NotSupportedException();
        public Task<ShiftSaveResult> SetDefaultAsync(string id) => throw new NotSupportedException();
        public Task<ShiftSaveResult> SetArchivedAsync(string id, bool archived) => throw new NotSupportedException();
        public Task<Shift?> GetEffectiveShiftAsync(string employeeId) => throw new NotSupportedException();
        public Task<Shift?> GetByIdAsync(string id) => throw new NotSupportedException();
    }
}
