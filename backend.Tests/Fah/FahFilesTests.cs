using System.Buffers.Binary;
using System.Text;
using AltomateHR.Api.Modules.Fah;
using AltomateHR.Api.Modules.Fah.Entities;
using ClosedXML.Excel;

namespace AltomateHR.Api.Tests.Fah;

// What FAH reads from an attached file, and what it refuses at upload.
public class FahFilesTests
{
    private static byte[] Png(int width, int height)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), height);
        return b;
    }

    [Fact]
    public void AnImage_IsKnownByItsBytes_NotItsName()
    {
        var file = FahFiles.Prepare("photo.txt", Png(1200, 900)).File!;

        Assert.Equal(FahAttachmentKind.Image, file.Kind);
        Assert.Equal("image/png", file.MediaType);
    }

    [Fact]
    public void AnImageTooBigForTheModel_IsRefused()
    {
        var outcome = FahFiles.Prepare("scan.png", Png(9000, 1000));

        Assert.Null(outcome.File);
        Assert.Contains("9000×1000", outcome.Error);
    }

    [Fact]
    public void AJpegsSize_IsReadFromItsFrameHeader()
    {
        // SOI, an APP0 segment, then SOF0 with height 480 and width 640.
        byte[] jpeg =
        [
            0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0xE0, 0x02, 0x80, 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        ];

        Assert.Equal((640, 480), FahFiles.ImageSize(jpeg));
    }

    [Fact]
    public void AnImageWhoseHeaderIsCutShort_IsRefused()
    {
        Assert.Null(FahFiles.Prepare("broken.png", Png(10, 10)[..12]).File);
    }

    [Fact]
    public void APdf_CountsItsPages_AndRefusesAPasswordProtectedOne()
    {
        var pdf = Encoding.Latin1.GetBytes("%PDF-1.7\n1 0 obj <</Type /Pages /Count 2>>\n2 0 obj <</Type /Page>>\n3 0 obj <</Type/Page>>\n");
        Assert.Equal(2, FahFiles.Prepare("slip.pdf", pdf).File!.Pages);

        var locked = Encoding.Latin1.GetBytes("%PDF-1.7\ntrailer <</Encrypt 5 0 R>>\n");
        Assert.Contains("password-protected", FahFiles.Prepare("slip.pdf", locked).Error);
    }

    [Fact]
    public void APdfWithTooManyPages_IsRefused()
    {
        var pdf = Encoding.Latin1.GetBytes("%PDF-1.7\n" + string.Concat(Enumerable.Repeat("<</Type /Page>>\n", FahFiles.MaxPdfPages + 1)));

        Assert.Contains($"{FahFiles.MaxPdfPages + 1} pages", FahFiles.Prepare("big.pdf", pdf).Error);
    }

    [Fact]
    public void AWorkbook_IsReadSheetBySheet_AsCsvRows()
    {
        using var workbook = new XLWorkbook();
        var staff = workbook.AddWorksheet("Staff");
        staff.Cell(1, 1).Value = "Name";
        staff.Cell(1, 2).Value = "Salary";
        staff.Cell(2, 1).Value = "Tan, Mei Ling";
        staff.Cell(2, 2).Value = 4500;
        workbook.AddWorksheet("Empty");
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var file = FahFiles.Prepare("staff.xlsx", stream.ToArray()).File!;

        Assert.Equal(FahAttachmentKind.Text, file.Kind);
        Assert.Contains("## Sheet \"Staff\" (2 rows)", file.Text);
        Assert.Contains("\"Tan, Mei Ling\",4500", file.Text);
        Assert.DoesNotContain("Empty", file.Text);
        Assert.Null(file.Note);
    }

    [Fact]
    public void ALongCsv_IsCutAtAWholeRow_AndSaysSo()
    {
        var rows = Enumerable.Range(1, 5000).Select(i => $"Employee {i},90010101{i:D4},RM 3000");
        var csv = Encoding.UTF8.GetBytes("Name,IC,Salary\n" + string.Join("\n", rows));

        var file = FahFiles.Prepare("staff.csv", csv).File!;

        Assert.True(file.Text!.Length <= FahFiles.MaxTextChars);
        Assert.EndsWith("RM 3000", file.Text);
        Assert.Matches(@"Only the first [\d,]+ of 5,001 rows were read\.", file.Note);
    }

    [Fact]
    public void ACsvSavedByExcelInWindowsEncoding_StillReads()
    {
        var file = FahFiles.Prepare("staff.csv", Encoding.Latin1.GetBytes("Name\nJosé\n")).File!;

        Assert.Contains("José", file.Text);
    }

    [Theory]
    [InlineData("old.xls", "save it as .xlsx")]
    [InlineData("old.doc", "save it as .docx")]
    [InlineData("IMG_1.HEIC", "JPG or PNG")]
    [InlineData("notes.pages", "can't read notes.pages")]
    public void FilesFahCantRead_AreRefusedWithWhatToDo(string name, string expected)
    {
        Assert.Contains(expected, FahFiles.Prepare(name, [1, 2, 3, 4]).Error);
    }

    [Fact]
    public void AFileName_LosesItsPathAndBrackets()
    {
        Assert.Equal("Payslip (Oct).pdf", FahFiles.CleanName(@"C:\Users\me\Payslip [Oct].pdf"));
    }
}
