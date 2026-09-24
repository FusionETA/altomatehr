namespace AltomateHR.Api.Modules.ApiKeys;

// What this deployment can do, advertised by GET /whoami.
//
// Integrations probe this before sending an optional block, because their
// request schemas are strict: a field an older deployment does not know would
// 400 the entire call. Announcing capability here is what lets the two sides
// ship independently instead of coordinating release times.
//
// Add a name the moment the capability lands, never before.
public static class ApiFeatures
{
    // PUT /onboarding accepts the whole policy block in one call.
    public const string OnboardingBulk = "onboarding.bulk";

    // POST /sso/ticket mints an inbound hand-off for an admin of this org.
    public const string InboundSso = "sso.inbound";

    public static readonly IReadOnlyList<string> All = [OnboardingBulk, InboundSso];
}
