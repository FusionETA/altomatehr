using System.Text;
using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Xero;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AltomateHR.Api.Tests.Attendance;

// Where a clock-in/out photo's bytes end up.
//
// Xero Files when the org is connected, local disk otherwise — the third
// module on that path, after leave attachments and claim receipts. No new
// columns: the four existing photo-url fields carry a Xero-hosted photo's file
// id inside the url itself.
public class AttendancePhotoStorageTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"attendance-photos-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private AttendancePhotoStorage Create(XeroUploadedFile? uploaded = null) =>
        new(new StubHostEnvironment { ContentRootPath = _root }, new StubXero(uploaded));

    private static AttendancePhotoUpload Upload(
        string name = "selfie.jpg", string contentType = "image/jpeg") =>
        new(name, contentType, 3, new MemoryStream(Encoding.UTF8.GetBytes("jpg")));

    private string LocalDirectory => Path.Combine(_root, "storage", "attendance-photos");

    [Fact]
    public async Task StoresInXeroWhenTheOrgIsConnected()
    {
        var storage = Create(new XeroUploadedFile("file-1", "selfie.jpg"));

        var result = await storage.StoreAsync(Upload());

        // The url carries the file id, which is why this needed no new column.
        Assert.Equal("/attendance/photos/xero/file-1", result.PhotoUrl);
        Assert.False(Directory.Exists(LocalDirectory));
    }

    // Clocking in has to work for an org that never connected Xero, and on the
    // day Xero is down. A refused upload must not block someone starting work.
    [Fact]
    public async Task FallsBackToDiskWhenXeroDeclines()
    {
        var storage = Create();

        var result = await storage.StoreAsync(Upload());

        Assert.StartsWith("/attendance/photos/", result.PhotoUrl);
        Assert.DoesNotContain("/xero/", result.PhotoUrl);
        Assert.Single(Directory.GetFiles(LocalDirectory));
    }

    // Both urls share the prefix the serving routes are built on, so a photo is
    // reachable whichever way it was stored.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothDestinationsProduceAServeableUrl(bool connected)
    {
        var storage = Create(connected ? new XeroUploadedFile("file-1", "s.jpg") : null);

        var result = await storage.StoreAsync(Upload());

        Assert.StartsWith("/attendance/photos/", result.PhotoUrl);
    }

    // A clock photo is an image — never a PDF, unlike a claim receipt — and
    // that check runs before either destination is touched.
    [Fact]
    public async Task RefusesANonImageWithoutUploading()
    {
        var storage = Create(new XeroUploadedFile("file-1", "x.pdf"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => storage.StoreAsync(Upload("x.pdf", "application/pdf")));

        Assert.False(Directory.Exists(LocalDirectory));
    }

    private sealed class StubXero(XeroUploadedFile? uploaded) : IXeroFileUploader
    {
        public Task<XeroUploadedFile?> TryUploadFileAsync(
            string folderName, byte[] content, string fileName, string contentType)
        {
            Assert.Equal("Attendance Photos", folderName);
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
