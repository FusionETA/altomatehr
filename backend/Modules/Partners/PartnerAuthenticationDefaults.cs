namespace AltomateHR.Api.Modules.Partners;

// Names for the partner access-token (apx_live_) authentication scheme.
public static class PartnerAuthenticationDefaults
{
    // The scheme the "Smart" selector forwards apx_live_ requests to.
    public const string Scheme = "PartnerToken";

    // Marker claim: this principal is a partner app, not a human or a wp_live key.
    // Read by [RequireScope] and PartnerAccessFilter to apply the stricter,
    // deny-by-default posture partner tokens get.
    public const string ClientIdClaim = "partner_client";
}
