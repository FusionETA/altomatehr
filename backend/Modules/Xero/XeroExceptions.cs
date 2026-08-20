namespace AltomateHR.Api.Modules.Xero;

public class XeroConfigurationException : InvalidOperationException
{
    public XeroConfigurationException(string message) : base(message) { }
}

public class XeroConnectionException : InvalidOperationException
{
    public XeroConnectionException(string message) : base(message) { }
}
