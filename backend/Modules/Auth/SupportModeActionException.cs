namespace AltomateHR.Api.Modules.Auth;

// An action refused because the caller is Fusioneta support acting inside a
// customer's company (support mode), where it would land on the wrong person —
// e.g. creating a company would make the SUPPORT account its Owner, not the
// customer. The message says where to do it instead. Answered as 403.
public class SupportModeActionException : InvalidOperationException
{
    public SupportModeActionException(string message) : base(message) { }
}
