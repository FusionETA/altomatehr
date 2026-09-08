using System.Text.RegularExpressions;

namespace AltomateHR.Api.Modules.LhdnForms;

// "ANY" = available regardless of archive status. "ACTIVE_ONLY" / "ARCHIVED_ONLY"
// gate on EmployeeProfile.IsArchived — an employee is archived when HR records
// their last working day, which is also the trigger LHDN itself uses for
// cessation-notice deadlines.
public enum LhdnFormAvailability { Any, ActiveOnly, ArchivedOnly }

public sealed record LhdnFormMetaEntry(
    LhdnFormKind Kind,
    string Code,
    string Title,
    string Description,
    LhdnFormAvailability Requires,
    bool NeedsYearPicker);

public static class LhdnFormMeta
{
    public static readonly IReadOnlyDictionary<LhdnFormKind, LhdnFormMetaEntry> All =
        new Dictionary<LhdnFormKind, LhdnFormMetaEntry>
        {
            [LhdnFormKind.PCB2II] = new(
                LhdnFormKind.PCB2II,
                "PCB 2(II)",
                "Statement of payment",
                "Monthly MTD (PCB) deductions paid for this employee. Generate on LHDN's request, e.g. during tax clearance or to reconcile a misallocated PCB payment.",
                LhdnFormAvailability.Any,
                NeedsYearPicker: true),
            [LhdnFormKind.CP22] = new(
                LhdnFormKind.CP22,
                "CP22",
                "New-employee notification",
                "Notification to LHDN that a new employee subject to tax has joined. Must be submitted within 30 days of the join date.",
                LhdnFormAvailability.ActiveOnly,
                NeedsYearPicker: false),
            [LhdnFormKind.CP22A] = new(
                LhdnFormKind.CP22A,
                "CP22A",
                "Cessation notification",
                "Notification to LHDN that an employee subject to tax is leaving. Must be submitted at least 30 days before cessation (or 30 days after death).",
                LhdnFormAvailability.ArchivedOnly,
                NeedsYearPicker: false),
            [LhdnFormKind.CP21] = new(
                LhdnFormKind.CP21,
                "CP21",
                "Leaving-Malaysia notification",
                "Notification to LHDN that an employee is leaving Malaysia for more than 3 months. Must be submitted at least 30 days before departure.",
                LhdnFormAvailability.ArchivedOnly,
                NeedsYearPicker: false),
            [LhdnFormKind.TP3] = new(
                LhdnFormKind.TP3,
                "PCB/TP3",
                "Handover for next employer",
                "YTD income, EPF, zakat, and PCB figures the employee can hand to their next employer so the new payroll calculates PCB correctly for the rest of the year.",
                LhdnFormAvailability.ArchivedOnly,
                NeedsYearPicker: false),
        };

    public static bool IsAvailable(LhdnFormKind kind, bool isArchived) => All[kind].Requires switch
    {
        LhdnFormAvailability.Any => true,
        LhdnFormAvailability.ActiveOnly => !isArchived,
        LhdnFormAvailability.ArchivedOnly => isArchived,
        _ => false,
    };

    /// Mirrors the EA bulk-export pattern: predictable for admins downloading
    /// multiple files. Year is only appended for year-scoped forms (today,
    /// just PCB 2(II)) — the others are event-scoped, not year-scoped.
    public static string FileName(LhdnFormKind kind, string employeeCode, int? year)
    {
        var meta = All[kind];
        var tag = meta.NeedsYearPicker && year is not null ? $"_{year}" : "";
        var safeFormCode = Regex.Replace(meta.Code, "[^A-Za-z0-9]", "");
        var safeEmployeeCode = Regex.Replace(employeeCode, "[^A-Za-z0-9_-]", "_");
        return $"{safeFormCode}_{safeEmployeeCode}{tag}.pdf";
    }

    /// CP22's "must file within 30 days of joining" deadline, computed purely
    /// from join date + today — independent of the archive gate above.
    public static (string Text, string Variant)? Cp22DeadlineBadge(DateTime? joinDate, DateTime today)
    {
        if (joinDate is null) return null;
        var daysSinceJoin = (today.Date - joinDate.Value.Date).Days;
        var daysRemaining = 30 - daysSinceJoin;
        if (daysRemaining > 7) return ($"Due in {daysRemaining} days", "success");
        if (daysRemaining >= 0) return ($"Due in {daysRemaining} day(s)", "pending");
        var daysOverdue = -daysRemaining;
        return daysOverdue > 365
            ? ("Overdue — file late", "outline")
            : ($"Overdue by {daysOverdue} days", "rejected");
    }
}
