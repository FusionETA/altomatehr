namespace AltomateHR.Api.Modules.Auth;

// The password was right, but every company the account belongs to has been
// deactivated by Fusioneta. Said plainly rather than as "invalid credentials",
// so the person doesn't reset a password that was never the problem.
public class CompanyDeactivatedException : InvalidOperationException
{
    public CompanyDeactivatedException()
        : base(Organizations.CompanyDeactivatedMiddleware.Message) { }
}
