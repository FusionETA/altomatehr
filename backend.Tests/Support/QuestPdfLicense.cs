using System.Runtime.CompilerServices;

namespace AltomateHR.Api.Tests.Support;

// QuestPDF refuses to render until a licence tier is declared. The API declares
// it in Program.cs, which unit tests never execute — so without this, any test
// that renders a PDF fails with a licence error rather than a real assertion.
//
// A module initializer rather than a fixture: it runs once, before any test in
// the assembly, so no test class has to remember to inherit anything.
internal static class QuestPdfLicense
{
    [ModuleInitializer]
    internal static void Configure()
    {
        // Same tier Program.cs declares. Community is free for organisations
        // under USD 1M annual revenue — confirm that still applies before
        // shipping to production.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }
}
