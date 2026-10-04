namespace AltomateHR.Api.Modules.ApiMonitoring;

// appsettings.json → "ApiMonitoring". Every request adds a row, so retention is
// what keeps the table from growing forever; 30 days covers a month-end payroll
// cycle with room to compare it against the one before.
public class ApiMonitoringOptions
{
    public const string SectionName = "ApiMonitoring";

    public int RetentionDays { get; set; } = 30;
}
