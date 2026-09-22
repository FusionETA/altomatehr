using System.Text;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Dtos;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Modules.Shifts;
using AltomateHR.Api.Modules.Shifts.Dtos;
using AltomateHR.Api.Modules.Shifts.Entities;

namespace AltomateHR.Api.Tests.Employees;

// Bulk onboarding.
//
// Two things here are worth a test each because getting them wrong is silent:
// a blank cell must LEAVE A FIELD ALONE (UpdateEmployeeDto overwrites policy,
// shift, modules and role unconditionally, so sending its defaults would reset
// them), and a created account's password must be random per person rather than
// derived from anything about them.
public class EmployeeImportTests
{
    // ─── Fixtures ───────────────────────────────────────────────────────

    private static byte[] Csv(params string[] dataRows)
    {
        var header = string.Join(",", EmployeeImportSheet.Columns.Select(
            c => c.Required ? $"*{c.Label}" : c.Label));
        return Encoding.UTF8.GetBytes(header + "\r\n" + string.Join("\r\n", dataRows) + "\r\n");
    }

    private static string Row(
        string email, string name = "", string role = "", string number = "",
        string jobTitle = "", string joinDate = "", string policy = "", string shift = "") =>
        string.Join(",", email, name, role, number, jobTitle, joinDate, policy, shift);

    private static (EmployeeImportService Service, FakeEmployeeService Employees) Make(
        params EmployeeDto[] existing)
    {
        var employees = new FakeEmployeeService(existing);
        return (new EmployeeImportService(employees, new FakePolicyService(), new FakeShiftService()),
            employees);
    }

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

    // ─── Creating ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreatesAPersonWhoIsNotInTheOrgYet()
    {
        var (service, employees) = Make();

        // Deliberately NOT the template's example values — those are skipped
        // by IsExampleRow, which is its own test below.
        var result = await service.ImportAsync(
            Csv(Row("chan@example.com", "Chan Mei Ling", "Employee", "EMP-042",
                "QS Executive", "2026-01-15", "Full-time", "Office Hours")),
            TabularFormat.Csv);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Updated);

