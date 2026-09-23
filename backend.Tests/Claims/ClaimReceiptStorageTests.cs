using System.Text;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Xero;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AltomateHR.Api.Tests.Claims;

// Where a claim receipt's bytes end up.
//
// Xero Files when the org is connected, local disk otherwise. Getting the
// order wrong is not cosmetic: a receipt on this server's disk is outside the
// books it justifies, and it disappears on a redeploy without a mounted volume.
public class ClaimReceiptStorageTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"claim-receipts-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ClaimReceiptStorage Create(XeroUploadedFile? uploaded = null) =>
        new(new StubHostEnvironment { ContentRootPath = _root }, new StubXero(uploaded));

    private static ClaimReceiptUpload Upload(
        string name = "receipt.pdf", string contentType = "application/pdf") =>
        new(name, contentType, 3, new MemoryStream(Encoding.UTF8.GetBytes("pdf")));

    private string LocalDirectory => Path.Combine(_root, "storage", "receipts");

    [Fact]
    public async Task StoresInXeroWhenTheOrgIsConnected()
    {
        var storage = Create(new XeroUploadedFile("file-1", "receipt.pdf"));

        var result = await storage.StoreAsync(Upload());

        Assert.Equal("file-1", result.XeroFileId);
        // Read back through our own proxy, so the OAuth token never reaches
        // the browser.
        Assert.Equal("/claims/receipts/xero/file-1", result.ReceiptUrl);
        Assert.False(Directory.Exists(LocalDirectory));
    }

    // Xero refusing — expired connection, missing scope, outage — must not stop
    // someone filing a claim.
    [Fact]
    public async Task FallsBackToDiskWhenXeroDeclines()
    {
        var storage = Create();

        var result = await storage.StoreAsync(Upload());

        Assert.Null(result.XeroFileId);
        Assert.StartsWith("/claims/receipts/", result.ReceiptUrl);
        Assert.DoesNotContain("/xero/", result.ReceiptUrl);
        Assert.Single(Directory.GetFiles(LocalDirectory));
    }

    // Both urls live under /claims/receipts/, which is what the claim-save
    // validation requires — a Xero receipt must not be rejected as an invalid
    // document url.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothDestinationsProduceAnAcceptableReceiptUrl(bool connected)
    {
        var storage = Create(connected ? new XeroUploadedFile("file-1", "r.pdf") : null);

        var result = await storage.StoreAsync(Upload());

        Assert.StartsWith("/claims/receipts/", result.ReceiptUrl);
    }

    // Validation comes first: a file neither destination should accept is
    // refused before either is touched.
    [Fact]
    public async Task RefusesAnUnsupportedTypeWithoutUploading()
    {
        var storage = Create(new XeroUploadedFile("file-1", "x.exe"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.StoreAsync(Upload("x.exe", "application/x-msdownload")));

        Assert.False(Directory.Exists(LocalDirectory));
    }

    private sealed class StubXero(XeroUploadedFile? uploaded) : IXeroFileUploader
    {
        public Task<XeroUploadedFile?> TryUploadFileAsync(
            string folderName, byte[] content, string fileName, string contentType)
        {
            // The folder is what an accountant sees this grouped under.
            Assert.Equal("Claims", folderName);
            return Task.FromResult(uploaded);
        }
    }

    private sealed class StubHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Development;
    }
}
