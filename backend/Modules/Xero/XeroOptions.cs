namespace AltomateHR.Api.Modules.Xero;

public class XeroOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string Scopes { get; set; } = "offline_access accounting.settings accounting.transactions projects.read";
    public string SuccessRedirectUrl { get; set; } = "/settings?tab=xero&connected=1";
    public string FailureRedirectUrl { get; set; } = "/settings?tab=xero&connected=0";
}
