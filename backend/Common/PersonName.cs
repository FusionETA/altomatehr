namespace AltomateHR.Api.Common;

// The name to show for a person: their name, else their email, else a fallback.
//
// `user.Name ?? user.Email` looks like that and isn't — `??` only skips a NULL.
// A name saved as "" (or "   ") passes straight through, and the person shows
// up as a blank cell: the loans table printed an empty name with the loan's
// note underneath, which then read as the name.
public static class PersonName
{
    public static string Display(string? name, string? email, string fallback = "")
    {
        if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        if (!string.IsNullOrWhiteSpace(email)) return email.Trim();
        return fallback;
    }

    // "Ahmad bin Ali (Mat)": the legal name with the nickname beside it, for
    // screens. Just the name when there's no nickname or it adds nothing.
    // Never for a payslip, statutory file, form or letter — those take the
    // legal name alone.
    public static string WithNickname(string name, string? nickname)
    {
        var nick = nickname?.Trim();
        return string.IsNullOrEmpty(nick) || string.Equals(nick, name.Trim(), StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name} ({nick})";
    }
}
