using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Organizations;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AltomateHR.Api.Modules.Payroll;

// The run in the route ({id}) must be all the caller's people — a
// policy-limited admin runs payroll for their own policies, and a run holding
// anyone else stays view-only for them (IPayrollRunService.OutsideScopeAsync
// decides). On the controller it covers every change to a run; IncludeReads
// adds it to a read where the file IS the action (a statutory file, the bank
// file). A no-op for anyone who sees every employee, and for a route with no
// run in it (creating one is checked by the service).
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class PayrollRunInScopeAttribute : Attribute, IAsyncActionFilter
{
    public bool IncludeReads { get; init; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        if ((IncludeReads || !AccessGate.IsRead(context))
            && services.GetRequiredService<IEmployeeScope>().IsLimited
            && context.RouteData.Values.TryGetValue("id", out var raw)
            && raw?.ToString() is { Length: > 0 } id
            && await services.GetRequiredService<IPayrollRunService>().OutsideScopeAsync(id) is { } refused)
        {
            context.Result = AccessGate.Forbidden(refused);
            return;
        }

        await next();
    }
}
