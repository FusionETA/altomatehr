namespace AltomateHR.Api.Modules.Payroll.Entities;

public enum ChildAbilityStatus
{
    NORMAL,
    DISABLED,
}

// A child's studying level, for PCB relief under LHDN Public Ruling 5/2019 §7.3.
//
// The ruling only ever produces RM 2,000 or RM 8,000 per child, so the enum
// collapses to the four cases that map onto those two amounts.
//
//   UNDER_18         — under 18. RM 2,000, no further questions.
//   PRE_UNIVERSITY   — 18+, pre-uni / matriculation / A-levels / Form 6.
//                      RM 2,000 per §7.3.2.
//   DIPLOMA_MALAYSIA — 18+, diploma or above at an approved Malaysian
//                      institution. RM 8,000 per §7.3.3(a).
//   DEGREE_ABROAD    — 18+, degree or above at an approved institution
//                      outside Malaysia. RM 8,000 per §7.3.3(b).
//
// The last two carry the same relief; they are split so Form EA and CP8D can
// report the two cohorts separately.
public enum ChildStudyingLevel
{
    UNDER_18,
    PRE_UNIVERSITY,
    DIPLOMA_MALAYSIA,
    DEGREE_ABROAD,
}

// What share of a child's relief this employee claims. HALF is the 50/50 split
// when both parents claim the same child.
public enum ChildPcbDeductionLevel
{
    FULL,
    HALF,
    NONE,
}

// One child on the employee's relief declaration. Persisted inside
// `EmployeeProfile.ChildReliefJson` as an array.
public sealed record ChildRelief
{
    public ChildAbilityStatus AbilityStatus { get; init; } = ChildAbilityStatus.NORMAL;

    public ChildStudyingLevel CurrentlyStudying { get; init; } = ChildStudyingLevel.UNDER_18;

    // Defaults to NONE so a child added without an explicit claim contributes no
    // relief — under-claiming is recoverable at year end, over-claiming is not.
    public ChildPcbDeductionLevel PcbDeduction { get; init; } = ChildPcbDeductionLevel.NONE;
}

// Reads `EmployeeProfile.ChildReliefJson` the way the previous system did.
//
// Profiles migrated from v1 carry the shapes v1 accepted over its life: a
// `currentlyStudying` of null, or the retired PRESCHOOL / PRIMARY / SECONDARY /
// NONE / HIGHER_ED codes, sometimes with an `age`. Strict deserialisation
// rejects a null or unknown enum, and one bad child used to make the whole
// list read as empty — silently dropping every child's relief (C = 0) while
// the profile screen still showed them.
//
// So each field is read leniently, with v1's own mapping
// (normaliseChildStudyingLevel / parseChildReliefJson):
//   · studying: legacy and missing → UNDER_18, HIGHER_ED → DIPLOMA_MALAYSIA
//   · ability:  anything but DISABLED → NORMAL
//   · claim:    anything but FULL / HALF → NONE (no claim, never an over-claim)
// A list that isn't JSON at all still reads as no children.
public static class ChildReliefJson
{
    public static IReadOnlyList<ChildRelief> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        System.Text.Json.JsonDocument doc;
        try
        {
            doc = System.Text.Json.JsonDocument.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return [];

            var children = new List<ChildRelief>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                children.Add(new ChildRelief
                {
                    AbilityStatus = Text(item, "abilityStatus") == "DISABLED"
                        ? ChildAbilityStatus.DISABLED
                        : ChildAbilityStatus.NORMAL,
                    CurrentlyStudying = StudyingLevel(Text(item, "currentlyStudying")),
                    PcbDeduction = Text(item, "pcbDeduction") switch
                    {
                        "FULL" => ChildPcbDeductionLevel.FULL,
                        "HALF" => ChildPcbDeductionLevel.HALF,
                        _ => ChildPcbDeductionLevel.NONE,
                    },
                });
            }
            return children;
        }
    }

    public static ChildStudyingLevel StudyingLevel(string? raw) => raw switch
    {
        "PRE_UNIVERSITY" => ChildStudyingLevel.PRE_UNIVERSITY,
        "DIPLOMA_MALAYSIA" or "HIGHER_ED" => ChildStudyingLevel.DIPLOMA_MALAYSIA,
        "DEGREE_ABROAD" => ChildStudyingLevel.DEGREE_ABROAD,
        // UNDER_18, and v1's retired PRESCHOOL / PRIMARY / SECONDARY / NONE / missing.
        _ => ChildStudyingLevel.UNDER_18,
    };

    // A property's string value, matched case-insensitively and upper-cased;
    // null when absent or not a string.
    private static string? Text(System.Text.Json.JsonElement item, string name)
    {
        foreach (var prop in item.EnumerateObject())
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            return prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                ? prop.Value.GetString()?.Trim().ToUpperInvariant()
                : null;
        }
        return null;
    }
}
