namespace AltomateHR.Api.Modules.Payroll;

// What a KWSP member number may look like.
//
// The KWSP i-Akaun upload refuses a row whose EPF number isn't numeric
// ("EPF number format is invalid") — and a placeholder like "XXXXXX" typed
// into a profile reached the file exactly that way, because the renderer
// copies the number as stored. Only the shape is checked: digits only. The
// length is not, because real numbers on file are 7–9 digits and KWSP's own
// rule on that isn't published.
public static class EpfNumberRule
{
    public const string Message =
        "EPF number must be digits only — the member number on their KWSP statement (e.g. 21968561).";

    // Null when fine, or when blank (whether it's REQUIRED is decided
    // elsewhere — by whether they contribute).
    public static string? Problem(string? epfNumber)
    {
        var value = epfNumber?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        return value.All(char.IsAsciiDigit) ? null : Message;
    }
}
