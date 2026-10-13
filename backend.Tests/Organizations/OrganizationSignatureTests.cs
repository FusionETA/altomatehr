using System.Reflection;
using System.Text;
using AltomateHR.Api.Common;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Dtos;
using AltomateHR.Api.Tests.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace AltomateHR.Api.Tests.Organizations;

// Saved signatures: what counts as an image (by its bytes), the size, pixel
// and count limits, the single company default, soft remove, that another
// company's signature is never found, and who may change them.
public class OrganizationSignatureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"signature-tests-{Guid.NewGuid():N}");
    private readonly string _dbName = $"signatures-{Guid.NewGuid()}";

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    // ─── Upload rules ────────────────────────────────────────────────────

    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("webp", "image/webp")]
    public async Task A_png_jpeg_or_webp_is_stored_and_listed(string format, string contentType)
    {
        var (service, db, audit) = Signatures();

        var result = await service.AddAsync(Picture(format), "  Tan Mei Ling ", " HR Manager ", isDefault: false);

        Assert.True(result.Ok);
        var dto = result.Signature!;
        Assert.Equal("Tan Mei Ling", dto.Name);
        Assert.Equal("HR Manager", dto.Position);
        Assert.Equal("Hana Admin", dto.CreatedByName);
        Assert.Equal(DateTimeKind.Utc, dto.CreatedAt.Kind);

        var row = db.OrganizationSignatures.Single();
        Assert.Equal("org-a", row.OrganizationId);
        Assert.Equal(contentType, row.ContentType);
        Assert.StartsWith("signature-", row.StoredFileName);
        Assert.Equal([row.StoredFileName], Files("org-a"));

        var image = await service.GetImageAsync(dto.Id);
        Assert.Equal(contentType, image!.ContentType);
        Assert.Equal(dto.Id, Assert.Single(await service.ListAsync()).Id);
        var entry = Assert.Single(audit.Written);
        Assert.Equal(AuditActions.SettingsOrgSignatureAdd, entry.Action);
        Assert.Equal(dto.Id, entry.TargetId);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("svg")]
    [InlineData("gif")]
    [InlineData("text")]
    [InlineData("too-big")]
    [InlineData("too-many-pixels")]
    [InlineData("truncated")]
    public async Task Bad_images_are_refused_with_a_reason_and_nothing_is_stored(string kind)
    {
        var (service, db, audit) = Signatures();
        var png = Picture("png");
        byte[]? bytes = kind switch
        {
            "none" => null,
            "svg" => Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"),
            "gif" => Encoding.ASCII.GetBytes("GIF89a\x01\0\x01\0\0\0\0;"),
            "text" => Encoding.UTF8.GetBytes("not an image, saved as signature.png"),
            // A real PNG, padded past 1 MB.
            "too-big" => [.. png, .. new byte[OrganizationSignatureService.MaxBytes]],
            // A PNG header claiming 4001 px tall — refused before decoding.
            "too-many-pixels" => PngHeader(100, 4001),
            // Cut inside the image data: the header still reads fine.
            "truncated" => OrganizationLogoTests.Picture("png", 800, 300) is var big ? big[..(big.Length * 2 / 3)] : null,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var result = await service.AddAsync(bytes, "Tan Mei Ling", "HR Manager", isDefault: false);

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Empty(db.OrganizationSignatures);
        Assert.Empty(Files("org-a"));
        Assert.Empty(audit.Written);
        if (kind == "too-big") Assert.Contains("1 MB", result.Error);
        if (kind == "too-many-pixels") Assert.Contains("4000", result.Error);
        if (kind is "svg" or "gif" or "text") Assert.Contains("PNG, JPG or WebP", result.Error);
    }

    [Theory]
    [InlineData(null, "HR Manager")]
    [InlineData("   ", "HR Manager")]
    [InlineData("Tan Mei Ling", null)]
    [InlineData("Tan Mei Ling", "")]
    [InlineData("long", "HR Manager")]
    [InlineData("Tan Mei Ling", "long")]
    public async Task A_name_and_position_are_required_and_kept_short(string? name, string? position)
    {
        var (service, db, _) = Signatures();
        var tooLong = new string('x', OrganizationSignatureService.MaxTextLength + 1);

        var result = await service.AddAsync(Picture("png"),
            name == "long" ? tooLong : name, position == "long" ? tooLong : position, isDefault: false);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
        Assert.Empty(db.OrganizationSignatures);
        Assert.Empty(Files("org-a"));
    }

    [Fact]
    public async Task A_company_keeps_at_most_ten_signatures()
    {
        var (service, _, _) = Signatures();
        for (var i = 0; i < OrganizationSignatureService.MaxSignatures; i++)
            Assert.True((await service.AddAsync(Picture("png"), $"Signer {i}", "Director", false)).Ok);

        var eleventh = await service.AddAsync(Picture("png"), "One more", "Director", false);

        Assert.False(eleventh.Ok);
        Assert.Equal("A company can keep up to 10 signatures. Remove one first.", eleventh.Error);
        Assert.Equal(10, Files("org-a").Length);

        // Removing one frees a place.
        await service.RemoveAsync((await service.ListAsync())[0].Id);
        Assert.True((await service.AddAsync(Picture("png"), "One more", "Director", false)).Ok);
    }

    // ─── The company default ─────────────────────────────────────────────

    [Fact]
    public async Task The_first_signature_becomes_the_default_and_later_ones_do_not()
    {
        var (service, _, _) = Signatures();

        var first = await service.AddAsync(Picture("png"), "Zara", "Director", isDefault: false);
        var second = await service.AddAsync(Picture("png"), "Aminah", "HR Manager", isDefault: false);

        Assert.True(first.Signature!.IsDefault);
        Assert.False(second.Signature!.IsDefault);
        // Listed default first, then by name.
        Assert.Equal(["Zara", "Aminah"], (await service.ListAsync()).Select(s => s.Name));
        Assert.Equal(first.Signature.Id, (await service.GetDefaultAsync())!.Id);
    }

    [Fact]
    public async Task Adding_one_as_the_default_takes_the_flag_from_the_old_default()
    {
        var (service, db, _) = Signatures();
        var first = await service.AddAsync(Picture("png"), "Zara", "Director", false);

        var second = await service.AddAsync(Picture("png"), "Aminah", "HR Manager", isDefault: true);

        Assert.True(second.Signature!.IsDefault);
        Assert.Equal(second.Signature.Id, db.OrganizationSignatures.AsNoTracking().Single(s => s.IsDefault).Id);
        Assert.False((await service.ListAsync()).Single(s => s.Id == first.Signature!.Id).IsDefault);
    }

    [Fact]
    public async Task Setting_a_default_clears_the_previous_one_and_unsetting_leaves_none()
    {
        var (service, db, audit) = Signatures();
        var a = (await service.AddAsync(Picture("png"), "Zara", "Director", false)).Signature!;
        var b = (await service.AddAsync(Picture("png"), "Aminah", "HR Manager", false)).Signature!;

        var updated = await service.UpdateAsync(b.Id, new UpdateSignatureDto { Name = "Aminah Yusof", Position = "Head of HR", IsDefault = true });

        Assert.True(updated.Ok);
        Assert.Equal("Aminah Yusof", updated.Signature!.Name);
        Assert.Equal("Head of HR", updated.Signature.Position);
        Assert.Equal(b.Id, db.OrganizationSignatures.AsNoTracking().Single(s => s.IsDefault).Id);

        var unset = await service.UpdateAsync(b.Id, new UpdateSignatureDto { Name = "Aminah Yusof", Position = "Head of HR", IsDefault = false });

        Assert.True(unset.Ok);
        Assert.Empty(db.OrganizationSignatures.AsNoTracking().Where(s => s.IsDefault));
        Assert.Null(await service.GetDefaultAsync());
        Assert.Equal(2, audit.Written.Count(e => e.Action == AuditActions.SettingsOrgSignatureUpdate));
        Assert.NotNull(a);
    }

    [Fact]
    public async Task Updating_refuses_a_blank_name_and_404s_an_unknown_id()
    {
        var (service, _, _) = Signatures();
        var a = (await service.AddAsync(Picture("png"), "Zara", "Director", false)).Signature!;

        var blank = await service.UpdateAsync(a.Id, new UpdateSignatureDto { Name = " ", Position = "Director" });
        var missing = await service.UpdateAsync("nope", new UpdateSignatureDto { Name = "X", Position = "Y" });

        Assert.False(blank.Ok);
        Assert.NotNull(blank.Error);
        Assert.False(missing.Ok);
        Assert.Null(missing.Error);
    }

    [Fact]
    public async Task However_the_default_moves_two_defaults_never_coexist()
    {
        var (service, db, _) = Signatures();
        var ids = new List<string>();
        for (var i = 0; i < 4; i++)
            ids.Add((await service.AddAsync(Picture("png"), $"Signer {i}", "Director", isDefault: true)).Signature!.Id);
        Assert.Equal(ids[^1], db.OrganizationSignatures.AsNoTracking().Single(s => s.IsDefault).Id);

        // Two admins, each on their own request, setting different defaults.
        var (adminA, _, _) = Signatures();
        var (adminB, _, _) = Signatures();
        await adminA.UpdateAsync(ids[0], new UpdateSignatureDto { Name = "Signer 0", Position = "Director", IsDefault = true });
        await adminB.UpdateAsync(ids[1], new UpdateSignatureDto { Name = "Signer 1", Position = "Director", IsDefault = true });
        // A stale "set default" for one that was already default re-asserts it.
        await adminA.UpdateAsync(ids[0], new UpdateSignatureDto { Name = "Signer 0", Position = "Director", IsDefault = true });

        Assert.Equal(ids[0], db.OrganizationSignatures.AsNoTracking().Single(s => s.IsDefault).Id);
        Assert.Equal(ids[0], (await adminB.GetDefaultAsync())!.Id);
    }

    [Fact]
    public async Task The_repository_moves_the_default_in_one_step_and_only_to_an_active_signature()
    {
        var (service, db, _) = Signatures();
        var a = (await service.AddAsync(Picture("png"), "Zara", "Director", false)).Signature!;
        var b = (await service.AddAsync(Picture("png"), "Aminah", "HR", false)).Signature!;
        var repository = new OrganizationSignatureRepository(db);

        Assert.True(await repository.MakeDefaultAsync(b.Id));
        Assert.Equal(b.Id, db.OrganizationSignatures.AsNoTracking().Single(s => s.IsDefault).Id);
        Assert.True(await repository.MakeDefaultAsync(a.Id));
        Assert.Equal(a.Id, db.OrganizationSignatures.AsNoTracking().Single(s => s.IsDefault).Id);

        // Removed meanwhile: never made the default again.
        await service.RemoveAsync(b.Id);
        Assert.False(await repository.MakeDefaultAsync(b.Id));
        Assert.False(await repository.ClearDefaultAsync(b.Id));
        Assert.False(db.OrganizationSignatures.AsNoTracking().Single(s => s.Id == b.Id).IsDefault);
    }

    [Fact]
    public async Task An_edit_that_lands_after_a_remove_is_404_and_the_signature_stays_removed()
    {
        var (setup, db, _) = Signatures();
        var s = (await setup.AddAsync(Picture("png"), "Zara", "Director", false)).Signature!;
        var (adminA, dbA, _) = Signatures();
        var (adminB, _, _) = Signatures();

        // Admin A has read the signature (the edit form)…
        var repositoryA = new OrganizationSignatureRepository(dbA);
        Assert.NotNull(await repositoryA.GetActiveByIdAsync(s.Id));
        // …Admin B removes it…
        Assert.True(await adminB.RemoveAsync(s.Id));
        // …and A's save arrives: refused, and nothing is brought back.
        Assert.False(await repositoryA.UpdateDetailsAsync(s.Id, "Zara Ahmad", "Director"));
        Assert.False(await repositoryA.MakeDefaultAsync(s.Id));
        var result = await adminA.UpdateAsync(s.Id, new UpdateSignatureDto { Name = "Zara Ahmad", Position = "Director", IsDefault = true });

        Assert.False(result.Ok);
        Assert.Null(result.Error);   // → 404
        var row = db.OrganizationSignatures.AsNoTracking().Single();
        Assert.NotNull(row.RemovedAt);
        Assert.False(row.IsDefault);
        Assert.Equal("Zara", row.Name);
        Assert.Empty(await adminA.ListAsync());
        Assert.Null(await adminA.GetImageAsync(s.Id));
        // A second remove at the same moment is a 404, not a second log entry.
        Assert.False(await adminA.RemoveAsync(s.Id));
    }

    // On MySQL each write must be ONE guarded statement. Pomelo translates them
    // here against no server at all: the connection open is suppressed and
    // each command captured instead of run.
    [Fact]
    public async Task On_mysql_each_write_is_a_single_guarded_update_of_only_its_columns()
    {
        var capture = new SqlCapture();
        var user = new StubUser { OrganizationId = "org-a" };
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseMySql("Server=127.0.0.1;Port=1;Database=none;User=none;Password=none",
                    new MySqlServerVersion(new Version(8, 0, 0)))
                .AddInterceptors(capture)
                .Options,
            user);
        var repository = new OrganizationSignatureRepository(db);

        await repository.MakeDefaultAsync("sig-1");
        await repository.UpdateDetailsAsync("sig-1", "Zara", "Director");
        await repository.SoftRemoveAsync("sig-1", DateTime.UtcNow);
        await repository.ClearDefaultAsync("sig-1");

        var updates = capture.Commands.Where(c => c.TrimStart().StartsWith("UPDATE", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, updates.Count);
        foreach (var sql in updates)
        {
            Assert.Contains("`RemovedAt` IS NULL", sql);       // only an active signature
            Assert.Contains("`OrganizationId` = ", sql);       // the tenant filter applies
        }
        // The default moves for every affected row in the same statement.
        Assert.Contains("`IsDefault` = `o`.`Id` = @", updates[0].Replace("(", "").Replace(")", ""));
        // An edit never writes RemovedAt or IsDefault; a remove never writes Name.
        Assert.DoesNotContain("`RemovedAt` = @", updates[1]);
        Assert.DoesNotContain("`IsDefault` =", updates[1].Split("WHERE")[0]);
        Assert.Contains("`RemovedAt` = @", updates[2].Split("WHERE")[0]);
        Assert.DoesNotContain("`Name`", updates[2].Split("WHERE")[0]);
    }

    // ─── Soft remove ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_removed_signature_is_hidden_its_image_gone_and_its_row_kept()
    {
        var (service, db, audit) = Signatures();
        var a = (await service.AddAsync(Picture("png"), "Zara", "Director", false)).Signature!;

        Assert.True(await service.RemoveAsync(a.Id));

        Assert.Empty(await service.ListAsync());
        Assert.Null(await service.GetImageAsync(a.Id));
        Assert.Null(await service.FindActiveAsync(a.Id));
        Assert.Null(await service.GetDefaultAsync());
        Assert.Empty(Files("org-a"));
        var row = db.OrganizationSignatures.AsNoTracking().Single();
        Assert.NotNull(row.RemovedAt);
        Assert.False(row.IsDefault);
        Assert.False((await service.UpdateAsync(a.Id, new UpdateSignatureDto { Name = "X", Position = "Y" })).Ok);
        Assert.False(await service.RemoveAsync(a.Id));   // already removed → not found
        Assert.Equal(AuditActions.SettingsOrgSignatureRemove, audit.Written.Last().Action);
    }

    [Fact]
    public async Task A_signature_whose_file_is_gone_or_damaged_has_no_image()
    {
        var (service, db, _) = Signatures();
        var a = (await service.AddAsync(Picture("png"), "Zara", "Director", false)).Signature!;
        var b = (await service.AddAsync(Picture("png"), "Aminah", "HR", false)).Signature!;
        var dir = Path.Combine(_root, "storage", "org-branding", "org-a");
        File.Delete(Path.Combine(dir, db.OrganizationSignatures.Single(s => s.Id == a.Id).StoredFileName));
        var bPath = Path.Combine(dir, db.OrganizationSignatures.Single(s => s.Id == b.Id).StoredFileName);
        File.WriteAllBytes(bPath, File.ReadAllBytes(bPath)[..60]);

        Assert.Null(await service.GetImageAsync(a.Id));
        Assert.Null(await service.GetImageAsync(b.Id));
    }

    // ─── Whose signatures ────────────────────────────────────────────────

    [Fact]
    public async Task Another_companys_signature_is_never_found()
    {
        var (serviceA, _, _) = Signatures("org-a");
        var mine = (await serviceA.AddAsync(Picture("png"), "Zara", "Director", false)).Signature!;
        var (serviceB, _, _) = Signatures("org-b");

        Assert.Empty(await serviceB.ListAsync());
        Assert.Null(await serviceB.GetImageAsync(mine.Id));
        Assert.Null(await serviceB.FindActiveAsync(mine.Id));
        Assert.Null(await serviceB.GetDefaultAsync());
        Assert.False((await serviceB.UpdateAsync(mine.Id, new UpdateSignatureDto { Name = "X", Position = "Y", IsDefault = true })).Ok);
        Assert.False(await serviceB.RemoveAsync(mine.Id));

        // org-a's own signature is untouched, and org-b's first is its own default.
        Assert.Equal("Zara", Assert.Single(await serviceA.ListAsync()).Name);
        Assert.True((await serviceB.AddAsync(Picture("png"), "Ben", "CEO", false)).Signature!.IsDefault);
        Assert.True(Assert.Single(await serviceA.ListAsync()).IsDefault);
    }

    // ─── Endpoints ───────────────────────────────────────────────────────

    [Fact]
    public async Task Post_returns_201_with_the_signature_and_400_with_an_error()
    {
        var (service, _, _) = Signatures();
        var controller = Controller(service);

        var created = await controller.Add(FormFile(Picture("png")), "Zara", "Director", "true");
        var badFlag = await controller.Add(FormFile(Picture("png")), "Zara", "Director", "yes");
        var svg = await controller.Add(FormFile(Encoding.UTF8.GetBytes("<svg/>")), "Zara", "Director", null);
        var noFile = await controller.Add(null, "Zara", "Director", null);

        var ok = Assert.IsType<ObjectResult>(created);
        Assert.Equal(201, ok.StatusCode);
        Assert.True(Assert.IsType<SignatureDto>(ok.Value).IsDefault);
        foreach (var refused in new[] { badFlag, svg, noFile })
        {
            var bad = Assert.IsType<BadRequestObjectResult>(refused);
            Assert.False(string.IsNullOrWhiteSpace(bad.Value!.GetType().GetProperty("error")!.GetValue(bad.Value) as string));
        }
    }

    [Fact]
    public async Task The_image_is_served_uncached_and_unknown_ids_are_404()
    {
        var (service, _, _) = Signatures();
        var png = Picture("png");
        var a = (await service.AddAsync(png, "Zara", "Director", false)).Signature!;
        var controller = Controller(service);

        var file = Assert.IsType<FileContentResult>(await controller.GetImage(a.Id));

        Assert.Equal(png, file.FileContents);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.IsType<NotFoundResult>(await controller.GetImage("nope"));
        Assert.IsType<NotFoundResult>(await controller.Update("nope", new UpdateSignatureDto { Name = "X", Position = "Y" }));
        Assert.IsType<NotFoundResult>(await controller.Remove("nope"));
        Assert.IsType<NoContentResult>(await controller.Remove(a.Id));
        Assert.IsType<NotFoundResult>(await controller.GetImage(a.Id));
    }

    // ─── Who may change them ─────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(OrganizationSignaturesController.List), "organizations:read", false)]
    [InlineData(nameof(OrganizationSignaturesController.GetImage), "organizations:read", false)]
    [InlineData(nameof(OrganizationSignaturesController.Add), "organizations:write", true)]
    [InlineData(nameof(OrganizationSignaturesController.Update), "organizations:write", true)]
    [InlineData(nameof(OrganizationSignaturesController.Remove), "organizations:write", true)]
    public void Signature_endpoints_are_gated_like_the_org_settings(string action, string scope, bool needsSettings)
    {
        var method = typeof(OrganizationSignaturesController).GetMethod(action)!;

        Assert.Equal("Admin,Owner", typeof(OrganizationSignaturesController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal("organizations/current/signatures",
            typeof(OrganizationSignaturesController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(scope, method.GetCustomAttribute<RequireScopeAttribute>()!.Scope);
        Assert.Equal(needsSettings, method.GetCustomAttribute<RequireSettingsAttribute>() is not null);
    }

    [Theory]
    [InlineData("POST", false)]
    [InlineData("PUT", false)]
    [InlineData("DELETE", false)]
    [InlineData("GET", true)]
    public async Task An_admin_without_change_settings_can_see_signatures_but_not_change_them(string method, bool allowed)
    {
        var access = new FixedAccess(new AdminAccess(true, null, CanChangeSettings: false, null));

        var result = await RunFilter(new RequireSettingsAttribute(), access, method);

        if (allowed) Assert.Null(result);
        else Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    internal static byte[] Picture(string format) => OrganizationLogoTests.Picture(format, 300, 100);

    private static byte[] PngHeader(int width, int height)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }
            .CopyTo(b, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), height);
        b[24] = 8; b[25] = 6;
        return b;
    }

    // One in-memory database per test, shared by every org's context so
    // tenant isolation is real (the query filter, not separate databases).
    private (OrganizationSignatureService, AppDbContext, FakeAuditService) Signatures(string orgId = "org-a")
    {
        var user = new StubUser { OrganizationId = orgId };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options, user);
        if (!db.Users.Any(u => u.Id == "usr-admin"))
        {
            db.Users.Add(new User { Id = "usr-admin", Name = "Hana Admin", Email = "admin@acme.test" });
            db.SaveChanges();
        }

        var audit = new FakeAuditService();
        var service = new OrganizationSignatureService(
            new OrganizationSignatureRepository(db),
            new OrganizationLogoStorage(new OrganizationLogoTests.TestEnvironment(_root)),
            new UserRepository(db),
            user,
            audit);
        return (service, db, audit);
    }

    private string[] Files(string orgId)
    {
        var dir = Path.Combine(_root, "storage", "org-branding", orgId);
        return Directory.Exists(dir) ? Directory.GetFiles(dir).Select(Path.GetFileName).ToArray()! : [];
    }

    private static IFormFile FormFile(byte[] bytes) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "signature.png");

    private static OrganizationSignaturesController Controller(IOrganizationSignatureService service) =>
        new(service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    private static async Task<IActionResult?> RunFilter(IAsyncActionFilter filter, IModuleAccessService access, string method)
    {
        var services = new ServiceCollection().AddSingleton(access).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Method = method;
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor { EndpointMetadata = [] });
        var context = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: new object());

        var reached = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            reached = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], new object()));
        });
        return reached ? null : context.Result;
    }

    private sealed class FixedAccess(AdminAccess access) : IModuleAccessService
    {
        public Task<IReadOnlyCollection<string>> GetEnabledModulesAsync() =>
            Task.FromResult<IReadOnlyCollection<string>>(OrgModules.AllModules.ToList());
        public Task<IReadOnlyCollection<string>> GetOrgModulesAsync() => GetEnabledModulesAsync();
        public Task<AdminAccess> GetAccessAsync() => Task.FromResult(access);
        public Task<ModuleLevel> GetModuleLevelAsync(string module) => Task.FromResult(access.LevelFor(module));
    }

    // Suppresses opening the connection and captures each command, answering
    // an UPDATE with "1 row" and a query with a single true.
    private sealed class SqlCapture : DbConnectionInterceptor, IDbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            System.Data.Common.DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());

        public override InterceptionResult ConnectionOpening(
            System.Data.Common.DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            InterceptionResult.Suppress();

        public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
        }

        public ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData,
            InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            var table = new System.Data.DataTable();
            table.Columns.Add("value", typeof(bool));
            table.Rows.Add(true);
            return ValueTask.FromResult(InterceptionResult<System.Data.Common.DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
    }

    private sealed class StubUser : ICurrentUser
    {
        public string? UserId { get; set; } = "usr-admin";
        public string? OrganizationId { get; set; }
        public string? Role { get; set; } = "Admin";
        public string? Email { get; set; } = "admin@acme.test";
        public string? IpAddress { get; set; } = "127.0.0.1";
        public bool IsAdmin => true;
        public bool IsAuthenticated => true;
        public bool IsScopedMachine => false;
    }
}
