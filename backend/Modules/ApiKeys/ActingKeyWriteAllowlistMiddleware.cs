using Microsoft.AspNetCore.Routing;

namespace AltomateHR.Api.Modules.ApiKeys;

// A FAH key (the in-app assistant acting as an admin) may READ whatever the
// admin and its scopes allow, but may only CHANGE data through the few routes
// FAH's tools use — each behind a preview the admin confirms on screen.
//
// An allow-list rather than a block-list, on purpose: payroll:write alone also
// covers submitting, approving, rejecting and reverting a run, deleting it,
// syncing it to Xero and emailing payslips. Those stay with a person on a
// screen, and a write route added later is refused here until someone decides
// FAH should have it.
public class ActingKeyWriteAllowlistMiddleware
{
    // (method, route template) — templates as the controllers declare them.
    private static readonly HashSet<(string Method, string Template)> Allowed =
    [
        ("POST", "employees"),
        ("PUT", "employees/{id}"),
        ("PUT", "employees/{id}/profile"),
        ("POST", "payroll/runs"),
        ("POST", "payroll/runs/{id}/generate"),
        ("PUT", "payroll/runs/{id}/adjustments/{employeeProfileId}"),
    ];

    private readonly RequestDelegate _next;

    public ActingKeyWriteAllowlistMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var isRead = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

        if (!isRead
            && context.User.HasClaim(ApiKeyAuthenticationDefaults.ActingClaim, "1")
            && !IsAllowed(method, context.GetEndpoint()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new
                {
                    status = 403,
                    message = "FAH can't do this. Open the screen in AltomateHR and do it there.",
                },
            });
            return;
        }

        await _next(context);
    }

    private static bool IsAllowed(string method, Endpoint? endpoint) =>
        endpoint is RouteEndpoint route
        && route.RoutePattern.RawText is { } template
        && Allowed.Contains((method.ToUpperInvariant(), template.Trim('/')));
}
