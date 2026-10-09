using System.Reflection;
using System.Security.Claims;
using System.Text;
using AltomateHR.Api.Common;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Documents;
using AltomateHR.Api.Modules.Documents.Pdf;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Tests.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Tests.Organizations;

// The company logo: what counts as an image (by its bytes), the size and pixel
// limits, replace/remove on disk, who may change it, that it is always the
// caller's own company's, and that letters print with it — or without it when
// it is missing or broken, never failing.
public class OrganizationLogoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"logo-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    // ─── What the bytes are ──────────────────────────────────────────────

    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("webp", "image/webp")]
    public void Real_png_jpeg_and_webp_are_recognised_with_their_size(string format, string contentType)
    {
        var bytes = Picture(format, width: 120, height: 60);

        var info = LogoImage.Read(bytes);

        Assert.NotNull(info);
        Assert.Equal(contentType, info.ContentType);
        Assert.Equal(120, info.Width);
        Assert.Equal(60, info.Height);
        Assert.True(LogoImage.CanDecode(bytes));
    }

    [Fact]
    public void Each_webp_header_kind_gives_its_size()
    {
        Assert.Equal((300, 200), Size(WebPHeader("VP8 ", 300, 200)));
        Assert.Equal((300, 200), Size(WebPHeader("VP8L", 300, 200)));
        Assert.Equal((5000, 4001), Size(WebPHeader("VP8X", 5000, 4001)));

        static (int, int) Size(byte[] b) => LogoImage.Read(b) is { } i ? (i.Width, i.Height) : (0, 0);
    }

    [Fact]
    public void Svg_gif_text_and_a_renamed_file_are_not_images()
    {
        Assert.Null(LogoImage.Read(Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")));
        Assert.Null(LogoImage.Read(Encoding.ASCII.GetBytes("GIF89a\x01\0\x01\0\0\0\0;")));
        Assert.Null(LogoImage.Read(Encoding.UTF8.GetBytes("just some text, saved as logo.png")));
        Assert.Null(LogoImage.Read([]));
        // A JPEG that ends before any frame header has no size to read.
        Assert.Null(LogoImage.Read([0xFF, 0xD8, 0xFF, 0xD9]));
    }

    // ─── Upload rules ────────────────────────────────────────────────────

    [Fact]
    public async Task A_png_is_stored_and_the_org_reports_it()
    {
        var (service, db, audit) = Logos();
        var png = Picture("png", 200, 80);

        var result = await service.SetAsync("org-a", png);

        Assert.True(result.Ok);
        Assert.True(result.Organization!.HasLogo);
        Assert.Equal(DateTimeKind.Utc, result.Organization.LogoUpdatedAt!.Value.Kind);
        var org = db.Organizations.Single(o => o.Id == "org-a");
        Assert.Equal("image/png", org.LogoContentType);
        Assert.EndsWith(".png", org.LogoFileName);
        Assert.Single(Files("org-a"));
        Assert.Equal(png, (await service.GetAsync("org-a"))!.Content);
        Assert.Equal(AuditActions.SettingsOrgLogoUpdate, Assert.Single(audit.Written).Action);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("svg")]
    [InlineData("gif")]
    [InlineData("text")]
    [InlineData("too-big")]
    [InlineData("too-many-pixels")]
    [InlineData("damaged")]
    public async Task Bad_uploads_are_refused_with_a_reason_and_nothing_is_stored(string kind)
    {
        var (service, db, audit) = Logos();
        var png = Picture("png", 50, 50);
        byte[]? bytes = kind switch
        {
            "none" => null,
            "svg" => Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"/>"),
            "gif" => Encoding.ASCII.GetBytes("GIF89a\x01\0\x01\0\0\0\0;"),
            "text" => Encoding.UTF8.GetBytes("not an image at all"),
            // A real PNG, padded past 2 MB.
            "too-big" => [.. png, .. new byte[OrganizationLogoService.MaxBytes]],
            // A PNG header claiming 5000 px wide — refused before decoding.
            "too-many-pixels" => PngHeader(5000, 100),
            // The right header, the rest cut off.
            "damaged" => png[..40],
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var result = await service.SetAsync("org-a", bytes);

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Null(db.Organizations.Single(o => o.Id == "org-a").LogoFileName);
        Assert.Empty(Files("org-a"));
        Assert.Empty(audit.Written);
        if (kind == "too-many-pixels") Assert.Contains("4000", result.Error);
        if (kind == "too-big") Assert.Contains("2 MB", result.Error);
    }

    [Fact]
    public async Task Replacing_the_logo_deletes_the_old_file()
    {
        var (service, db, _) = Logos();
        await service.SetAsync("org-a", Picture("png", 60, 30));
        var first = db.Organizations.Single(o => o.Id == "org-a").LogoFileName!;

        var jpeg = Picture("jpeg", 90, 30);
        var result = await service.SetAsync("org-a", jpeg);

        Assert.True(result.Ok);
        var second = db.Organizations.Single(o => o.Id == "org-a").LogoFileName!;
        Assert.NotEqual(first, second);
        Assert.Equal([second], Files("org-a"));
        var served = await service.GetAsync("org-a");
        Assert.Equal("image/jpeg", served!.ContentType);
        Assert.Equal(jpeg, served.Content);
    }

    [Fact]
    public async Task Removing_clears_the_logo_and_its_file_and_removing_none_is_fine()
    {
        var (service, db, audit) = Logos();
        await service.SetAsync("org-a", Picture("webp", 60, 30));

        await service.RemoveAsync("org-a");
        await service.RemoveAsync("org-a");   // nothing left — still fine, no second entry

        var org = db.Organizations.Single(o => o.Id == "org-a");
        Assert.Null(org.LogoFileName);
        Assert.Null(org.LogoContentType);
        Assert.Empty(Files("org-a"));
        Assert.Null(await service.GetAsync("org-a"));
        Assert.Equal(
            [AuditActions.SettingsOrgLogoUpdate, AuditActions.SettingsOrgLogoRemove],
            audit.Written.Select(a => a.Action));
    }

    [Fact]
    public async Task A_logo_file_gone_from_disk_reads_as_no_logo()
    {
        var (service, _, _) = Logos();
        await service.SetAsync("org-a", Picture("png", 60, 30));
        foreach (var f in Directory.GetFiles(Path.Combine(_root, "storage", "org-branding", "org-a"))) File.Delete(f);

        Assert.Null(await service.GetAsync("org-a"));
    }

    // ─── Whose logo ──────────────────────────────────────────────────────

    [Fact]
    public async Task The_endpoint_only_ever_serves_the_callers_own_company()
    {
        var (service, _, _) = Logos();
        var mine = Picture("png", 40, 40);
        await service.SetAsync("org-a", mine);
        await service.SetAsync("org-b", Picture("jpeg", 80, 40));

        var asA = await Controller("org-a").GetLogo(service);
        var file = Assert.IsType<FileContentResult>(asA);
        Assert.Equal(mine, file.FileContents);
        Assert.Equal("image/png", file.ContentType);

        // org-c has no logo: a 404, never someone else's.
        Assert.IsType<NotFoundResult>(await Controller("org-c").GetLogo(service));
    }

    [Fact]
    public async Task The_logo_is_served_uncached()
    {
        var (service, _, _) = Logos();
        await service.SetAsync("org-a", Picture("png", 40, 40));
        var controller = Controller("org-a");

        await controller.GetLogo(service);

        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    // ─── Who may change it ───────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(OrganizationsController.SetLogo), "organizations:write", true)]
    [InlineData(nameof(OrganizationsController.RemoveLogo), "organizations:write", true)]
    [InlineData(nameof(OrganizationsController.GetLogo), "organizations:read", false)]
    public void Logo_endpoints_are_gated_like_the_org_settings(string action, string scope, bool needsSettings)
    {
        var method = typeof(OrganizationsController).GetMethod(action)!;

        Assert.Equal("Admin,Owner", method.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal(scope, method.GetCustomAttribute<RequireScopeAttribute>()!.Scope);
        Assert.Equal(needsSettings, method.GetCustomAttribute<RequireSettingsAttribute>() is not null);
    }

    [Theory]
    [InlineData("PUT", false)]
    [InlineData("DELETE", false)]
    [InlineData("GET", true)]
    public async Task An_admin_without_change_settings_can_see_the_logo_but_not_change_it(string method, bool allowed)
    {
        var access = new FixedAccess(new AdminAccess(true, null, CanChangeSettings: false, null));

        var result = await RunFilter(new RequireSettingsAttribute(), access, method);

        if (allowed) Assert.Null(result);
        else Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    // ─── Letters ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("none")]
    [InlineData("png")]
    [InlineData("jpeg")]
    [InlineData("webp")]
    [InlineData("tall")]
    [InlineData("broken")]
    public void The_letter_renders_with_without_and_despite_a_broken_logo(string logo)
    {
        byte[]? bytes = logo switch
        {
            "none" => null,
            "tall" => Picture("png", 40, 400),
            "broken" => [.. Picture("png", 60, 30)[..40]],
            _ => Picture(logo, 300, 100),
        };

        var pdf = LetterPdf.Render(Letter(bytes));

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void A_letter_with_a_logo_embeds_an_image_and_one_without_does_not()
    {
        var with = LetterPdf.Render(Letter(Picture("png", 300, 100)));
        var without = LetterPdf.Render(Letter(null));
        var broken = LetterPdf.Render(Letter(Picture("png", 300, 100)[..40]));

        Assert.Contains("/Subtype /Image", Encoding.Latin1.GetString(with));
        Assert.DoesNotContain("/Subtype /Image", Encoding.Latin1.GetString(without));
        Assert.DoesNotContain("/Subtype /Image", Encoding.Latin1.GetString(broken));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    internal static LetterPdfModel Letter(byte[]? logo) => new(
        Title: "Notice",
        CompanyName: "Acme Sdn Bhd",
        RegistrationNo: "202001000001",
        CompanyAddressLine: "1 Jalan Ampang, 50450 Kuala Lumpur",
        CompanyPhone: "03-1234 5678",
        CompanyEmail: "hr@acme.test",
        DateText: "9 October 2026",
        Blocks: LetterMarkup.Parse("# Notice\n\nDear Siti,\n\nHello.", _ => ""),
        SignatoryName: "Hana",
        SignatoryPosition: "HR Manager",
        Logo: logo);

    // A real image from the same engine that prints letters: a page rendered
    // at 72 dpi, so 1 pt = 1 px.
    internal static byte[] Picture(string format, float width, float height) =>
        Document.Create(d => d.Page(p =>
            {
                p.Size(width, height);
                p.Margin(0);
                p.Content().Background("#1d4ed8");
            }))
            .GenerateImages(new ImageGenerationSettings
            {
                ImageFormat = format switch
                {
                    "png" => ImageFormat.Png,
                    "jpeg" => ImageFormat.Jpeg,
                    "webp" => ImageFormat.Webp,
                    _ => throw new ArgumentOutOfRangeException(nameof(format)),
                },
                RasterDpi = 72,
            })
            .First();

    // Signature + IHDR only — enough for the size check, not for a decoder.
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

    private static byte[] WebPHeader(string chunk, int width, int height)
    {
        var b = new byte[40];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(b, 0);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(b, 8);
        Encoding.ASCII.GetBytes(chunk).CopyTo(b, 12);
        switch (chunk)
        {
            case "VP8 ":
                b[23] = 0x9D; b[24] = 0x01; b[25] = 0x2A;
                b[26] = (byte)width; b[27] = (byte)(width >> 8);
                b[28] = (byte)height; b[29] = (byte)(height >> 8);
                break;
            case "VP8L":
                b[20] = 0x2F;
                var bits = (uint)(width - 1) | (uint)(height - 1) << 14;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(21), bits);
                break;
            case "VP8X":
                var w = width - 1; var h = height - 1;
                b[24] = (byte)w; b[25] = (byte)(w >> 8); b[26] = (byte)(w >> 16);
                b[27] = (byte)h; b[28] = (byte)(h >> 8); b[29] = (byte)(h >> 16);
                break;
        }
        return b;
    }

    private (OrganizationLogoService, AppDbContext, FakeAuditService) Logos()
    {
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"logo-{Guid.NewGuid()}").Options,
            new StubUser());
        db.Organizations.AddRange(
            new Organization { Id = "org-a", Name = "Acme" },
            new Organization { Id = "org-b", Name = "Beta" },
            new Organization { Id = "org-c", Name = "Gamma" });
        db.SaveChanges();

        var audit = new FakeAuditService();
        var service = new OrganizationLogoService(
            new OrganizationRepository(db), new OrganizationLogoStorage(new TestEnvironment(_root)), audit);
        return (service, db, audit);
    }

    private string[] Files(string orgId)
    {
        var dir = Path.Combine(_root, "storage", "org-branding", orgId);
        return Directory.Exists(dir) ? Directory.GetFiles(dir).Select(Path.GetFileName).ToArray()! : [];
    }

    private static OrganizationsController Controller(string orgId)
    {
        var controller = new OrganizationsController(null!, new StubUser { OrganizationId = orgId });
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("org", orgId)], "test")),
            },
        };
        return controller;
    }

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

    internal sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "AltomateHR.Api.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Testing";
    }
}
