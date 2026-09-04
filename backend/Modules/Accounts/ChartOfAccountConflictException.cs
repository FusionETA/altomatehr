namespace AltomateHR.Api.Modules.Accounts;

// The org's state forbids the change, rather than the input being wrong — so
// callers map this to 409, not 400. Raised when a hand-made account would
// compete with Xero's chart of accounts.
public class ChartOfAccountConflictException : InvalidOperationException
{
    public ChartOfAccountConflictException(string message) : base(message) { }
}
