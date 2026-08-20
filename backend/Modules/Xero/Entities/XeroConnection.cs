using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Xero.Entities;

public class XeroConnection : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? ConnectionId { get; set; }

    [MaxLength(80)]
    public string TenantId { get; set; } = string.Empty;

    [MaxLength(160)]
    public string TenantName { get; set; } = string.Empty;

    [MaxLength(40)]
    public string? TenantType { get; set; }

    [MaxLength(40)]
    public string TokenType { get; set; } = "Bearer";

    [MaxLength(1000)]
    public string Scope { get; set; } = string.Empty;

    public string AccessTokenProtected { get; set; } = string.Empty;
    public string RefreshTokenProtected { get; set; } = string.Empty;

    public DateTime AccessTokenExpiresAt { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DisconnectedAt { get; set; }

    [NotMapped]
    public bool IsConnected => DisconnectedAt is null;
}
