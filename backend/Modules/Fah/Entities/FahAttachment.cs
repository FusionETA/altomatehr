using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Fah.Entities;

// A file an admin attached in FAH's chat. Uploaded on its own, before the
// message it goes with, then tied to that chat when the message is sent.
//
// Images and PDFs go to the model provider's file store (ModelFileId), which
// deletes them by itself after FahFiles.KeepFor; the chat refers to them by
// id. Spreadsheets and Word files are read into text here (Text) and that
// text goes into the chat itself, so it never expires.
public class FahAttachment : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string UserId { get; set; } = string.Empty;

    // Set when the message it was attached to is sent; null until then.
    [MaxLength(40)]
    public string? ConversationId { get; set; }

    [MaxLength(160)]
    public string FileName { get; set; } = string.Empty;

    public FahAttachmentKind Kind { get; set; }

    [MaxLength(100)]
    public string MediaType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    // A PDF's page count, as far as it could be read (0 when it couldn't).
    public int Pages { get; set; }

    [MaxLength(100)]
    public string? ModelFileId { get; set; }

    // A spreadsheet or Word file, read into text.
    public string? Text { get; set; }

    // A spreadsheet's rows (serialised FahFiles.FahSheet list), whole, for an
    // employee import — Text is cut to what the model reads.
    public string? TableJson { get; set; }

    // What the admin and FAH are told about the file, e.g. that only the
    // first rows were read.
    [MaxLength(300)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    // When the model provider deletes the stored file (images and PDFs only).
    public DateTime? ExpiresAt { get; set; }
}

public enum FahAttachmentKind
{
    Image,
    Pdf,
    Text,
}
