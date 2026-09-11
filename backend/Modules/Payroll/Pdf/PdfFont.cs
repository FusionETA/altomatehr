namespace AltomateHR.Api.Modules.Payroll.Pdf;

// One typeface for every generated document.
//
// These used to ask for "Helvetica", which exists on macOS and NOT on the
// Linux images CI and production run on. QuestPDF silently substitutes, the
// text metrics change, and the same document paginates differently depending
// on the machine that produced it — a statutory worksheet that fits two pages
// on a developer's laptop ran to three on the build runner, which is how this
// was found.
//
// Lato is embedded in QuestPDF itself, so it resolves identically everywhere
// with nothing to install. The point is not the typeface; it is that the
// output must not depend on the host.
public static class PdfFont
{
    public const string Family = "Lato";
}
