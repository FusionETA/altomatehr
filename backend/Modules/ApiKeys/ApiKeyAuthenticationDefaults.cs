namespace AltomateHR.Api.Modules.ApiKeys;

// Names used to register + recognise the wp_live_ authentication scheme.
public static class ApiKeyAuthenticationDefaults
{
    // The auth scheme the selector forwards wp_live_ requests to.
    public const string Scheme = "ApiKey";

    // Claim stamped on an api-key principal so downstream code (audit middleware,
    // [RequireScope]) can tell a machine caller apart from a JWT user.
    public const string ApiKeyIdClaim = "apikey_id";

    // Each granted scope is added as one claim of this type.
    public const string ScopeClaim = "apikey_scope";
}
