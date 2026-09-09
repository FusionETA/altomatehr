namespace AltomateHR.Api.Modules.Xero;

public class XeroConfigurationException : InvalidOperationException
{
    public XeroConfigurationException(string message) : base(message) { }
}

public class XeroConnectionException : InvalidOperationException
{
    public XeroConnectionException(string message) : base(message) { }

    public XeroConnectionException(string message, int? statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    // The HTTP status Xero answered with, when the failure came from a response
    // rather than the transport. Null for anything we raised ourselves. Callers
    // use it to tell a dead credential (400/401) from Xero having a bad day
    // (429/5xx) — the first needs a human, the second needs a retry.
    public int? StatusCode { get; }
}

// The stored tokens cannot be used and no retry will help: Xero has revoked or
// expired the refresh token, or the data-protection key that encrypted them is
// gone. The only way forward is fresh OAuth consent, so this must surface as a
// "reconnect Xero" prompt — never as a 500, which is what it used to do.
public class XeroReconnectRequiredException : InvalidOperationException
{
    public XeroReconnectRequiredException(string message) : base(message) { }
}
