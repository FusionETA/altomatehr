using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Documents.Dtos;
using AltomateHR.Api.Modules.Documents.Entities;

namespace AltomateHR.Api.Modules.Documents;

public class DocumentTemplateService : IDocumentTemplateService
{
    private readonly IDocumentTemplateRepository _templates;
    private readonly ICurrentUser _currentUser;

    public DocumentTemplateService(IDocumentTemplateRepository templates, ICurrentUser currentUser)
    {
        _templates = templates;
        _currentUser = currentUser;
    }

    public async Task<List<DocumentTemplateDto>> GetAllAsync() =>
        (await _templates.GetAllAsync()).Select(ToDto).ToList();

    public async Task<DocumentTemplateDto?> GetAsync(string id) =>
        await _templates.GetByIdAsync(id) is { } t ? ToDto(t) : null;

    public async Task<TemplateSaveResult> CreateAsync(SaveDocumentTemplateDto dto)
    {
        if (Validate(dto) is { } refused) return refused;

        var now = DateTime.UtcNow;
        var template = new DocumentTemplate
        {
            Name = dto.Name.Trim(),
            Category = dto.Category,
            Body = dto.Body,
            CreatedByUserId = _currentUser.UserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _templates.AddAsync(template);
        return new TemplateSaveResult(true, ToDto(template), null);
    }

    public async Task<TemplateSaveResult> UpdateAsync(string id, SaveDocumentTemplateDto dto)
    {
        var template = await _templates.GetByIdAsync(id);
        if (template is null) return TemplateSaveResult.NotFound();
        if (Validate(dto) is { } refused) return refused;

        template.Name = dto.Name.Trim();
        template.Category = dto.Category;
        template.Body = dto.Body;
        template.UpdatedAt = DateTime.UtcNow;
        await _templates.UpdateAsync(template);
        return new TemplateSaveResult(true, ToDto(template), null);
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var template = await _templates.GetByIdAsync(id);
        if (template is null) return false;
        await _templates.DeleteAsync(template);
        return true;
    }

    public async Task<AddSampleTemplatesResultDto> AddSamplesAsync()
    {
        var existing = (await _templates.GetSampleKeysAsync()).ToHashSet(StringComparer.Ordinal);
        var now = DateTime.UtcNow;

        var added = SampleTemplates.All
            .Where(s => !existing.Contains(s.Key))
            .Select(s => new DocumentTemplate
            {
                Name = s.Name,
                Category = s.Category,
                Body = s.Body,
                SampleKey = s.Key,
                CreatedByUserId = _currentUser.UserId,
                CreatedAt = now,
                UpdatedAt = now,
            })
            .ToList();

        if (added.Count > 0) await _templates.AddRangeAsync(added);

        return new AddSampleTemplatesResultDto
        {
            Added = added.Count,
            Skipped = SampleTemplates.All.Count - added.Count,
            Templates = added.Select(ToDto).ToList(),
        };
    }

    public TemplateImportResult Import(string? fileName, byte[] content)
    {
        var outcome = DocxTemplateImport.Convert(fileName, content);
        if (!outcome.Ok) return new TemplateImportResult(false, null, outcome.Error);

        return new TemplateImportResult(true, new TemplateImportDto
        {
            SuggestedName = outcome.SuggestedName,
            Body = outcome.Body,
            UnknownFields = DocxTemplateImport.UnknownPlaceholders(outcome.Body).ToList(),
            Warnings = outcome.Warnings.ToList(),
        }, null);
    }

    public IReadOnlyList<MergeFieldDto> GetMergeFields() =>
        MergeFields.All.Select(f => new MergeFieldDto
        {
            Key = f.Key,
            Label = f.Label,
            Source = f.Source,
            Kind = f.Kind,
            Description = f.Description,
            WritableToEmployee = f.WritableToEmployee,
        }).ToList();

    private static TemplateSaveResult? Validate(SaveDocumentTemplateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return new TemplateSaveResult(false, null, "Give the template a name.");
        if (string.IsNullOrWhiteSpace(dto.Body))
            return new TemplateSaveResult(false, null, "The template has no text.");

        var unknown = MergeFields.UnknownKeysIn(dto.Body);
        if (unknown.Count > 0)
        {
            var list = string.Join(", ", unknown.Select(k => "{{" + k + "}}"));
            return new TemplateSaveResult(false, null,
                $"These merge fields don't exist: {list}. Pick fields from the list, or use {{{{input.yourName}}}} for something typed in for each letter.",
                unknown);
        }

        return null;
    }

    internal static DocumentTemplateDto ToDto(DocumentTemplate t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Category = t.Category,
        Body = t.Body,
        IsSample = t.SampleKey is not null,
        Fields = MergeFields.KeysIn(t.Body).ToList(),
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
    };
}
