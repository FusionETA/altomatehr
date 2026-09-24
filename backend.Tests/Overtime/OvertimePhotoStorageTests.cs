using System.Text;
using AltomateHR.Api.Modules.Overtime;
using AltomateHR.Api.Modules.Xero;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AltomateHR.Api.Tests.Overtime;

// Where an overtime before/after photo's bytes end up.
//
// Xero Files when the org is connected, local disk otherwise — the last of the
// four modules on that path. No new columns: BeforePhotoUrl and AfterPhotoUrl
// each carry a Xero-hosted photo's file id inside the url.
public class OvertimePhotoStorageTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"overtime-photos-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private OvertimePhotoStorage Create(XeroUploadedFile? uploaded = null) =>
        new(new StubHostEnvironment { ContentRootPath = _root }, new StubUploader(uploaded));

    private static OvertimePhotoUpload Upload(
        string name = "after.jpg", string contentType = "image/jpeg") =>
        new(name, contentType, 3, new MemoryStream(Encoding.UTF8.GetBytes("jpg")));

    private string LocalDirectory => Path.Combine(_root, "storage", "overtime-photos");

    [Fact]
    public async Task StoresInXeroWhenTheOrgIsConnected()
    {
        var storage = Create(new XeroUploadedFile("file-1", "after.jpg"));

        var result = await storage.StoreAsync(Upload());

        Assert.Equal("/overtime/photos/xero/file-1", result.PhotoUrl);
        Assert.False(Directory.Exists(LocalDirectory));
    }

    // An overtime request cannot be approved without its after-work photo, so a
    // refused upload would strand the request. Disk keeps that from happening.
    [Fact]
    public async Task FallsBackToDiskWhenXeroDeclines()
    {
        var storage = Create();

        var result = await storage.StoreAsync(Upload());

        Assert.StartsWith("/overtime/photos/", result.PhotoUrl);
        Assert.DoesNotContain("/xero/", result.PhotoUrl);
        Assert.Single(Directory.GetFiles(LocalDirectory));
    }

    // Both urls keep the prefix the service validates a submitted photo against,
    // so a Xero-hosted one is not rejected as an invalid photo url.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothDestinationsProduceAnAcceptablePhotoUrl(bool connected)
    {
        var storage = Create(connected ? new XeroUploadedFile("file-1", "a.jpg") : null);

        var result = await storage.StoreAsync(Upload());

        Assert.StartsWith("/overtime/photos/", result.PhotoUrl);
    }

    [Fact]
    public async Task RefusesANonImageWithoutUploading()
    {
        var storage = Create(new XeroUploadedFile("file-1", "x.pdf"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.StoreAsync(Upload("x.pdf", "application/pdf")));

        Assert.False(Directory.Exists(LocalDirectory));
    }

    private sealed class StubUploader(XeroUploadedFile? uploaded) : IXeroFileUploader
    {
        public Task<XeroUploadedFile?> TryUploadFileAsync(
            string folderName, byte[] content, string fileName, string contentType)
        {
            Assert.Equal("Overtime Photos", folderName);
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
