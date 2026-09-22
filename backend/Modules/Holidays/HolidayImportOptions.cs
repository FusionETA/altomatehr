namespace AltomateHR.Api.Modules.Holidays;

// Config for importing a country's public holidays.
//
// The API key is a SECRET: it belongs in user-secrets, never in
// appsettings.json. Without one the import still works through date.nager.at —
// but not for Malaysia, which nager has no calendar for at all.
public class HolidayImportOptions
{
    public const string SectionName = "Holidays";

    public string CalendarificApiKey { get; set; } = string.Empty;
}
