namespace AltomateHR.Api.Modules.Claims;

public class ClaimValidationException : Exception
{
    public ClaimValidationException(string message, string? field = null) : base(message)
    {
        Field = field;
    }

    public string? Field { get; }
}
