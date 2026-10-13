using AltomateHR.Api.Modules.Documents.Dtos;

namespace AltomateHR.Api.Modules.Documents;

// "Write with AI": drafts a letter template from what the admin describes,
// using only the merge-field registry. Stores nothing — the draft opens in the
// editor and is saved (and validated) like any other template.
public interface ITemplateAiService
{
    Task<TemplateAiResultDto> DraftAsync(TemplateAiRequestDto request, CancellationToken cancellationToken = default);
}
