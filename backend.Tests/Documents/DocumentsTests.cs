using AltomateHR.Api.Common;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Documents;
using AltomateHR.Api.Modules.Documents.Dtos;
using AltomateHR.Api.Modules.Documents.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Documents;

// HR letters: the merge field registry and markup, the template library, and
// the rule that a letter never goes out with a blank where a detail should be.
public class DocumentsTests
{
    // ─── Registry ────────────────────────────────────────────────────────

    [Fact]
    public void Unknown_fields_are_flagged_and_input_fields_are_allowed()
    {
        var unknown = MergeFields.UnknownKeysIn(
            "{{employee.name}} {{employee.shoeSize}} {{input.lastWorkingDay}} {{input.}} {{ today }}");

        Assert.Equal(["employee.shoeSize", "input."], unknown);
    }

    [Fact]
    public void Every_letter_uses_the_date_and_signature_fields()
    {
        var used = MergeFields.UsedKeys("Dear {{employee.name}}");

        Assert.Equal("employee.name", used[0]);
        Assert.Contains("today", used);
        Assert.Contains("signatory.name", used);
        Assert.Contains("company.name", used);
    }

    [Fact]
    public void Input_fields_are_described_from_their_name()
    {
        var day = MergeFields.DescribeInput("input.lastWorkingDay");
        var text = MergeFields.DescribeInput("input.misconduct_summary");

        Assert.Equal("Last working day", day.Label);
        Assert.Equal(MergeFieldKind.Date, day.Kind);
        Assert.Equal("Misconduct summary", text.Label);
        Assert.Equal(MergeFieldKind.Text, text.Kind);
    }

    [Fact]
    public void Typed_dates_and_money_print_in_letter_form()
    {
        Assert.Equal("8 October 2026", MergeFields.FormatTyped(MergeFieldKind.Date, "2026-10-08"));
        Assert.Equal("RM 4,500.00", MergeFields.FormatTyped(MergeFieldKind.Money, "4500"));
        Assert.Equal("two weeks", MergeFields.FormatTyped(MergeFieldKind.Date, "two weeks"));
    }

    // ─── Markup ──────────────────────────────────────────────────────────

    [Fact]
    public void Markup_parses_headings_bullets_bold_and_keeps_values_literal()
    {
        var blocks = LetterMarkup.Parse(
            "# Title\n\nDear {{employee.name}},\nline two **bold**\n\n- one\n- two",
            key => key == "employee.name" ? "**Ali** - Abu" : "");

        Assert.Equal(LetterMarkup.BlockKind.Heading1, blocks[0].Kind);
        Assert.Equal(LetterMarkup.BlockKind.Paragraph, blocks[1].Kind);
        Assert.Equal(2, blocks[1].Lines.Count);
        // The value's ** is printed as typed, not turned into bold.
        Assert.Equal("Dear **Ali** - Abu,", string.Concat(blocks[1].Lines[0].Select(s => s.Text)));
        Assert.Contains(blocks[1].Lines[1], s => s.Bold && s.Text == "bold");
        Assert.Equal(LetterMarkup.BlockKind.Bullets, blocks[2].Kind);
        Assert.Equal(2, blocks[2].Lines.Count);
    }

    // ─── Templates ───────────────────────────────────────────────────────

    [Fact]
    public async Task A_template_with_unknown_fields_is_refused_with_the_list()
    {
        using var db = Db();
        var service = new DocumentTemplateService(new DocumentTemplateRepository(db), new StubUser());

        var result = await service.CreateAsync(new SaveDocumentTemplateDto
        {
            Name = "Bad", Body = "Hi {{employee.nickname}} {{input.ok}}",
        });

        Assert.False(result.Ok);
        Assert.Equal(["employee.nickname"], result.UnknownFields);
        Assert.Empty(db.DocumentTemplates);
    }

    [Fact]
    public async Task Samples_are_valid_and_adding_them_twice_does_not_duplicate()
    {
        using var db = Db();
        var service = new DocumentTemplateService(new DocumentTemplateRepository(db), new StubUser());

        var first = await service.AddSamplesAsync();
        var second = await service.AddSamplesAsync();

        Assert.Equal(4, first.Added);
        Assert.Equal(0, second.Added);
        Assert.Equal(4, db.DocumentTemplates.Count());
        Assert.All(SampleTemplates.All, s => Assert.Empty(MergeFields.UnknownKeysIn(s.Body)));
        Assert.All(db.DocumentTemplates, t => Assert.Equal("org-1", t.OrganizationId));
    }

