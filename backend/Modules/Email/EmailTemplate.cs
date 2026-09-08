namespace AltomateHR.Api.Modules.Email;

// The shell every outgoing email is wrapped in.
//
// Shared because the next email should inherit this rather than invent its own:
// the OTP is the only sender today, but digests and approval notices are coming.
//
// Written for email clients, not browsers — which is why it looks nothing like
// the app's CSS:
//   * Tables, not flex or grid. Outlook renders through Word's engine and
//     supports neither.
//   * Every style inline. Gmail strips <style> blocks in some contexts, so a
//     stylesheet would be a design that sometimes vanishes.
//   * No images. A logo would need a public URL, and blocked remote images are
//     the norm — a wordmark in text always renders.
//   * Hex, not the app's hsl() tokens. `var()` and hsl() are unreliable here.
public static class EmailTemplate
{
    // Straight from the app's palette so the mail matches the product.
    private const string Primary = "#4B1985";
    private const string PageBg = "#F7F5FA";
    private const string Ink = "#190D26";
    private const string Muted = "#726581";
    private const string Border = "#D3CADD";

    // A system font stack: no webfont, because @font-face is widely ignored and
    // a failed load falls back to something unintended.
    private const string Font =
        "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif";

    // `preheader` is the grey line the inbox shows next to the subject. Left
    // unset, clients scrape the first words of the body instead, which reads
    // like a fragment.
    public static string Wrap(string headline, string bodyHtml, string preheader = "")
    {
        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <meta name="color-scheme" content="light">
              <title>{Escape(headline)}</title>
            </head>
            <body style="margin:0;padding:0;background:{PageBg};font-family:{Font};">
              <div style="display:none;font-size:1px;color:{PageBg};line-height:1px;max-height:0;max-width:0;opacity:0;overflow:hidden;">{Escape(preheader)}</div>

              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:{PageBg};padding:24px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="width:100%;max-width:600px;">

                      <tr>
                        <td style="padding:0 4px 16px;font-family:{Font};font-size:18px;font-weight:bold;color:{Primary};letter-spacing:-0.2px;">
                          AltomateHR
                        </td>
                      </tr>

                      <tr>
                        <td style="background:#FFFFFF;border:1px solid {Border};border-radius:16px;padding:28px 24px;">
                          <h1 style="margin:0 0 16px;font-family:{Font};font-size:20px;line-height:1.3;font-weight:bold;color:{Ink};">
                            {Escape(headline)}
                          </h1>
                          {bodyHtml}
                        </td>
                      </tr>

                      <tr>
                        <td style="padding:16px 4px 0;font-family:{Font};font-size:12px;line-height:1.5;color:{Muted};">
                          Sent by AltomateHR. If this wasn't you, no action is needed.
                        </td>
                      </tr>

                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    // A one-time code, shown as the thing the reader is actually here for.
    //
    // Letter-spaced and monospaced so 0/O and 1/l can't be confused when
    // retyping, and selectable as plain text — an image of a code can't be
    // copied, and a code that can't be copied gets mistyped.
    public static string CodeBlock(string code) => $"""
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:4px 0 20px;">
          <tr>
            <td align="center" style="background:{PageBg};border:1px solid {Border};border-radius:12px;padding:18px 12px;font-family:'SF Mono',Menlo,Consolas,'Courier New',monospace;font-size:30px;font-weight:bold;letter-spacing:8px;color:{Primary};">
              {Escape(code)}
            </td>
          </tr>
        </table>
        """;

    public static string Paragraph(string html, bool muted = false) =>
        $"""<p style="margin:0 0 12px;font-family:{Font};font-size:15px;line-height:1.6;color:{(muted ? Muted : Ink)};">{html}</p>""";

    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value);
}
