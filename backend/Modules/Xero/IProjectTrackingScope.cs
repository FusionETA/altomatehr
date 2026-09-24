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
    // Hidden only when it demonstrably came from a DIFFERENT category. Manual
    // projects, Xero Projects-API ones (no tracking option) and rows synced
    // before the category was recorded (null category) all stay visible —
    // hiding a project whose category is unknown could hide a current one.
    public static bool IsHidden(
        string? trackingOptionId, string? trackingCategoryId, string? activeCategoryId) =>
        activeCategoryId is not null
        && trackingOptionId is not null
        && trackingCategoryId is not null
        && !string.Equals(trackingCategoryId, activeCategoryId, StringComparison.Ordinal);
}