    [Fact]
    public async Task Another_companys_templates_are_invisible()
    {
        var name = $"docs-{Guid.NewGuid()}";
        using (var other = Db(name, new StubUser { OrganizationId = "org-2" }))
        {
            other.DocumentTemplates.Add(new DocumentTemplate { Name = "Theirs", Body = "x" });
            other.SaveChanges();
        }

        using var db = Db(name);
        var service = new DocumentTemplateService(new DocumentTemplateRepository(db), new StubUser());

        Assert.Empty(await service.GetAllAsync());
    }

    // ─── Resolve / generate ──────────────────────────────────────────────

    private const string Body =
        "Dear {{employee.name}}, IC {{employee.idNumber}}. Reg {{company.registrationNo}}. Ends {{input.lastDay}}.";

    [Fact]
    public async Task Resolve_returns_only_the_fields_the_template_uses_and_flags_gaps()
    {
        using var db = Db();
        var template = Seed(db, idNumber: null);
        var (service, _, _) = Letters(db);

        var result = await service.ResolveAsync(template.Id, "usr-emp");

        Assert.True(result.Ok);
        var fields = result.Letter!.Fields.ToDictionary(f => f.Key);
        Assert.Equal("Siti Aminah", fields["employee.name"].Value);
        Assert.False(fields["employee.name"].Missing);

        Assert.True(fields["employee.idNumber"].Missing);
        Assert.True(fields["employee.idNumber"].WritableToEmployee);

        Assert.True(fields["company.registrationNo"].Missing);
        Assert.False(fields["company.registrationNo"].WritableToEmployee);
        Assert.Contains("Payroll", fields["company.registrationNo"].FixHint);

        Assert.True(fields["input.lastDay"].Missing);
        Assert.Equal(MergeFieldSource.Input, fields["input.lastDay"].Source);

        Assert.DoesNotContain("employee.monthlySalary", fields.Keys);
    }

    [Fact]
    public async Task Generate_refuses_while_any_field_is_blank()
    {
        using var db = Db();
        var template = Seed(db, idNumber: null);
        var (service, storage, _) = Letters(db);

        var result = await service.GenerateAsync(template.Id, new GenerateLetterDto
        {
            EmployeeUserId = "usr-emp",
            Values = new() { ["input.lastDay"] = "2026-11-30" },
        });

        Assert.False(result.Ok);
        Assert.Equal(["employee.idNumber", "company.registrationNo"], result.MissingFields);
        Assert.Empty(db.GeneratedDocuments);
        Assert.Empty(storage.Files);
    }

    [Fact]
    public async Task Generate_with_every_gap_settled_saves_the_letter_and_writes_back_only_empty_fields()
    {
        using var db = Db();
        var template = Seed(db, idNumber: null);
        var (service, storage, audit) = Letters(db);

        var result = await service.GenerateAsync(template.Id, new GenerateLetterDto
        {
            EmployeeUserId = "usr-emp",
            Values = new()
            {
                ["employee.idNumber"] = "900101-14-5678",
                ["input.lastDay"] = "2026-11-30",
                // A typed override of a field ON FILE never reaches the record.
                ["employee.name"] = "Puan Siti Aminah",
            },
            LeaveBlank = ["company.registrationNo"],
            SaveToEmployee = ["employee.idNumber", "employee.name"],
            Save = true,
        });

        Assert.True(result.Ok);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(result.Content!, 0, 4));

        var saved = Assert.Single(db.GeneratedDocuments);
        Assert.Equal("usr-emp", saved.EmployeeUserId);
        Assert.Equal("org-1", saved.OrganizationId);
        Assert.Contains("30 November 2026", saved.ValuesJson);
        Assert.Contains("Puan Siti Aminah", saved.ValuesJson);
        Assert.Single(storage.Files);

