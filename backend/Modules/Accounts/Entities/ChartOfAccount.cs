using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Accounts.Entities;

// An account in the org's chart of accounts. Claims are filed against selectable
// accounts; some carry a spend limit or a mileage rate. (Xero-linked accounts come later.)
public class ChartOfAccount : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;             // e.g. "6100"

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;             // e.g. "Travel Expenses"

    [MaxLength(20)]
    public string Type { get; set; } = "EXPENSE";                // EXPENSE | BANK (loose string, like the real app)

    public bool IsSelectable { get; set; } = true;               // employees can file claims against it

    [Precision(12, 2)]
    public decimal? LimitAmount { get; set; }                    // optional spend limit

    public bool AllowMileageClaim { get; set; }

    [Precision(10, 4)]
    public decimal? MileageRate { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; }
}
