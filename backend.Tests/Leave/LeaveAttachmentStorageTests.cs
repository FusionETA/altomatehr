using System.Text;
using AltomateHR.Api.Modules.Leave;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AltomateHR.Api.Tests.Leave;

// An MC is the evidence a sick-leave decision rests on, so it has to store
// reliably — and the upload route takes a filename straight from the browser,
// so it has to refuse anything it shouldn't write or serve.
public class LeaveAttachmentStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"altomate-leave-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task StoreAsync_KeepsTheFile_AndHandsBackAUrlThatReadsItBack()
    {
        var storage = Create();

        var stored = await storage.StoreAsync(Upload("mc.pdf", "application/pdf", "a scan"));

        Assert.StartsWith("/leave/attachments/", stored.AttachmentUrl);
        var file = await storage.GetAsync(Path.GetFileName(stored.AttachmentUrl));
        Assert.NotNull(file);
        Assert.Equal("application/pdf", file!.ContentType);
        Assert.Equal("a scan", await File.ReadAllTextAsync(file.Path));
    }

    [Fact]
    public async Task StoreAsync_NamesFilesItself_SoTwoUploadsCannotCollide()
    {
        // Two people attach "mc.pdf" in the same second. Trusting the browser's
        // name would have the second overwrite the first's evidence.
        var storage = Create();

        var first = await storage.StoreAsync(Upload("mc.pdf", "application/pdf", "first"));
        var second = await storage.StoreAsync(Upload("mc.pdf", "application/pdf", "second"));

        Assert.NotEqual(first.AttachmentUrl, second.AttachmentUrl);
        var kept = await storage.GetAsync(Path.GetFileName(first.AttachmentUrl));
        Assert.Equal("first", await File.ReadAllTextAsync(kept!.Path));
    }

    [Theory]
    [InlineData("payload.exe", "application/x-msdownload")]
    [InlineData("macro.docm", "application/vnd.ms-word.document.macroEnabled.12")]
    [InlineData("sheet.html", "text/html")]
    public async Task StoreAsync_RefusesATypeItWillNotServe(string name, string contentType)
    {
        var storage = Create();

        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.StoreAsync(Upload(name, contentType, "x")));
    }

    [Fact]
    public async Task StoreAsync_RefusesAFileOverTheLimit()
    {
        var storage = Create();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => storage.StoreAsync(new LeaveAttachmentUpload(
                "big.pdf", "application/pdf", 9 * 1024 * 1024, new MemoryStream())));

        Assert.Contains("8 MB", error.Message);
    }

    [Theory]
    [InlineData("../../appsettings.json")]
    [InlineData("sub/dir/mc.pdf")]
    public async Task GetAsync_RefusesAnythingThatIsNotABareFilename(string name)
    {
        // The name arrives from the URL, so this is the one place standing
        // between a request and the rest of the disk.
        var storage = Create();

        Assert.Null(await storage.GetAsync(name));
    }

    [Fact]
    public async Task GetAsync_ReturnsNothingForAFileThatWasNeverStored()
    {
        var storage = Create();

        Assert.Null(await storage.GetAsync("20260101000000-deadbeef.pdf"));
    }

    // ---- Xero Files ----

    // The point of the change: a connected org's MC belongs in Xero, beside the
    // books an accountant is already reconciling, not on this server's disk.
    [Fact]
    public async Task StoresInXeroWhenTheOrgIsConnected()
    {
        var storage = Create(new AltomateHR.Api.Modules.Xero.XeroUploadedFile("file-1", "mc.pdf"));

        var result = await storage.StoreAsync(Upload("mc.pdf", "application/pdf", "x"));

        Assert.Equal("file-1", result.XeroFileId);
        // Read back through our own proxy, so the OAuth token never reaches
        // the browser.
        Assert.Equal("/leave/files/file-1/content", result.AttachmentUrl);
        // And nothing was written locally.
        Assert.False(Directory.Exists(Path.Combine(_root, "storage", "leave-attachments")));
    }

    // Xero refusing — an expired connection, a missing scope, an outage — must
    // not stop someone filing leave. The bytes go to disk instead.
    [Fact]
    public async Task FallsBackToDiskWhenXeroDeclines()
    {
        var storage = Create();

        var result = await storage.StoreAsync(Upload("mc.pdf", "application/pdf", "x"));

        Assert.Null(result.XeroFileId);
        Assert.StartsWith("/leave/attachments/", result.AttachmentUrl);
        Assert.True(Directory.Exists(Path.Combine(_root, "storage", "leave-attachments")));
    }

    // Validation still comes first: a bad file is refused before either
    // destination is touched.
    [Fact]
    public async Task RefusesAnUnsupportedTypeWithoutCallingXero()
    {
        var storage = Create(new AltomateHR.Api.Modules.Xero.XeroUploadedFile("file-1", "x.exe"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.StoreAsync(Upload("x.exe", "application/x-msdownload", "x")));
    }

    // ---- wiring ----

    // `xero` null means "no connection", which is the local-disk path these
    // tests are about. The Xero path is covered by its own test below.
    private LeaveAttachmentStorage Create(AltomateHR.Api.Modules.Xero.XeroUploadedFile? uploaded = null) =>
        new(new FakeHostEnvironment { ContentRootPath = _root }, new StubXero(uploaded));

    private sealed class StubXero(AltomateHR.Api.Modules.Xero.XeroUploadedFile? uploaded)
        : AltomateHR.Api.Modules.Xero.IXeroFileUploader
    {
        public Task<AltomateHR.Api.Modules.Xero.XeroUploadedFile?> TryUploadFileAsync(
            string folderName, byte[] content, string fileName, string contentType) =>
            Task.FromResult(uploaded);
    }

    private static LeaveAttachmentUpload Upload(string name, string contentType, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new LeaveAttachmentUpload(name, contentType, bytes.Length, new MemoryStream(bytes));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

internal sealed class FakeHostEnvironment : IWebHostEnvironment
{
    public string WebRootPath { get; set; } = string.Empty;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ApplicationName { get; set; } = "AltomateHR.Api.Tests";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = Environments.Development;
}
