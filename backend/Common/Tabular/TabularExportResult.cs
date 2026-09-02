namespace AltomateHR.Api.Common.Tabular;

// A rendered download: the bytes plus everything the controller needs to hand
// them to the browser. Lives here so claims / attendance / leave all return the
// same shape and the controllers stay one-liners.
public sealed record TabularExportResult(byte[] Content, string FileName, string ContentType)
{
    public static TabularExportResult From(
        TabularSheet sheet, TabularFormat format, string baseFileName, TabularPdfHeader? pdfHeader = null) =>
        From([sheet], format, baseFileName, pdfHeader);

    public static TabularExportResult From(
        IReadOnlyList<TabularSheet> sheets,
        TabularFormat format,
        string baseFileName,
        TabularPdfHeader? pdfHeader = null) =>
        new(TabularWriter.Write(sheets, format, pdfHeader),
            $"{baseFileName}.{format.Extension()}",
            format.ContentType());
}
