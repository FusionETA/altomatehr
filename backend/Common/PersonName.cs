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
}
