namespace AltomateHR.Api.Modules.Xero;

// Which Xero tracking category currently holds this org's projects.
//
// A Xero org can have two active tracking categories (say "Project" and
// "Region"), and the admin chooses which one becomes AltomateHR's project list.
// Switching must not delete the projects synced from the other one — past
// claims and shifts still point at them — so instead they are hidden from
// anywhere a project is PICKED, and come back if the admin switches back.
// Name lookups on historical records are unaffected.
//
// A narrow port rather than a member on IXeroService, so the Projects module
// depends on exactly this one fact and not on the whole Xero surface.
public interface IProjectTrackingScope
{
    // Null when Xero isn't connected or no category has been chosen — then
    // nothing is hidden, which is how it behaved before a choice existed.
    Task<string?> GetActiveCategoryIdAsync();
}

public static class ProjectTrackingVisibility
{
    // Connecting Xero archives every hand-created project that exists AT THAT
    // MOMENT (XeroService.ArchiveManualProjectsAsync) — but that is a one-time
    // sweep, not a standing rule, so a manual project made afterwards (or one
    // the sweep missed) would otherwise sit in the picker forever with nothing
    // to reconcile it against. Once Xero is actually providing the list, a
    // project with NEITHER Xero id has no place in it.
    //
    // A Xero Projects-API row (xeroProjectId set, no tracking option) is a
    // different case — it demonstrably IS Xero's, just via the legacy sync —
    // so it stays, same as a tracked row whose category is unknown.
    public static bool IsHidden(
        string? xeroProjectId, string? trackingOptionId, string? trackingCategoryId,
        string? activeCategoryId)
    {
        if (activeCategoryId is null) return false;   // Xero isn't connected/configured yet.

        if (xeroProjectId is null && trackingOptionId is null) return true;   // truly hand-made

        // Hidden only when it demonstrably came from a DIFFERENT category. A
        // legacy Projects-API row and one synced before the category was
        // recorded (null category) both stay — hiding a project whose
        // category is unknown could hide a current one.
        return trackingOptionId is not null
            && trackingCategoryId is not null
            && !string.Equals(trackingCategoryId, activeCategoryId, StringComparison.Ordinal);
    }
}
