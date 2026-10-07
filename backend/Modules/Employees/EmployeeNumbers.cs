using System.Text.RegularExpressions;

namespace AltomateHR.Api.Modules.Employees;

// The next staff number for a company that left the field blank, continuing
// the company's OWN pattern: "GE-0042" → "GE-0043", "1007" → "1008". The
// pattern followed is the one most of the company's numbers already use (by
// prefix and digit width), so a stray manual number doesn't hijack it. A
// company with no numbers yet starts at "EMP-001".
//
// Pure: the caller checks the result against the database and asks again if
// someone took it meanwhile.
public static partial class EmployeeNumbers
{
    public const string FirstNumber = "EMP-001";

    [GeneratedRegex(@"^(?<prefix>.*?)(?<digits>\d+)$")]
    private static partial Regex Pattern();

    public static string Next(IEnumerable<string?> existing)
    {
        var taken = existing
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var parsed = taken
            .Select(n => Pattern().Match(n))
            .Where(m => m.Success)
            .Select(m => (Prefix: m.Groups["prefix"].Value, Digits: m.Groups["digits"].Value))
            .ToList();

        if (parsed.Count == 0) return Unused(FirstNumber, taken);

        // The house pattern: the commonest (prefix, width), ties to the one
        // with the highest number in use.
        var house = parsed
            .GroupBy(p => (p.Prefix, Width: p.Digits.Length))
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Max(p => long.Parse(p.Digits)))
            .First();

        var next = house.Max(p => long.Parse(p.Digits)) + 1;
        var candidate = house.Key.Prefix + next.ToString().PadLeft(house.Key.Width, '0');
        return Unused(candidate, taken);
    }

    // Steps past anything already taken (a manual number sitting in the way).
    private static string Unused(string candidate, IReadOnlySet<string> taken)
    {
        while (taken.Contains(candidate))
        {
            var m = Pattern().Match(candidate);
            var width = m.Groups["digits"].Value.Length;
            candidate = m.Groups["prefix"].Value
                        + (long.Parse(m.Groups["digits"].Value) + 1).ToString().PadLeft(width, '0');
        }
        return candidate;
    }
}
