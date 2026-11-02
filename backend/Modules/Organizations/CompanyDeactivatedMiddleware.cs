using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Organizations;

// A deactivated company is closed to everyone in it, at once: the next call
// from any session or API key in it is refused, whatever the token says.
//
// 401 with code "company_deactivated", so the app signs out (or moves to
// another company the person belongs to) instead of retrying. /auth stays
// open to a person's session — switching to another company and signing out
// must still work — but not to an API key (the partner /auth/verify and
// /auth/organizations live there too). Fusioneta support may look inside.
public class CompanyDeactivatedMiddleware
{
    public const string Code = "company_deactivated";
    public const string Message =
        "This company's AltomateHR account has been deactivated. Please contact AltomateHR support.";

    private readonly RequestDelegate _next;

    public CompanyDeactivatedMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, IOrganizationStatus status)
    {
        if (currentUser.OrganizationId is { } organizationId
            && !currentUser.IsSupport
            && (currentUser.IsScopedMachine
                || !context.Request.Path.StartsWithSegments("/auth", StringComparison.OrdinalIgnoreCase))
            && await status.IsDeactivatedAsync(organizationId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = Message, code = Code });
            return;
        }

        await _next(context);
    }
}