        var profile = db.EmployeeProfiles.Single(p => p.UserId == "usr-emp");
        Assert.Equal("900101-14-5678", profile.IdNumber);
        Assert.Equal("Siti Aminah", db.Users.Single(u => u.Id == "usr-emp").Name);
        Assert.True(audit.Recorded(AuditActions.DocumentsLetterGenerate));
    }

    [Fact]
    public async Task A_limited_admin_cannot_write_a_letter_for_someone_outside_their_scope()
    {
        var scope = new EmployeeScope();
        scope.Limit(["usr-admin"], []);
        using var db = Db(scope: scope);
        var template = Seed(db, idNumber: "1");
        var (service, _, _) = Letters(db, scope);

        Assert.False((await service.ResolveAsync(template.Id, "usr-emp")).Ok);
        var generate = await service.GenerateAsync(template.Id, new GenerateLetterDto { EmployeeUserId = "usr-emp" });
        Assert.False(generate.Ok);
        Assert.Null(generate.Error);   // reads as not found
    }

    // ─── Employees grant / scopes ────────────────────────────────────────
    //
    // A letter prints the employee's record, which is the Employees module's,
    // not Documents'. Documents access alone must not open it.

    [Fact]
    public async Task Without_employees_access_resolve_preview_and_generate_are_forbidden()
    {
        using var db = Db();
        var template = Seed(db, idNumber: "900101-14-5678");
        var (service, storage, _) = Letters(db, employees: ModuleLevel.None);

        var resolve = await service.ResolveAsync(template.Id, "usr-emp");
        var preview = await service.PreviewAsync(template.Id, new PreviewLetterDto { EmployeeUserId = "usr-emp" });
        var generate = await service.GenerateAsync(template.Id, new GenerateLetterDto
        {
            EmployeeUserId = "usr-emp",
            Values = new() { ["input.lastDay"] = "2026-11-30" },
            LeaveBlank = ["company.registrationNo"],
        });

        Assert.True(resolve.Forbidden);
        Assert.Null(resolve.Letter);
        Assert.Contains("Employees", resolve.Error);
        Assert.True(preview.Forbidden);
        Assert.Null(preview.Content);
        Assert.True(generate.Forbidden);
        Assert.Null(generate.Content);
        Assert.Empty(db.GeneratedDocuments);
        Assert.Empty(storage.Files);

        // A preview with no employee reads only company details: still allowed.
        var draft = await service.PreviewAsync(template.Id, new PreviewLetterDto());
        Assert.True(draft.Ok);
    }

    [Fact]
    public async Task View_only_employees_access_reads_but_cannot_save_to_the_record()
    {
        using var db = Db();
        var template = Seed(db, idNumber: null);
        var (service, storage, _) = Letters(db, employees: ModuleLevel.View);

        var resolve = await service.ResolveAsync(template.Id, "usr-emp");
        Assert.True(resolve.Ok);
        var idField = resolve.Letter!.Fields.Single(f => f.Key == "employee.idNumber");
        Assert.True(idField.Missing);
        Assert.False(idField.WritableToEmployee);   // not offered without Manage

        var request = new GenerateLetterDto
        {
            EmployeeUserId = "usr-emp",
            Values = new() { ["employee.idNumber"] = "900101-14-5678", ["input.lastDay"] = "2026-11-30" },
            LeaveBlank = ["company.registrationNo"],
            SaveToEmployee = ["employee.idNumber"],
            Save = true,
        };

        // Refused, not silently skipped — and nothing is kept or written.
        var refused = await service.GenerateAsync(template.Id, request);
        Assert.True(refused.Forbidden);
        Assert.Contains("manage access to Employees", refused.Error);
        Assert.Null(db.EmployeeProfiles.Single(p => p.UserId == "usr-emp").IdNumber);
        Assert.Empty(db.GeneratedDocuments);
        Assert.Empty(storage.Files);

        request.SaveToEmployee = null;
        var letter = await service.GenerateAsync(template.Id, request);
        Assert.True(letter.Ok);
        Assert.Null(db.EmployeeProfiles.Single(p => p.UserId == "usr-emp").IdNumber);
    }

    [Theory]
    [InlineData(ModuleLevel.None, "employees:read")]
    [InlineData(ModuleLevel.View, "employees:write")]
    public async Task An_api_key_is_told_which_employees_scope_it_lacks(ModuleLevel level, string scope)
    {
        using var db = Db();
        var template = Seed(db, idNumber: null);
        var (service, _, _) = Letters(db, employees: level, user: new StubUser { ScopedMachine = true });

        var result = await service.GenerateAsync(template.Id, new GenerateLetterDto
        {
            EmployeeUserId = "usr-emp",
            Values = new() { ["employee.idNumber"] = "1", ["input.lastDay"] = "2026-11-30" },
            LeaveBlank = ["company.registrationNo"],
            SaveToEmployee = ["employee.idNumber"],
        });

        Assert.True(result.Forbidden);
        Assert.Equal($"Caller is missing required scope: {scope}.", result.Error);
    }

    // ─── Write-back fits the columns ─────────────────────────────────────

    [Fact]
    public async Task A_long_typed_address_is_fitted_to_the_column_after_its_lines_are_joined()
    {
        using var db = Db();
        Seed(db, idNumber: "1");
        var template = new DocumentTemplate
        {
            OrganizationId = "org-1", Name = "Address", Body = "{{employee.address}} {{employee.jobTitle}}",
        };
        db.DocumentTemplates.Add(template);
        db.OrganizationMemberships.Single(m => m.UserId == "usr-emp").JobTitle = null;
        db.SaveChanges();
        var (service, _, _) = Letters(db);

        // 158 characters on four lines: under 160 as typed, 164 once each
        // line break becomes ", " — which used to overflow varchar(160).
        var address = string.Join("\n",
            new string('A', 40), new string('B', 40), new string('C', 40), new string('D', 38));
        var result = await service.GenerateAsync(template.Id, new GenerateLetterDto
        {
            EmployeeUserId = "usr-emp",
            Values = new() { ["employee.address"] = address, ["employee.jobTitle"] = new string('J', 300) },
            LeaveBlank = ["company.registrationNo"],
            SaveToEmployee = ["employee.address", "employee.jobTitle"],
        });

        Assert.True(result.Ok);
        var saved = db.EmployeeProfiles.Single(p => p.UserId == "usr-emp").AddressLine1!;
        Assert.Equal(160, saved.Length);
        Assert.StartsWith(new string('A', 40) + ", " + new string('B', 40) + ", ", saved);
        Assert.Equal(120, db.OrganizationMemberships.Single(m => m.UserId == "usr-emp").JobTitle!.Length);
    }

    // ─── Leave date after a rehire ───────────────────────────────────────

    [Fact]
    public async Task A_rehired_employee_has_no_leave_date_from_their_previous_stint()
    {
        using var db = Db();
        Seed(db, idNumber: "1");
        var template = LeaveDateTemplate(db, joinDate: new DateTime(2024, 3, 1));
        db.EmploymentPeriods.AddRange(
            new EmploymentPeriod
            {
                OrganizationId = "org-1", UserId = "usr-emp",
                JoinDate = new DateTime(2020, 1, 1), LeaveDate = new DateTime(2022, 6, 30), EndReason = "Resigned",
            },
            new EmploymentPeriod
            {
                OrganizationId = "org-1", UserId = "usr-emp",
                JoinDate = new DateTime(2024, 3, 1), StartReason = "Restored",
            });
        db.SaveChanges();
        var (service, _, _) = Letters(db);

        var resolve = await service.ResolveAsync(template.Id, "usr-emp");
        var leave = resolve.Letter!.Fields.Single(f => f.Key == "employee.leaveDate");
        Assert.True(leave.Missing);
        Assert.Null(leave.Value);

        // …so Generate asks for it rather than printing 30 June 2022.
        var generate = await service.GenerateAsync(template.Id, new GenerateLetterDto
        {
            EmployeeUserId = "usr-emp", LeaveBlank = ["company.registrationNo"],
        });
        Assert.Equal(["employee.leaveDate"], generate.MissingFields);
    }

    [Fact]
    public async Task Someone_no_longer_employed_keeps_their_last_leave_date_from_the_history()
    {
        using var db = Db();
        Seed(db, idNumber: "1");
        var template = LeaveDateTemplate(db, joinDate: new DateTime(2020, 1, 1));
        db.EmploymentPeriods.Add(new EmploymentPeriod
        {
            OrganizationId = "org-1", UserId = "usr-emp",
            JoinDate = new DateTime(2020, 1, 1), LeaveDate = new DateTime(2022, 6, 30), EndReason = "Transferred to X",
        });
        db.SaveChanges();
        var (service, _, _) = Letters(db);

        var resolve = await service.ResolveAsync(template.Id, "usr-emp");
        var leave = resolve.Letter!.Fields.Single(f => f.Key == "employee.leaveDate");
        Assert.False(leave.Missing);
        Assert.Equal("30 June 2022", leave.Value);
    }

    private static DocumentTemplate LeaveDateTemplate(AppDbContext db, DateTime joinDate)
    {
        db.EmployeeProfiles.Single(p => p.UserId == "usr-emp").JoinDate = joinDate;
        var template = new DocumentTemplate
        {
            OrganizationId = "org-1", Name = "Resignation",
            Body = "Joined {{employee.joinDate}}, last day {{employee.leaveDate}}.",
        };
        db.DocumentTemplates.Add(template);
        db.SaveChanges();
        return template;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static DocumentTemplate Seed(AppDbContext db, string? idNumber)
    {
        db.Organizations.Add(new Organization { Id = "org-1", Name = "Acme Sdn Bhd", Plan = OrgPlan.DIY, Tier = OrgPlanTier.PAID });
        db.Users.Add(new User { Id = "usr-emp", Name = "Siti Aminah", Email = "siti@acme.test" });
        db.Users.Add(new User { Id = "usr-admin", Name = "Hana Admin", Email = "admin@acme.test" });
        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = "org-1", UserId = "usr-emp", Role = "Employee", JobTitle = "Engineer",
        });
        db.EmployeeProfiles.Add(new EmployeeProfile { OrganizationId = "org-1", UserId = "usr-emp", IdNumber = idNumber });
        db.PayrollCompanyInfos.Add(new PayrollCompanyInfo
        {
            OrganizationId = "org-1", EmployerName = "Acme Sdn Bhd", DeclarantName = "Hana", DeclarantPosition = "HR Manager",
        });
        var template = new DocumentTemplate { OrganizationId = "org-1", Name = "Notice", Body = Body };
        db.DocumentTemplates.Add(template);
        db.SaveChanges();
        return template;
    }

    private static (GeneratedLetterService, MemoryStorage, FakeAuditService) Letters(
        AppDbContext db, IEmployeeScope? scope = null,
        ModuleLevel employees = ModuleLevel.Manage, ICurrentUser? user = null)
    {
        var storage = new MemoryStorage();
        var audit = new FakeAuditService();
        var profiles = new EmployeeProfileRepository(db);
        var memberships = new OrganizationMembershipRepository(db);
        var profileService = new EmployeeProfileService(profiles, memberships, null!, null!);
        var service = new GeneratedLetterService(
            new DocumentTemplateRepository(db),
            new GeneratedDocumentRepository(db),
            storage,
            memberships,
            profiles,
            new EmploymentPeriodRepository(db),
            new UserRepository(db),
            new PayrollCompanyInfoRepository(db),
            new OrganizationRepository(db),
            profileService,
            scope ?? new EmployeeScope(),
            new EmployeesAt(employees),
            user ?? new StubUser(),
            audit);
        return (service, storage, audit);
    }

    private static AppDbContext Db(string? name = null, ICurrentUser? user = null, IEmployeeScope? scope = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(name ?? $"docs-{Guid.NewGuid()}")
                .Options,
            user ?? new StubUser(),
            scope);

    private sealed class StubUser : ICurrentUser
    {
        public string? UserId { get; set; } = "usr-admin";
        public string? OrganizationId { get; set; } = "org-1";
        public string? Role { get; set; } = "Admin";
        public string? Email { get; set; } = "admin@acme.test";
        public string? IpAddress { get; set; } = "127.0.0.1";
        public bool IsAdmin => true;
        public bool IsAuthenticated => true;
        public bool ScopedMachine { get; init; }
        public bool IsScopedMachine => ScopedMachine;
    }

    // The caller's Employees level as GetCallerLevelAsync reports it (grant
    // capped by key scopes — that combination is tested in AdminAccessTests).
    private sealed class EmployeesAt(ModuleLevel employees) : IModuleAccessService
    {
        public Task<IReadOnlyCollection<string>> GetEnabledModulesAsync() =>
            Task.FromResult<IReadOnlyCollection<string>>(OrgModules.AllModules.ToList());
        public Task<IReadOnlyCollection<string>> GetOrgModulesAsync() => GetEnabledModulesAsync();
        public Task<ModuleLevel> GetModuleLevelAsync(string module) =>
            Task.FromResult(module == OrgModules.Employees ? employees : ModuleLevel.Manage);
        public Task<ModuleLevel> GetCallerLevelAsync(string module) => GetModuleLevelAsync(module);
    }

    private sealed class MemoryStorage : IGeneratedDocumentStorage
    {
        public Dictionary<string, byte[]> Files { get; } = [];

        public Task<string> StoreAsync(string organizationId, string employeeUserId, byte[] content)
        {
            var name = $"{Guid.NewGuid():N}.pdf";
            Files[$"{organizationId}/{employeeUserId}/{name}"] = content;
            return Task.FromResult(name);
        }

        public Task<byte[]?> ReadAsync(string organizationId, string employeeUserId, string storedFileName) =>
            Task.FromResult(Files.GetValueOrDefault($"{organizationId}/{employeeUserId}/{storedFileName}"));

        public void Delete(string organizationId, string employeeUserId, string storedFileName) =>
            Files.Remove($"{organizationId}/{employeeUserId}/{storedFileName}");
    }
}
