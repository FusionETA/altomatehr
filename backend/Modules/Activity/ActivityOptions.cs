namespace AltomateHR.Api.Modules.Activity;

// appsettings.json → "Activity". Every page view adds a row, so retention is
// what keeps the tables from growing forever. 90 days, longer than API
// monitoring's 30, because usage is compared month against month.
public class ActivityOptions
{
    public const string SectionName = "Activity";

    public int RetentionDays { get; set; } = 90;
}
