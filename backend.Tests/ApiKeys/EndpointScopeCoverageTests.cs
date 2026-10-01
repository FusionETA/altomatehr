using System.Reflection;
using AltomateHR.Api.Modules.ApiKeys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace AltomateHR.Api.Tests.ApiKeys;

// Every endpoint must say what an API key may do with it. [RequireScope] only
// binds a key when it is THERE: an endpoint without it is open to every key,
// whatever scopes the key was given. 112 endpoints were found that way — a
// read-only key could create employees, approve claims or reset passwords.
//
// So each action needs one of:
//   [RequireScope("…")]  a key needs that scope
//   [HumanOnly]          no key at all (a person's own action, or a platform one)
//   [AllowAnonymous]     public by design
// on the action or its controller. A new endpoint without one fails here.
public class EndpointScopeCoverageTests
{
    // Deliberately reachable by any key — each one says why at its definition.
    private static readonly HashSet<string> OpenToAnyKey =
    [
        "PartnerApiController.WhoAmI",   // a key may always ask what it is
        "PartnerApiController.Pending",  // omits each section the key can't read
    ];

    [Fact]
    public void Every_endpoint_declares_what_an_API_key_may_do()
    {
        var controllers = typeof(ApiScopes).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        var unguarded = new List<string>();
        foreach (var controller in controllers)
        {
            var classGuarded = Guarded(controller);
            foreach (var action in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (!action.GetCustomAttributes<HttpMethodAttribute>().Any()) continue;
                var name = $"{controller.Name}.{action.Name}";
                if (classGuarded || Guarded(action) || OpenToAnyKey.Contains(name)) continue;
                unguarded.Add(name);
            }
        }

        Assert.True(unguarded.Count == 0,
            "Endpoints with no [RequireScope], [HumanOnly] or [AllowAnonymous]:\n  "
            + string.Join("\n  ", unguarded.OrderBy(n => n)));
    }

    private static bool Guarded(MemberInfo member) =>
        member.GetCustomAttributes<RequireScopeAttribute>().Any()
        || member.GetCustomAttributes<HumanOnlyAttribute>().Any()
        || member.GetCustomAttributes<AllowAnonymousAttribute>().Any();
}
