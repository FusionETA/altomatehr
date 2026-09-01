namespace AltomateHR.Api.Modules.Ai;

// Mirrors Modules/Xero/XeroExceptions.cs: a configuration fault (our side, fix
// the secrets) versus a provider fault (their side, or a bad response).
public class AiConfigurationException : InvalidOperationException
{
    public AiConfigurationException(string message) : base(message) { }
}

public class AiProviderException : InvalidOperationException
{
    public AiProviderException(string message) : base(message) { }
}
