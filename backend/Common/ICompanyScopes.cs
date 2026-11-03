using System.Security.Claims;

namespace AltomateHR.Api.Common;

// Runs work as the signed-in person in ANOTHER of their companies: a fresh DI
// scope whose ICurrentUser is them, with that company's org and their role
// there. Everything inside — the tenant filter, the OrganizationId stamped on
// new rows, the activity log's actor — is that company's, exactly as if they
// had switched to it and done it on its screens.
//
// It checks nothing itself: the caller decides who may, from the person's own
// memberships (FAH: only where they're the Owner).
public interface ICompanyScopes
{
    Task<T> RunAsync<T>(string organizationId, string role, Func<IServiceProvider, Task<T>> work);
}

// The person a CompanyScopes scope acts as. Empty in an ordinary request,
// where CurrentUser reads the request's own token.
public sealed class CompanyPrincipal
{
    public ClaimsPrincipal? Principal { get; set; }
}

public sealed class CompanyScopes : ICompanyScopes
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ICurrentUser _currentUser;

    public CompanyScopes(IServiceScopeFactory scopes, ICurrentUser currentUser)
    {
        _scopes = scopes;
        _currentUser = currentUser;
    }

    public async Task<T> RunAsync<T>(string organizationId, string role, Func<IServiceProvider, Task<T>> work)
    {
        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("Only a signed-in person can act in another company.");

        await using var scope = _scopes.CreateAsyncScope();
        // The same claims a sign-in to that company would carry (TokenService).
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Role, role),
            new("org", organizationId),
        };
        if (_currentUser.Email is { } email) claims.Add(new Claim(ClaimTypes.Email, email));
        scope.ServiceProvider.GetRequiredService<CompanyPrincipal>().Principal =
            new ClaimsPrincipal(new ClaimsIdentity(claims, "CompanyScope"));

        return await work(scope.ServiceProvider);
    }
}
