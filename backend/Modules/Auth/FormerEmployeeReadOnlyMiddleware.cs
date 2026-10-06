using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Auth;

// A session in a company the person no longer works at is VIEW-ONLY.
//
// They keep the membership after leaving or a transfer so they can still read
// that company's payslips and TP1 statements, which they may need for tax. But
// clocking in, claiming, applying for leave — anything that writes — belongs to
// a current employee, so it is refused here, once, instead of in every module.
//
// Reads stay open (the portal only shows Payslips in this state anyway). So do
// the account's own plumbing: /auth (switch back, refresh, log out, password),
// and marking notifications read / push subscriptions, which touch no company data.
public class FormerEmployeeReadOnlyMiddleware
{
    private static readonly string[] AllowedWritePrefixes = ["/auth", "/notifications", "/push"];

    private readonly RequestDelegate _next;

    public FormerEmployeeReadOnlyMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser)
    {
        var method = context.Request.Method;
        var isRead = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);

        if (!isRead
            && currentUser.IsFormer
            && !AllowedWritePrefixes.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "You no longer work at this company, so it's view-only. Switch to your current company to continue.",
            });
            return;
        }

        await _next(context);
    }
}
