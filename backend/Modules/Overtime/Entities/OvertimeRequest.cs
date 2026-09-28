using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Overtime.Entities;

public class OvertimeRequest : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string EmployeeId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string? ProjectId { get; set; }

    public DateTime WorkDate { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public int RequestedMinutes { get; set; }

    [MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;

    // The FIRST before/after file, mirrored from the lists below. Kept filled
    // so anything reading the single-photo columns — an older deployment on
    // the same database, the seed data — still sees a photo.
    [MaxLength(1000)]
    public string BeforePhotoUrl { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? AfterPhotoUrl { get; set; }

    public const int MaxAttachmentsPerSide = 10;

    // Every before/after file, as JSON. NULL means "written before lists
    // existed": the list is then read from the single-photo column, so older
    // rows (and ones an older deployment writes) need no data migration.
    // "[]" is a real, empty list.
    [Column("BeforeAttachments")]
    [JsonIgnore]
    public string? BeforeAttachmentsJson { get; set; }

    [Column("AfterAttachments")]
    [JsonIgnore]
    public string? AfterAttachmentsJson { get; set; }

    [NotMapped]
    public List<OvertimeAttachment> BeforeAttachments
    {
        get => Read(BeforeAttachmentsJson, BeforePhotoUrl, "legacy-before");
        set
        {
            BeforeAttachmentsJson = JsonSerializer.Serialize(value);
            BeforePhotoUrl = value.FirstOrDefault()?.Url ?? string.Empty;
        }
    }

    [NotMapped]
    public List<OvertimeAttachment> AfterAttachments
    {
        get => Read(AfterAttachmentsJson, AfterPhotoUrl, "legacy-after");
        set
        {
            AfterAttachmentsJson = JsonSerializer.Serialize(value);
            AfterPhotoUrl = value.FirstOrDefault()?.Url;
        }
    }

    private List<OvertimeAttachment> Read(string? json, string? legacyUrl, string legacyId)
    {
        if (json is not null)
            return JsonSerializer.Deserialize<List<OvertimeAttachment>>(json) ?? [];

        return string.IsNullOrWhiteSpace(legacyUrl)
            ? []
            : [new OvertimeAttachment
            {
                Id = legacyId,
                Url = legacyUrl,
                FileName = legacyUrl.Split('/').Last(),
                AddedAt = SubmittedAt,
            }];
    }

    public OvertimeStatus Status { get; set; } = OvertimeStatus.PENDING;

    public int CurrentStep { get; set; }

    [MaxLength(1000)]
    public string? ReviewNotes { get; set; }

    // Who last decided on this request. Null where nobody did: a submission
    // auto-approved because the employee has no approver, or one resolved by
    // the unreachable-approval sweep. Naming a person for those would be a
    // fabrication on an audit surface.
    [MaxLength(40)]
    public string? ReviewerId { get; set; }

    public DateTime SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
