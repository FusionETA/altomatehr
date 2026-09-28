namespace AltomateHR.Api.Modules.Overtime.Entities;

// One before- or after-work file on an overtime request — a photo, or a PDF
// such as a signed job sheet. Not its own table: stored as a JSON list on the
// request (OvertimeRequest.BeforeAttachmentsJson / AfterAttachmentsJson), the
// same shape Claims uses for its supporting documents.
public sealed class OvertimeAttachment
{
    // Stable within the request, so one file can be removed by id.
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    // "/overtime/photos/{file}" or "/overtime/photos/xero/{fileId}".
    public string Url { get; set; } = string.Empty;

    // What the person uploaded it as. Display only — the url is what's served.
    public string FileName { get; set; } = string.Empty;

    public DateTime AddedAt { get; set; }
}
