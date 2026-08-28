using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Organizations.Entities;

// A public holiday for one org. Ported from production's OrgHoliday: a plain
// (date, name) list, deliberately NOT a rule engine — there is no state code
// and no "observe national holidays" flag. An admin edits the list for the
// year, which keeps holiday policy where it belongs: with the org.
//
// Leave day-counting skips these, so a request spanning Hari Raya doesn't
// charge the employee for it.
public class OrgHoliday : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    public DateTime Date { get; set; }          // date only; time is ignored

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
