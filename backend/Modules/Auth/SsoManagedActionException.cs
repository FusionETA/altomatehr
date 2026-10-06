namespace AltomateHR.Api.Modules.Auth;

// Thrown when a session that arrived through the SSO hand-off tries something
// the external platform manages for that account (e.g. creating a company).
// Controllers map it to 403.
public class SsoManagedActionException : InvalidOperationException
{
    public SsoManagedActionException(string message) : base(message) { }
}
