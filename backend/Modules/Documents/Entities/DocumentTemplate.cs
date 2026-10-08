using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Documents.Entities;

// What kind of letter a template produces. Only used to group and label the
// library — nothing branches on it.
public enum DocumentCategory
{
    OFFER,
    CONFIRMATION,
    PROMOTION,
    RESIGNATION,
    TERMINATION,
    WARNING,
    OTHER,
}

// A letter template in a company's library, authored in-app.
//
// Body is a small markdown subset with {{merge.fields}} — see LetterMarkup for
// the grammar and MergeFields for the field registry. Per company: one org's
// wording never shows up in another's library.
public class DocumentTemplate : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    public DocumentCategory Category { get; set; } = DocumentCategory.OTHER;

    // The template text. Long: a termination letter with its clauses runs to
    // a few thousand characters.
    public string Body { get; set; } = string.Empty;

    // Set on the rows created by "Add sample templates", so adding them twice
    // does not duplicate a sample the company still has. Null for the
    // company's own templates.
    [MaxLength(40)]
    public string? SampleKey { get; set; }

    [MaxLength(40)]
    public string? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
