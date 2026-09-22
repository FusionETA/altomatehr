namespace AltomateHR.Api.Modules.Email;

// Where the employee portal lives, for links inside emails.
//
// Not derivable from the request: the API and the frontend are separate
// origins, so the host that served the API call is not the host an employee
// signs in at. Configured rather than guessed — a welcome email pointing at
// the API would be worse than one with no link at all.
public class PortalOptions
{
    public const string SectionName = "Portal";

    // The dev frontend. Override per environment in appsettings.{Env}.json or
    // an env var; a wrong value makes the welcome email's button dead, so it
    // is worth checking on first deploy.
    public string BaseUrl { get; set; } = "http://localhost:5173";

    public string LoginUrl => $"{BaseUrl.TrimEnd('/')}/login";
}
