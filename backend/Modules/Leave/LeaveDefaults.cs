using AltomateHR.Api.Modules.Leave.Entities;

namespace AltomateHR.Api.Modules.Leave;

// The leave types every new org starts with, ported from production's
// DEFAULT_LEAVE_TYPES. Day counts follow Malaysian statutory practice
// (maternity 98, paternity 7, hospitalisation 60).
//
// All LUMP_SUM with no carry-forward: an org opts into pro-rated accrual or
// carry-forward afterwards, and only on ANNUAL — see LeaveTypeService.Validate.
public static class LeaveDefaults
{
    public const string UnpaidCode = "UNPAID";
    public const string AnnualCode = "ANNUAL";

    public sealed record Seed(string Code, string Name, bool Paid, double DefaultDays);

    public static readonly IReadOnlyList<Seed> All =
    [
        new("ANNUAL",          "Annual Leave",           true,  14),
        new("MEDICAL",         "Medical Leave",          true,  14),
        new("COMPASSIONATE",   "Compassionate Leave",    true,   3),
        new("HOSPITALIZATION", "Hospitalization Leave",  true,  60),
        new("MARRIAGE",        "Marriage Leave",         true,   3),
        new("MATERNITY",       "Maternity Leave",        true,  98),
        new("PATERNITY",       "Paternity Leave",        true,   7),
        new("UNPAID",          "Unpaid Leave",           false,  0),
    ];

    // UNPAID is structural, not just a default: the apply path exempts unpaid
    // types from the balance check, so archiving it would remove that route.
    public static bool IsProtected(string? code) =>
        string.Equals(code?.Trim(), UnpaidCode, StringComparison.OrdinalIgnoreCase);
}
