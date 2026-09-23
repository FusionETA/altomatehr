namespace AltomateHR.Api.Modules.Xero;

public class XeroOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string Scopes { get; set; } = "offline_access accounting.settings accounting.transactions projects.read files";
    // Where the OAuth callback sends the browser when there is no stored return
    // URL. The outcome marker (?xero=connected / ?xero=failed) is appended by
    // XeroService, so these stay plain — the frontend reads that marker to open
    // the Xero card and say what happened.
    public string SuccessRedirectUrl { get; set; } = "/";
    public string FailureRedirectUrl { get; set; } = "/";
}
