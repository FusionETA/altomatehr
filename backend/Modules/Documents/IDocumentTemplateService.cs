using AltomateHR.Api.Modules.Documents.Dtos;

namespace AltomateHR.Api.Modules.Documents;

// The company's letter template library.
public interface IDocumentTemplateService
{
    Task<List<DocumentTemplateDto>> GetAllAsync();

    // Null → not in this company (404).
    Task<DocumentTemplateDto?> GetAsync(string id);

    // Refused (400) for a blank name or body, or a body using merge fields
    // that don't exist — every unknown one is listed.
    Task<TemplateSaveResult> CreateAsync(SaveDocumentTemplateDto dto);
    Task<TemplateSaveResult> UpdateAsync(string id, SaveDocumentTemplateDto dto);

    // False → not found. Letters already generated from it are kept: they
    // carry their own snapshot of its name.
    Task<bool> DeleteAsync(string id);

    // Adds the sample letters this company doesn't already have.
    Task<AddSampleTemplatesResultDto> AddSamplesAsync();

    // The merge field registry, for the editor's side panel.
    IReadOnlyList<MergeFieldDto> GetMergeFields();
}
