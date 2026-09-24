using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Onboarding;

// The nine answers an external platform collects while provisioning a company,
// in one call. Every block optional — send only what the client answered.
//
// Shapes match the handover agreed with Altomate (docs/altomate-hr-policy-api-
// handover.md on their side), because their schemas are strict and a field
// renamed here is a 400 there.
public class OnboardingDto
{
    public OnboardingSettingsDto? Settings { get; set; }
    public OnboardingCalculationDto? Calculation { get; set; }
    public OnboardingOvertimeDto? Overtime { get; set; }
    public OnboardingWorkScheduleDto? WorkSchedule { get; set; }
    public OnboardingLeaveDto? Leave { get; set; }
}

public class OnboardingSettingsDto
{
    // One or the other, never both: they write the same column, so accepting
    // both would need a precedence rule nobody could remember. Refused instead.
    public List<string>? WorkingDays { get; set; }
    public List<string>? NonWorkingDays { get; set; }
}

public class OnboardingCalculationDto
{
    // "TWENTY_SIX" or "CALENDAR".
    public string? ProrationBasis { get; set; }
    public OnboardingHrdfDto? Hrdf { get; set; }
}

public class OnboardingHrdfDto
{
    public bool? Contribute { get; set; }

    // Must be > 0 when contributing: a null rate levies nothing at all, which
    // looks like a working configuration and is not.
    public decimal? Rate { get; set; }
}

// Same vocabulary GET /policies returns as otRates — what you read back is
// what you send.
public class OnboardingOvertimeDto
{
    public decimal? NormalDay { get; set; }
    public decimal? RestDay { get; set; }
    public decimal? PublicHoliday { get; set; }
    public decimal? RestDayInShift { get; set; }
    public decimal? PublicHolidayInShift { get; set; }

    // Hours x 60.
    public int? DailyThresholdMinutes { get; set; }
}

public class OnboardingWorkScheduleDto
{
    public string? WorkingHoursStart { get; set; }
    public string? WorkingHoursEnd { get; set; }
    public int? LunchBreakMinutes { get; set; }
}

public class OnboardingLeaveDto
{
    // Keyed by seeded leave-type code, case-insensitive.
    public Dictionary<string, double>? Entitlements { get; set; }
    public Dictionary<string, OnboardingCarryForwardDto>? CarryForward { get; set; }
}

public class OnboardingCarryForwardDto
{
    public bool Enabled { get; set; }

    // Month of year, 1-12. Required when enabled.
    [Range(1, 12)]
    public int? ExpiryMonth { get; set; }

    public double? MaxDays { get; set; }
}

// What actually landed. `Applied` is the contract: a 409 still names the blocks
// that were written, because the endpoint is not atomic and the caller's retry
// depends on knowing what not to worry about.
public record OnboardingResult(
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Failed,
    IReadOnlyList<string> PoliciesUpdated,
    IReadOnlyList<string> LeaveTypesUpdated,
    string? ValidationError = null);
