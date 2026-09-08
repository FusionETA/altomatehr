using AltomateHR.Api.Modules.Email;

namespace AltomateHR.Api.Tests.Email;

// The shell has to survive email clients, not browsers, and the rules that make
// that true are invisible in the markup. These pin them, because the natural
// instinct when editing HTML is to reach for a stylesheet or a flex container —
// both of which silently break in Outlook or Gmail.
public class EmailTemplateTests
{
    private static string Render() =>
        EmailTemplate.Wrap(
            "Reset your password",
            EmailTemplate.Paragraph("Hello Zi Rong,")
            + EmailTemplate.CodeBlock("694137")
            + EmailTemplate.Paragraph("Expires in 10 minutes.", muted: true),
            preheader: "Your code is 694137.");

    [Fact]
    public void UsesTablesForLayout_NotFlexOrGrid()
    {
        var html = Render();

        Assert.Contains("role=\"presentation\"", html);
        // Outlook renders through Word's engine and supports neither.
        Assert.DoesNotContain("display:flex", html);
        Assert.DoesNotContain("display:grid", html);
    }

    [Fact]
    public void CarriesNoStyleBlock_BecauseGmailStripsThem()
    {
        // Every rule must be inline, or the design vanishes in the clients that
        // drop <style> — which is the worst kind of failure: it renders fine
        // wherever the author happens to test.
        Assert.DoesNotContain("<style", Render());
    }

    [Fact]
    public void RemoteImagesAreNotRelieduponForContent()
    {
        // Blocked remote images are the default in most clients, so a logo
        // image would leave the mail headerless. The wordmark is text.
        var html = Render();

        Assert.DoesNotContain("<img", html);
        Assert.Contains("AltomateHR", html);
    }

    [Fact]
    public void CodeIsSelectableText()
    {
        // An image of a code can't be copied, and a code that can't be copied
        // gets mistyped.
        Assert.Contains("694137", Render());
    }

    [Fact]
    public void SetsAPreheader_SoTheInboxDoesNotScrapeTheFirstWords()
    {
        Assert.Contains("Your code is 694137.", Render());
    }

    [Fact]
    public void EscapesTheHeadline()
    {
        var html = EmailTemplate.Wrap("<script>alert(1)</script>", "", preheader: "");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void WritesASampleForVisualInspection()
    {
        // Not an assertion — a way to open the real output in a browser without
        // sending mail to a real inbox to see a spacing change.
        var path = Environment.GetEnvironmentVariable("EMAIL_PREVIEW_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;

        File.WriteAllText(path, Render());
        Assert.True(File.Exists(path));
    }
}
