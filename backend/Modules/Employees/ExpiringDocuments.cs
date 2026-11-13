using AltomateHR.Api.Modules.Employees.Dtos;

namespace AltomateHR.Api.Modules.Employees;

// The two documents a foreign worker must keep valid.
public enum IdentityDocument
{
    WORK_PERMIT,
    PASSPORT,
}

// One document that lapses on `Expiry`. DaysLeft is negative once expired,
// 0 on the day.
public sealed record ExpiringDocument(
    string UserId, IdentityDocument Document, string? Number, DateTime Expiry, int DaysLeft);

// Which work permits and passports matter, in one place: the dashboard's
// "Documents expiring" card and the daily expiry reminders must agree on who
// counts, or HR is reminded about someone the card leaves out.
public static class ExpiringDocuments
{
    // Foreign staff only. A citizen or PR has no permit to renew, and a blank
    // nationality isn't known to be foreign.
    public static bool IsForeignWorker(IdentityDocumentsDto p) =>
        !p.HasPr
        && !string.IsNullOrWhiteSpace(p.Nationality)
        && !Payroll.PayslipCalculator.IsMalaysianNationality(p.Nationality);

    // Still working here on `today`: not archived, not already gone.
    public static bool IsCurrent(IdentityDocumentsDto p, DateTime today) =>
        !p.IsArchived && !(p.LeaveDate is { } left && left.Date < today.Date);

    // Every work permit and passport of current foreign staff that lapses
    // within `withinDays` of `today`, or already has. Soonest first.
    public static List<ExpiringDocument> Due(
        IEnumerable<IdentityDocumentsDto> people, DateTime today, int withinDays)
    {
        var day = today.Date;
        return people
            .Where(p => IsForeignWorker(p) && IsCurrent(p, day))
            .SelectMany(p => DocumentsOf(p, day))
            .Where(d => d.DaysLeft <= withinDays)
            .OrderBy(d => d.DaysLeft)
            .ThenBy(d => d.Document)
            .ToList();
    }

    private static IEnumerable<ExpiringDocument> DocumentsOf(IdentityDocumentsDto p, DateTime today)
    {
        if (p.WorkPermitExpiry is { } permit)
            yield return new(p.UserId, IdentityDocument.WORK_PERMIT, Blank(p.WorkPermitNumber), permit.Date,
                (int)(permit.Date - today).TotalDays);
        if (p.PassportExpiry is { } passport)
            yield return new(p.UserId, IdentityDocument.PASSPORT, Blank(p.PassportNumber), passport.Date,
                (int)(passport.Date - today).TotalDays);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static string Label(IdentityDocument d) => d == IdentityDocument.PASSPORT ? "passport" : "work permit";
}
