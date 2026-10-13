using System.Text;
using System.Text.Json;
using AltomateHR.Api.Common;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Documents;
using AltomateHR.Api.Modules.Documents.Dtos;
using AltomateHR.Api.Modules.Documents.Entities;
using AltomateHR.Api.Modules.Documents.Pdf;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Organizations;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Documents;

// Saved signatures on letters: which one prints (the request → the template's
// default → the company default → none), that it names the signatory unless a
// value is typed, that a removed or another company's signature is refused,
// that a missing image never fails a letter, and what the letter on file and
// the activity log record.
public class LetterSignatureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"letter-signatures-{Guid.NewGuid():N}");
    private readonly string _dbName = $"letter-signatures-{Guid.NewGuid()}";

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private static readonly GenerateLetterDto Settled = new()
    {
        EmployeeUserId = "usr-emp",
        Values = new() { ["input.lastDay"] = "2026-11-30" },
        Save = true,
    };

    private static GenerateLetterDto With(string? signatureId, Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>(Settled.Values!);
        foreach (var kv in extra ?? []) values[kv.Key] = kv.Value;
        return new GenerateLetterDto { EmployeeUserId = "usr-emp", Values = values, Save = true, SignatureId = signatureId };
    }

    // ─── Which signature prints ──────────────────────────────────────────

    [Fact]
    public async Task With_no_choice_the_company_default_prints_and_names_the_signatory()
    {
        var w = World();
        var zara = await w.AddSignature("Zara Ahmad", "Director");

        var result = await w.Letters.GenerateAsync(w.Template.Id, Settled);

        Assert.True(result.Ok);
        Assert.True(HasImage(result.Content!));
        var kept = w.Db.GeneratedDocuments.AsNoTracking().Single();
        Assert.Equal(zara.Id, kept.SignatureId);
        Assert.Equal("Zara Ahmad", kept.SignatureName);
        var values = Values(kept);
        Assert.Equal("Zara Ahmad", values["signatory.name"]);
        Assert.Equal("Director", values["signatory.position"]);

        var listed = Assert.Single((await w.Letters.ListAsync(null)).Letters!);
        Assert.Equal("Zara Ahmad", listed.SignatureName);
    }

    [Fact]
    public async Task The_templates_default_beats_the_company_default_until_it_is_removed()
    {
        var w = World();
        var companyDefault = await w.AddSignature("Zara Ahmad", "Director");
        var forTemplate = await w.AddSignature("Ben Lee", "HR Manager");
        w.Template.DefaultSignatureId = forTemplate.Id;
        w.Db.SaveChanges();

        await w.Letters.GenerateAsync(w.Template.Id, Settled);
        await w.Signatures.RemoveAsync(forTemplate.Id);
        var afterRemove = await w.Letters.GenerateAsync(w.Template.Id, Settled);

        Assert.True(afterRemove.Ok);
        Assert.True(HasImage(afterRemove.Content!));
        var kept = w.Db.GeneratedDocuments.AsNoTracking().OrderBy(d => d.CreatedAt).ToList();
        Assert.Equal([forTemplate.Id, companyDefault.Id], kept.Select(d => d.SignatureId));
        Assert.Equal("Zara Ahmad", Values(kept[1])["signatory.name"]);
    }

    [Fact]
    public async Task An_explicit_signature_prints_instead_of_the_default()
    {
        var w = World();
        await w.AddSignature("Zara Ahmad", "Director");
        var ben = await w.AddSignature("Ben Lee", "HR Manager");

        var result = await w.Letters.GenerateAsync(w.Template.Id, With(ben.Id));

        Assert.True(result.Ok);
        var kept = w.Db.GeneratedDocuments.AsNoTracking().Single();
        Assert.Equal(ben.Id, kept.SignatureId);
        Assert.Equal("HR Manager", Values(kept)["signatory.position"]);
    }

    [Fact]
    public async Task None_prints_the_blank_gap_and_the_declarant_as_before()
    {
        var w = World();
        await w.AddSignature("Zara Ahmad", "Director");

        var result = await w.Letters.GenerateAsync(w.Template.Id, With("none"));

        Assert.True(result.Ok);
        Assert.False(HasImage(result.Content!));
        var kept = w.Db.GeneratedDocuments.AsNoTracking().Single();
        Assert.Null(kept.SignatureId);
        Assert.Null(kept.SignatureName);
        Assert.Equal("Hana", Values(kept)["signatory.name"]);
        Assert.Equal("HR Manager", Values(kept)["signatory.position"]);
    }

    [Fact]
    public async Task Without_any_signature_a_letter_is_exactly_as_before()
    {
        var w = World();

        var result = await w.Letters.GenerateAsync(w.Template.Id, Settled);
        var resolved = await w.Letters.ResolveAsync(w.Template.Id, "usr-emp");

        Assert.True(result.Ok);
        Assert.False(HasImage(result.Content!));
        Assert.Equal("Hana", Values(w.Db.GeneratedDocuments.AsNoTracking().Single())["signatory.name"]);
        Assert.Null(resolved.Letter!.Signature);
    }

    [Fact]
    public async Task A_value_typed_for_the_letter_still_overrides_the_signatures_name()
    {
        var w = World();
        await w.AddSignature("Zara Ahmad", "Director");

        var result = await w.Letters.GenerateAsync(w.Template.Id,
            With(null, new() { ["signatory.name"] = "Zara binti Ahmad", ["signatory.position"] = "Managing Director" }));

        Assert.True(result.Ok);
        var values = Values(w.Db.GeneratedDocuments.AsNoTracking().Single());
        Assert.Equal("Zara binti Ahmad", values["signatory.name"]);
        Assert.Equal("Managing Director", values["signatory.position"]);
    }

    [Fact]
    public async Task A_signature_fills_the_signatory_when_payroll_has_no_declarant()
    {
        var w = World(declarant: false);

        // No declarant and no signature: the signatory is a gap.
        var gap = await w.Letters.GenerateAsync(w.Template.Id, Settled);
        Assert.False(gap.Ok);
        Assert.Contains("signatory.name", gap.MissingFields!);

        var zara = await w.AddSignature("Zara Ahmad", "Director");
        var result = await w.Letters.GenerateAsync(w.Template.Id, With(zara.Id));
        var chain = await w.Letters.GenerateAsync(w.Template.Id, Settled);

        Assert.True(result.Ok);
        Assert.True(chain.Ok);
        var values = Values(w.Db.GeneratedDocuments.AsNoTracking().OrderBy(d => d.CreatedAt).First());
        Assert.Equal("Zara Ahmad", values["signatory.name"]);
        Assert.Equal("Director", values["signatory.position"]);

        // "none" brings the gap back: the declarant is still missing.
        var none = await w.Letters.GenerateAsync(w.Template.Id, With("none"));
        Assert.False(none.Ok);
        Assert.Contains("signatory.name", none.MissingFields!);
    }

    [Fact]
    public async Task Resolve_keeps_the_declarant_in_the_signatory_fields_and_returns_the_signature_apart()
    {
        var w = World();
        var zara = await w.AddSignature("Zara Ahmad", "Director");

        var resolved = await w.Letters.ResolveAsync(w.Template.Id, "usr-emp");

        var fields = resolved.Letter!.Fields.ToDictionary(f => f.Key);
        Assert.Equal("Hana", fields["signatory.name"].Value);
        Assert.Equal("HR Manager", fields["signatory.position"].Value);
        Assert.False(fields["signatory.name"].Missing);
        Assert.Equal(zara.Id, resolved.Letter.Signature!.Id);
        Assert.Equal("Zara Ahmad", resolved.Letter.Signature.Name);
        Assert.Equal("Director", resolved.Letter.Signature.Position);
    }

    [Fact]
    public async Task Resolve_offers_the_templates_default_for_the_dialog_to_preselect()
    {
        var w = World();
        await w.AddSignature("Zara Ahmad", "Director");
        var ben = await w.AddSignature("Ben Lee", "HR Manager");
        w.Template.DefaultSignatureId = ben.Id;
        w.Db.SaveChanges();

        var resolved = await w.Letters.ResolveAsync(w.Template.Id, "usr-emp");

        Assert.Equal(ben.Id, resolved.Letter!.Signature!.Id);
        Assert.Equal("Ben Lee", resolved.Letter.Signature.Name);
        Assert.Equal("Hana", resolved.Letter.Fields.Single(f => f.Key == "signatory.name").Value);
    }

    // ─── Refused ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_removed_or_unknown_signature_is_refused_on_generate_and_preview()
    {
        var w = World();
        var zara = await w.AddSignature("Zara Ahmad", "Director");
        await w.Signatures.RemoveAsync(zara.Id);

        foreach (var id in new[] { zara.Id, "not-a-signature" })
        {
            var generated = await w.Letters.GenerateAsync(w.Template.Id, With(id));
            var preview = await w.Letters.PreviewAsync(w.Template.Id, new PreviewLetterDto { SignatureId = id });
            var draft = await w.Letters.PreviewAsync(null, new PreviewLetterDto { Body = "Hi", SignatureId = id });

            foreach (var r in new[] { generated, preview, draft })
            {
                Assert.False(r.Ok);
                Assert.False(r.Forbidden);
                Assert.Contains("signature", r.Error, StringComparison.OrdinalIgnoreCase);
            }
        }
        Assert.Empty(w.Db.GeneratedDocuments);
    }

    [Fact]
    public async Task Another_companys_signature_is_refused_like_an_unknown_one()
    {
        var other = World("org-2");
        var theirs = await other.AddSignature("Not Ours", "CEO");
        var w = World();

        var generated = await w.Letters.GenerateAsync(w.Template.Id, With(theirs.Id));
        var preview = await w.Letters.PreviewAsync(w.Template.Id, new PreviewLetterDto { SignatureId = theirs.Id });

        Assert.False(generated.Ok);
        Assert.NotNull(generated.Error);
        Assert.False(preview.Ok);
        // And their default never prints on our letters.
        var ours = await w.Letters.GenerateAsync(w.Template.Id, Settled);
        Assert.True(ours.Ok);
        Assert.False(HasImage(ours.Content!));
        Assert.Null(w.Db.GeneratedDocuments.AsNoTracking().Single().SignatureId);
    }

    // ─── Never fails a letter ────────────────────────────────────────────

    [Fact]
    public async Task A_signature_whose_image_is_gone_prints_the_blank_gap_and_is_not_recorded_as_signed()
    {
        var w = World();
        var zara = await w.AddSignature("Zara Ahmad", "Director");
        foreach (var f in Directory.GetFiles(Path.Combine(_root, "storage", "org-branding", "org-1"))) File.Delete(f);

        var result = await w.Letters.GenerateAsync(w.Template.Id, Settled);

        Assert.True(result.Ok);
        Assert.False(HasImage(result.Content!));
        var kept = w.Db.GeneratedDocuments.AsNoTracking().Single();
        // No image went out, so the letter isn't on file as signed with it…
        Assert.Null(kept.SignatureId);
        Assert.Null(kept.SignatureName);
        Assert.Null(Assert.Single((await w.Letters.ListAsync(null)).Letters!).SignatureName);
        // …though its name still printed under the blank gap.
        Assert.Equal("Zara Ahmad", Values(kept)["signatory.name"]);

        // And the activity log doesn't claim it either.
        var entry = w.LetterAudit.Written.Single(e => e.Action == AuditActions.DocumentsLetterGenerate);
        Assert.DoesNotContain("with the signature", entry.Summary);
        Assert.Contains("unavailable", entry.Summary);
        var metadata = JsonSerializer.SerializeToElement(entry.Metadata);
        Assert.Equal(JsonValueKind.Null, metadata.GetProperty("SignatureId").ValueKind);
        Assert.Equal(JsonValueKind.Null, metadata.GetProperty("SignatureName").ValueKind);
        Assert.Equal(zara.Id, metadata.GetProperty("UnavailableSignatureId").GetString());
    }

    [Fact]
    public async Task A_damaged_signature_file_is_not_recorded_as_signed()
    {
        var w = World();
        await w.AddSignature("Zara Ahmad", "Director");
        var path = Directory.GetFiles(Path.Combine(_root, "storage", "org-branding", "org-1")).Single();
        File.WriteAllBytes(path, File.ReadAllBytes(path)[..60]);

        var result = await w.Letters.GenerateAsync(w.Template.Id, Settled);

        Assert.True(result.Ok);
        Assert.False(HasImage(result.Content!));
        Assert.Null(w.Db.GeneratedDocuments.AsNoTracking().Single().SignatureName);
        Assert.DoesNotContain("with the signature",
            w.LetterAudit.Written.Single(e => e.Action == AuditActions.DocumentsLetterGenerate).Summary);
    }

    [Fact]
    public void Only_readable_images_count_as_drawable()
    {
        Assert.True(LetterPdf.CanDrawImage(OrganizationLogoTests.Picture("png", 300, 100)));
        Assert.False(LetterPdf.CanDrawImage(null));
        Assert.False(LetterPdf.CanDrawImage([]));
        Assert.False(LetterPdf.CanDrawImage(Encoding.UTF8.GetBytes("<svg/>")));
    }

    [Fact]
    public async Task Previews_print_the_chosen_signature()
    {
        var w = World();
        var zara = await w.AddSignature("Zara Ahmad", "Director");

        var chain = await w.Letters.PreviewAsync(w.Template.Id, new PreviewLetterDto());
        var draft = await w.Letters.PreviewAsync(null, new PreviewLetterDto { Body = "Hello", SignatureId = zara.Id });
        var none = await w.Letters.PreviewAsync(w.Template.Id, new PreviewLetterDto { SignatureId = "none" });

        Assert.True(HasImage(chain.Content!));
        Assert.True(HasImage(draft.Content!));
        Assert.False(HasImage(none.Content!));
    }

    // ─── Activity log ────────────────────────────────────────────────────

    [Fact]
    public async Task The_letter_generate_entry_names_the_signature_used()
    {
        var w = World();
        var zara = await w.AddSignature("Zara Ahmad", "Director");

        await w.Letters.GenerateAsync(w.Template.Id, Settled);

        var entry = w.LetterAudit.Written.Single(e => e.Action == AuditActions.DocumentsLetterGenerate);
        var metadata = JsonSerializer.SerializeToElement(entry.Metadata);
        Assert.Equal(zara.Id, metadata.GetProperty("SignatureId").GetString());
        Assert.Equal("Zara Ahmad", metadata.GetProperty("SignatureName").GetString());
        Assert.Contains("Zara Ahmad", entry.Summary);
    }

    // ─── Template default ────────────────────────────────────────────────

    [Fact]
    public async Task A_template_default_must_be_an_active_signature_of_this_company()
    {
        var w = World();
        var zara = await w.AddSignature("Zara Ahmad", "Director");
        var theirs = await World("org-2").AddSignature("Not Ours", "CEO");
        var templates = new DocumentTemplateService(new DocumentTemplateRepository(w.Db), w.User, w.Signatures);

        var saved = await templates.CreateAsync(new SaveDocumentTemplateDto { Name = "Offer", Body = "Hi", DefaultSignatureId = zara.Id });
        var unknown = await templates.CreateAsync(new SaveDocumentTemplateDto { Name = "Offer", Body = "Hi", DefaultSignatureId = "nope" });
        var foreign = await templates.UpdateAsync(saved.Template!.Id,
            new SaveDocumentTemplateDto { Name = "Offer", Body = "Hi", DefaultSignatureId = theirs.Id });

        Assert.True(saved.Ok);
        Assert.Equal(zara.Id, saved.Template.DefaultSignatureId);
        Assert.False(unknown.Ok);
        Assert.NotNull(unknown.Error);
        Assert.False(foreign.Ok);
        Assert.NotNull(foreign.Error);
        Assert.Equal(zara.Id, (await templates.GetAsync(saved.Template.Id))!.DefaultSignatureId);

        // Removed later: it reads as none (the company default applies).
        await w.Signatures.RemoveAsync(zara.Id);
        Assert.Null((await templates.GetAsync(saved.Template.Id))!.DefaultSignatureId);
        Assert.Null((await templates.GetAllAsync()).Single(t => t.Id == saved.Template.Id).DefaultSignatureId);

        // Blank clears it.
        var cleared = await templates.UpdateAsync(saved.Template.Id,
            new SaveDocumentTemplateDto { Name = "Offer", Body = "Hi", DefaultSignatureId = " " });
        Assert.True(cleared.Ok);
        Assert.Null(cleared.Template!.DefaultSignatureId);
    }

    // ─── The PDF itself ──────────────────────────────────────────────────

    [Theory]
    [InlineData("none")]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("webp")]
    [InlineData("wide")]
    [InlineData("tall")]
    [InlineData("broken")]
    public void The_letter_renders_with_without_and_despite_a_broken_signature(string kind)
    {
        byte[]? bytes = kind switch
        {
            "none" => null,
            "wide" => OrganizationLogoTests.Picture("png", 1200, 60),
            "tall" => OrganizationLogoTests.Picture("png", 40, 400),
            "broken" => OrganizationLogoTests.Picture("png", 300, 100)[..40],
            _ => OrganizationLogoTests.Picture(kind, 300, 100),
        };

        var pdf = LetterPdf.Render(OrganizationLogoTests.Letter(null) with { Signature = bytes });

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal(kind is not ("none" or "broken"), HasImage(pdf));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static bool HasImage(byte[] pdf) => Encoding.Latin1.GetString(pdf).Contains("/Subtype /Image");

    private static Dictionary<string, string> Values(GeneratedDocument doc) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(doc.ValuesJson!)!;

    private sealed record TestWorld(
        AppDbContext Db,
        StubUser User,
        DocumentTemplate Template,
        GeneratedLetterService Letters,
        OrganizationSignatureService Signatures,
        FakeAuditService LetterAudit)
    {
        public async Task<SignatureRef> AddSignature(string name, string position)
        {
            var result = await Signatures.AddAsync(OrganizationSignatureTests.Picture("png"), name, position, isDefault: false);
            Assert.True(result.Ok, result.Error);
            return new SignatureRef(result.Signature!.Id, name, position);
        }
    }

    // One company (org-1 by default) in a database shared by every World in
    // the test, so another company's rows are really there — and filtered.
    private TestWorld World(string orgId = "org-1", bool declarant = true)
    {
        var user = new StubUser { OrganizationId = orgId };
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, user);

        var empId = $"usr-emp-{orgId}";
        var employeeUserId = orgId == "org-1" ? "usr-emp" : empId;
        db.Organizations.Add(new Organization { Id = orgId, Name = $"Company {orgId}", Plan = OrgPlan.DIY, Tier = OrgPlanTier.PAID });
        db.Users.Add(new User { Id = employeeUserId, Name = "Siti Aminah", Email = $"siti@{orgId}.test" });
        if (!db.Users.Any(u => u.Id == "usr-admin"))
            db.Users.Add(new User { Id = "usr-admin", Name = "Hana Admin", Email = "admin@acme.test" });
        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = orgId, UserId = employeeUserId, Role = "Employee", JobTitle = "Engineer",
        });
        db.EmployeeProfiles.Add(new EmployeeProfile { OrganizationId = orgId, UserId = employeeUserId, IdNumber = "900101-01-1234" });
        db.PayrollCompanyInfos.Add(new PayrollCompanyInfo
        {
            OrganizationId = orgId,
            EmployerName = "Acme Sdn Bhd",
            RegistrationNo = "202001000001",
            DeclarantName = declarant ? "Hana" : null,
            DeclarantPosition = declarant ? "HR Manager" : null,
        });
        var template = new DocumentTemplate
        {
            OrganizationId = orgId, Name = "Notice",
            Body = "Dear {{employee.name}}, reg {{company.registrationNo}}. Ends {{input.lastDay}}.",
        };
        db.DocumentTemplates.Add(template);
        db.SaveChanges();

        var signatures = new OrganizationSignatureService(
            new OrganizationSignatureRepository(db),
            new OrganizationLogoStorage(new OrganizationLogoTests.TestEnvironment(_root)),
            new UserRepository(db),
            user,
            new FakeAuditService());

        var audit = new FakeAuditService();
        var profiles = new EmployeeProfileRepository(db);
        var memberships = new OrganizationMembershipRepository(db);
        var letters = new GeneratedLetterService(
            new DocumentTemplateRepository(db),
            new GeneratedDocumentRepository(db),
            new MemoryStorage(),
            memberships,
            profiles,
            new EmploymentPeriodRepository(db),
            new UserRepository(db),
            new PayrollCompanyInfoRepository(db),
            new OrganizationRepository(db),
            new EmployeeProfileService(profiles, memberships, null!, null!),
            new EmployeeScope(),
            new FullAccess(),
            user,
            audit,
            logos: null,
            signatures: signatures);

        return new TestWorld(db, user, template, letters, signatures, audit);
    }

    private sealed class StubUser : ICurrentUser
    {
        public string? UserId { get; set; } = "usr-admin";
        public string? OrganizationId { get; set; } = "org-1";
        public string? Role { get; set; } = "Admin";
        public string? Email { get; set; } = "admin@acme.test";
        public string? IpAddress { get; set; } = "127.0.0.1";
        public bool IsAdmin => true;
        public bool IsAuthenticated => true;
        public bool IsScopedMachine => false;
    }

    private sealed class FullAccess : IModuleAccessService
    {
        public Task<IReadOnlyCollection<string>> GetEnabledModulesAsync() =>
            Task.FromResult<IReadOnlyCollection<string>>(OrgModules.AllModules.ToList());
        public Task<IReadOnlyCollection<string>> GetOrgModulesAsync() => GetEnabledModulesAsync();
        public Task<ModuleLevel> GetModuleLevelAsync(string module) => Task.FromResult(ModuleLevel.Manage);
        public Task<ModuleLevel> GetCallerLevelAsync(string module) => GetModuleLevelAsync(module);
    }

    private sealed class MemoryStorage : IGeneratedDocumentStorage
    {
        private readonly Dictionary<string, byte[]> _files = [];

        public Task<string> StoreAsync(string organizationId, string employeeUserId, byte[] content)
        {
            var name = $"{Guid.NewGuid():N}.pdf";
            _files[$"{organizationId}/{employeeUserId}/{name}"] = content;
            return Task.FromResult(name);
        }

        public Task<byte[]?> ReadAsync(string organizationId, string employeeUserId, string storedFileName) =>
            Task.FromResult(_files.GetValueOrDefault($"{organizationId}/{employeeUserId}/{storedFileName}"));

        public void Delete(string organizationId, string employeeUserId, string storedFileName) =>
            _files.Remove($"{organizationId}/{employeeUserId}/{storedFileName}");
    }
}
