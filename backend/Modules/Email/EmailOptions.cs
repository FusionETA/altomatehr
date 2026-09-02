namespace AltomateHR.Api.Modules.Email;

public class EmailOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "AltomateHR";

    // The endpoint doesn't vary in practice, so this is override-only.
    public string BaseUrl { get; set; } = "https://api.enginemailer.com/RESTAPI/V2/Submission/SendEmail";

    // A blank key means "not configured" — never a valid empty credential.
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(FromEmail);
}