        var created = Assert.Single(employees.Created);
        Assert.Equal("chan@example.com", created.Email);
        Assert.Equal("Chan Mei Ling", created.Name);
        Assert.Equal("EMP-042", created.EmployeeNumber);
        Assert.Equal(new DateTime(2026, 1, 15), created.JoinDate);
        // Names in the sheet, ids in the call.
        Assert.Equal("pol-full", created.PolicyId);
        Assert.Equal("shift-office", created.ShiftId);
    }

    // The whole point of returning them: they are shown once and stored nowhere.
    [Fact]
    public async Task ReturnsThePasswordForEachAccountItCreated()
    {
        var (service, employees) = Make();

        var result = await service.ImportAsync(
            Csv(Row("a@example.com", "Person A"), Row("b@example.com", "Person B")),
            TabularFormat.Csv);

        Assert.Equal(2, result.CreatedAccounts.Count);
        Assert.Equal(
            result.CreatedAccounts.Select(a => a.Password),
            employees.Created.Select(c => c.Password));
        Assert.All(result.CreatedAccounts, a => Assert.False(string.IsNullOrWhiteSpace(a.Password)));
    }

    // The reference app derives this from the employee's email and date of
    // birth, which makes every account guessable by anyone who knows them.
    [Fact]
    public async Task PasswordsAreRandomPerPersonRatherThanDerived()
    {
        var (service, _) = Make();

        var result = await service.ImportAsync(
            Csv(Row("a@example.com", "Person A"), Row("b@example.com", "Person B")),
            TabularFormat.Csv);

        var passwords = result.CreatedAccounts.Select(a => a.Password).ToList();
        Assert.Equal(2, passwords.Distinct().Count());
        Assert.All(passwords, p => Assert.DoesNotContain("example.com", p));
        // No 0/O/1/l/I — it gets read off a screen and typed by someone else.
        Assert.All(passwords, p => Assert.DoesNotContain(p, c => "0O1lI".Contains(c)));
    }

    [Fact]
    public async Task ANewPersonWithoutANameFailsTheirRowOnly()
    {
        var (service, employees) = Make();

        var result = await service.ImportAsync(
            Csv(Row("nameless@example.com"), Row("fine@example.com", "Perfectly Fine")),
            TabularFormat.Csv);

        Assert.False(result.Ok);
        Assert.Contains("Name column is required", Assert.Single(result.Errors).Message);
        // The good row still landed — each row is an independent person.
        Assert.Equal(1, result.Created);
        Assert.Equal("fine@example.com", Assert.Single(employees.Created).Email);
    }

    // ─── Updating, and the fields that must survive it ──────────────────

    [Fact]
    public async Task AnExistingEmailIsUpdatedRatherThanDuplicated()
    {
        var (service, employees) = Make(Member("usr-1", "aisyah@example.com"));

        var result = await service.ImportAsync(
            Csv(Row("aisyah@example.com", jobTitle: "Safety Officer")), TabularFormat.Csv);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);
        Assert.Empty(employees.Created);
        Assert.Equal("Safety Officer", Assert.Single(employees.Updated).Dto.JobTitle);
    }

    // UpdateEmployeeDto writes policy, shift, modules and role unconditionally,
    // so a blank column would clear them. Blank means "leave alone".
    [Fact]
    public async Task BlankColumnsDoNotClearPolicyShiftRoleOrModules()
    {
        var (service, employees) = Make(Member(
            "usr-1", "aisyah@example.com",
            role: "Supervisor", policyId: "pol-full", shiftId: "shift-office",
            modules: ["claims", "leave"]));

        var result = await service.ImportAsync(
            Csv(Row("aisyah@example.com", jobTitle: "Safety Officer")), TabularFormat.Csv);

        Assert.True(result.Ok, result.Message);
        var sent = Assert.Single(employees.Updated).Dto;
        Assert.Equal("Supervisor", sent.Role);
        Assert.Equal("pol-full", sent.PolicyId);
        Assert.Equal("shift-office", sent.ShiftId);
        Assert.Equal(["claims", "leave"], sent.Modules);
    }

    [Fact]
    public async Task AFilledColumnStillOverridesTheExistingValue()
    {
        var (service, employees) = Make(Member("usr-1", "aisyah@example.com", policyId: "pol-full"));

        await service.ImportAsync(
            Csv(Row("aisyah@example.com", policy: "Probation")), TabularFormat.Csv);

        Assert.Equal("pol-probation", Assert.Single(employees.Updated).Dto.PolicyId);
    }

    // Matching is on email, so the login must never be rewritten from the sheet.
    [Fact]
    public async Task TheEmailColumnIsNeverWrittenBack()
    {
        var (service, employees) = Make(Member("usr-1", "aisyah@example.com"));

        await service.ImportAsync(Csv(Row("aisyah@example.com", "New Name")), TabularFormat.Csv);

        Assert.Null(Assert.Single(employees.Updated).Dto.Email);
    }

    // ─── Rejection ──────────────────────────────────────────────────────

    [Fact]
    public async Task AnUnknownPolicyNameFailsTheRowRatherThanSilentlyDefaulting()
    {
        var (service, employees) = Make();

        var result = await service.ImportAsync(
            Csv(Row("a@example.com", "Person A", policy: "Platinum")), TabularFormat.Csv);

        Assert.False(result.Ok);
        Assert.Contains("No policy named \"Platinum\"", Assert.Single(result.Errors).Message);
        Assert.Empty(employees.Created);
    }

    [Fact]
    public async Task AMissingEmailColumnRejectsTheFile()
    {
        var (service, _) = Make();
        var content = Encoding.UTF8.GetBytes("Name,Role\r\nAisyah,Employee\r\n");

        var result = await service.ImportAsync(content, TabularFormat.Csv);

        Assert.False(result.Ok);
        Assert.Contains("Missing required column", result.Message);
    }

    [Fact]
    public async Task TheTemplatesExampleRowIsNotImported()
    {
        var (service, employees) = Make();
        var template = service.BuildTemplate(TabularFormat.Csv);

        var result = await service.ImportAsync(template.Content, TabularFormat.Csv);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(0, result.Created);
        Assert.Empty(employees.Created);
    }

    // ─── The export round-trips ─────────────────────────────────────────

    // Export, edit, import back. If the two shapes drifted, an admin's first
    // import would fail on a file this system produced.
    [Fact]
    public async Task TheExportImportsBackCleanly()
    {
        var (service, employees) = Make(
            Member("usr-1", "aisyah@example.com"),
            Member("usr-2", "chan@example.com", role: "Supervisor"));

        var export = await service.ExportAsync(TabularFormat.Csv);
        var result = await service.ImportAsync(export.Content, TabularFormat.Csv);

        Assert.True(result.Ok, result.Message);
        // Nobody new, and nobody's values moved.
        Assert.Equal(0, result.Created);
        Assert.Equal(2, result.Updated);
        Assert.Empty(employees.Created);

        var back = employees.Updated.Single(u => u.Id == "usr-2").Dto;
        Assert.Equal("Supervisor", back.Role);
        Assert.Equal("E-001", back.EmployeeNumber);
        Assert.Equal("Site Engineer", back.JobTitle);
    }

    // Names, not ids — the import reads them back by name.
    [Fact]
    public async Task TheExportWritesPolicyAndShiftNamesNotIds()
    {
        var (service, _) = Make(Member("usr-1", "aisyah@example.com"));

        var export = await service.ExportAsync(TabularFormat.Csv);
        var text = Encoding.UTF8.GetString(export.Content);

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
        var (service, _) = Make(Member("usr-1", "aisyah@example.com"));

        var export = await service.ExportAsync(TabularFormat.Csv);

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
            return Task.FromResult(new EmployeeSaveResult(true, new EmployeeDto { Email = dto.Email }, null));
        }

        public Task<EmployeeSaveResult> UpdateAsync(string id, UpdateEmployeeDto dto)
        {
            Updated.Add((id, dto));
            return Task.FromResult(new EmployeeSaveResult(true, new EmployeeDto { Id = id }, null));
        }
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
