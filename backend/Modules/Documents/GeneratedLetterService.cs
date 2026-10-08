using System.Globalization;
using System.Text.Json;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Documents.Dtos;
using AltomateHR.Api.Modules.Documents.Entities;
using AltomateHR.Api.Modules.Documents.Pdf;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Documents;

// Template + employee → letter.
//
// The rule the product owner set: a letter never goes out with a blank where a
// detail should be. So every field the template uses is resolved from the
// records first (ResolveAsync), the dialog shows what was found and what is
// missing, and GenerateAsync refuses until each gap is filled for this letter
// or explicitly left blank. What the admin types is used for this one letter
// only — unless they tick "Also save to employee record" for a gap, which goes
// through IEmployeeProfileService (never the repository) and only ever fills
// an EMPTY field.
//
// A letter is filled from the employee's record, which belongs to the
// Employees module, not this one. So beside the Documents gate on the
// controller, anything that reads a person's record needs Employees at View
// (employees:read for an API key) and saving to it needs Manage
// (employees:write) — else an admin granted Documents but not Employees could
// read ICs and salaries, or edit the record, through a letter.
public class GeneratedLetterService : IGeneratedLetterService
{
    private const int MaxListed = 200;
    // A typed detail is a date, a name, a sentence or two — not a document.
    private const int MaxTypedLength = 2000;
    private static readonly TimeZoneInfo Myt = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // Company Info lives under Payroll → Settings → Form E (LHDN).
    private const string CompanyInfoHint =
        "Company detail — set it in Payroll → Settings → Form E (LHDN) → Employer details.";
    private const string SignatoryHint =
        "Defaults to the declarant in Payroll → Settings → Form E (LHDN). Set it there, or type it here for this letter.";
    private const string ManageEmployeeHint = "Set it in Company/Employee → Manage Employee.";

    private readonly IDocumentTemplateRepository _templates;
    private readonly IGeneratedDocumentRepository _generated;
    private readonly IGeneratedDocumentStorage _storage;
    private readonly IOrganizationMembershipRepository _memberships;
    private readonly IEmployeeProfileRepository _profiles;
    private readonly IEmploymentPeriodRepository _periods;
    private readonly IUserRepository _users;
    private readonly IPayrollCompanyInfoRepository _companyInfo;
    private readonly IOrganizationRepository _organizations;
    private readonly IEmployeeProfileService _profileService;
    private readonly IEmployeeScope _scope;
    private readonly IModuleAccessService _access;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;

    public GeneratedLetterService(
        IDocumentTemplateRepository templates,
        IGeneratedDocumentRepository generated,
        IGeneratedDocumentStorage storage,
        IOrganizationMembershipRepository memberships,
        IEmployeeProfileRepository profiles,
        IEmploymentPeriodRepository periods,
        IUserRepository users,
        IPayrollCompanyInfoRepository companyInfo,
        IOrganizationRepository organizations,
        IEmployeeProfileService profileService,
        IEmployeeScope scope,
        IModuleAccessService access,
        ICurrentUser currentUser,
        IAuditService audit)
    {
        _templates = templates;
        _generated = generated;
        _storage = storage;
        _memberships = memberships;
        _profiles = profiles;
        _periods = periods;
        _users = users;
        _companyInfo = companyInfo;
        _organizations = organizations;
        _profileService = profileService;
        _scope = scope;
        _access = access;
        _currentUser = currentUser;
        _audit = audit;
    }

    // ─── Resolve ─────────────────────────────────────────────────────────

    public async Task<ResolveResult> ResolveAsync(string templateId, string employeeUserId)
    {
        var employeeLevel = await _access.GetCallerLevelAsync(OrgModules.Employees);
        if (EmployeeRecordDenied(employeeLevel, write: false) is { } denied) return ResolveResult.Denied(denied);

        var template = await _templates.GetByIdAsync(templateId);
        if (template is null) return ResolveResult.NotFound();

        var employee = await LoadEmployeeAsync(employeeUserId);
        if (employee is null) return ResolveResult.NotFound();

        var record = RecordValues(employee, await LoadCompanyAsync());

        var fields = MergeFields.UsedKeys(template.Body).Select(key =>
        {
            var def = MergeFields.Describe(key);
            var found = record.GetValueOrDefault(key);
            var missing = found.Display is null;
            return new ResolvedFieldDto
            {
                Key = key,
                Label = def.Label,
                Source = def.Source,
                Kind = def.Kind,
                Value = found.Display,
                EditValue = found.Edit,
                Missing = missing,
                // Only offered to a caller who could save it (Employees: Manage).
                WritableToEmployee = missing && def.WritableToEmployee && employeeLevel >= ModuleLevel.Manage,
                FixHint = missing ? FixHintFor(def) : null,
            };
        }).ToList();

        return new ResolveResult(true, new ResolvedLetterDto
        {
            TemplateId = template.Id,
            TemplateName = template.Name,
            EmployeeUserId = employeeUserId,
            EmployeeName = employee.User.Name,
            Fields = fields,
        }, null);
    }

    private static string? FixHintFor(MergeFieldDefinition def) => def.Source switch
    {
        MergeFieldSource.Company => CompanyInfoHint,
        MergeFieldSource.Signatory => SignatoryHint,
        MergeFieldSource.Employee when !def.WritableToEmployee => ManageEmployeeHint,
        _ => null,
    };

    // ─── Preview ─────────────────────────────────────────────────────────

    public async Task<LetterFileResult> PreviewAsync(string? templateId, PreviewLetterDto dto)
    {
        string name;
        string body;
        if (templateId is not null)
        {
            var template = await _templates.GetByIdAsync(templateId);
            if (template is null) return LetterFileResult.NotFound();
            name = string.IsNullOrWhiteSpace(dto.Name) ? template.Name : dto.Name.Trim();
            body = dto.Body ?? template.Body;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(dto.Body))
                return LetterFileResult.Refused("The template has no text to preview.");
            name = string.IsNullOrWhiteSpace(dto.Name) ? "Letter" : dto.Name.Trim();
            body = dto.Body;
        }

        var unknown = MergeFields.UnknownKeysIn(body);
        if (unknown.Count > 0)
            return LetterFileResult.Refused(
                "These merge fields don't exist: " + string.Join(", ", unknown.Select(k => "{{" + k + "}}")) + ".");

        EmployeeContext? employee = null;
        if (!string.IsNullOrWhiteSpace(dto.EmployeeUserId))
        {
            var level = await _access.GetCallerLevelAsync(OrgModules.Employees);
            if (EmployeeRecordDenied(level, write: false) is { } denied) return LetterFileResult.Denied(denied);

            employee = await LoadEmployeeAsync(dto.EmployeeUserId);
            if (employee is null) return LetterFileResult.NotFound();
        }

        var company = await LoadCompanyAsync();
        var record = RecordValues(employee, company);

        // Whatever has no value prints as its label in brackets, so the
        // preview shows where each field lands.
        var values = MergeFields.UsedKeys(body).ToDictionary(
            k => k,
            k => record.GetValueOrDefault(k).Display ?? $"[{MergeFields.Describe(k).Label}]",
            StringComparer.Ordinal);

        var pdf = Render(name, body, values, company,
            watermark: "PREVIEW — fields shown in [brackets] have no value yet.");
        var fileName = SafeFileName($"{name} - preview.pdf");
        return LetterFileResult.File(fileName, pdf);
    }

    // ─── Generate ────────────────────────────────────────────────────────

    public async Task<LetterFileResult> GenerateAsync(string templateId, GenerateLetterDto dto)
    {
        // A write-back the caller may not make is refused outright, never
        // silently skipped: an admin who ticked "Also save to employee record"
        // must not be told the letter worked while the record stayed as it was.
        var employeeLevel = await _access.GetCallerLevelAsync(OrgModules.Employees);
        if (EmployeeRecordDenied(employeeLevel, write: false) is { } cannotRead)
            return LetterFileResult.Denied(cannotRead);
        if (dto.SaveToEmployee is { Count: > 0 } && EmployeeRecordDenied(employeeLevel, write: true) is { } cannotWrite)
            return LetterFileResult.Denied(cannotWrite);

        var template = await _templates.GetByIdAsync(templateId);
        if (template is null) return LetterFileResult.NotFound();

        var employee = await LoadEmployeeAsync(dto.EmployeeUserId);
        if (employee is null) return LetterFileResult.NotFound();

        var unknown = MergeFields.UnknownKeysIn(template.Body);
        if (unknown.Count > 0)
            return LetterFileResult.Refused(
                "This template uses merge fields that don't exist: "
                + string.Join(", ", unknown.Select(k => "{{" + k + "}}")) + ". Edit the template first.");

        var company = await LoadCompanyAsync();
        var record = RecordValues(employee, company);
        var typed = (dto.Values ?? new Dictionary<string, string?>())
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value!.Trim(), StringComparer.Ordinal);
        if (typed.FirstOrDefault(kv => kv.Value.Length > MaxTypedLength) is { Key: not null } tooLong)
            return LetterFileResult.Refused(
                $"{MergeFields.Describe(tooLong.Key).Label} is too long — keep it under {MaxTypedLength} characters.");
        var leaveBlank = (dto.LeaveBlank ?? []).ToHashSet(StringComparer.Ordinal);

        // Compose: what the admin typed for this letter wins, then the record,
        // then an explicit blank. Anything left is a gap, and refuses.
        var keys = MergeFields.UsedKeys(template.Body);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var key in keys)
        {
            var def = MergeFields.Describe(key);
            if (typed.TryGetValue(key, out var t)) values[key] = MergeFields.FormatTyped(def.Kind, t);
            else if (record.GetValueOrDefault(key).Display is { } onFile) values[key] = onFile;
            else if (leaveBlank.Contains(key)) values[key] = string.Empty;
            else missing.Add(key);
        }

        if (missing.Count > 0)
        {
            var labels = string.Join(", ", missing.Select(k => MergeFields.Describe(k).Label));
            return LetterFileResult.Refused(
                $"Fill in these details, or choose to leave them blank, before generating: {labels}.", missing);
        }

        // "Also save to employee record": only gaps in writable employee
        // fields, only with a value typed for this letter.
        var writeBack = BuildWriteBack(dto.SaveToEmployee, keys, record, typed, out var writeBackError);
        if (writeBackError is not null) return LetterFileResult.Refused(writeBackError);

        IReadOnlyList<string> savedToRecord = [];
        if (writeBack is not null)
            savedToRecord = await _profileService.FillMissingFieldsAsync(employee.User.Id, writeBack) ?? [];

        var pdf = Render(template.Name, template.Body, values, company, watermark: null);
        var today = TodayInMalaysia();
        var fileName = SafeFileName($"{template.Name} - {employee.User.Name} - {today:yyyy-MM-dd}.pdf");

        string? savedId = null;
        if (dto.Save)
        {
            var organizationId = _currentUser.OrganizationId
                ?? throw new InvalidOperationException("No organisation on the request.");
            var stored = await _storage.StoreAsync(organizationId, employee.User.Id, pdf);
            var me = _currentUser.UserId is { } uid ? await _users.GetByIdAsync(uid) : null;

            var kept = new GeneratedDocument
            {
                TemplateId = template.Id,
                TemplateName = template.Name,
                Category = template.Category,
                EmployeeUserId = employee.User.Id,
                GeneratedByUserId = _currentUser.UserId,
                GeneratedByName = me?.Name ?? _currentUser.Email,
                FileName = fileName,
                StoredFileName = stored,
                SizeBytes = pdf.LongLength,
                ValuesJson = JsonSerializer.Serialize(values),
                CreatedAt = DateTime.UtcNow,
            };
            await _generated.AddAsync(kept);
            savedId = kept.Id;
        }

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.DocumentsLetterGenerate,
            $"Generated \"{template.Name}\" for {employee.User.Name}" + (dto.Save ? "" : " (not kept on file)"),
            TargetType: dto.Save ? "GeneratedDocument" : "DocumentTemplate",
            TargetId: savedId ?? template.Id,
            Metadata: new
            {
                TemplateId = template.Id,
                TemplateName = template.Name,
                EmployeeUserId = employee.User.Id,
                Saved = dto.Save,
                SavedToEmployeeRecord = savedToRecord,
            }));

        return LetterFileResult.File(fileName, pdf);
    }

    private static FillEmployeeFieldsDto? BuildWriteBack(
        List<string>? requested,
        IReadOnlyList<string> used,
        Dictionary<string, (string? Display, string? Edit)> record,
        Dictionary<string, string> typed,
        out string? error)
    {
        error = null;
        if (requested is null || requested.Count == 0) return null;

        var dto = new FillEmployeeFieldsDto();
        var any = false;
        foreach (var key in requested.Distinct(StringComparer.Ordinal))
        {
            var def = MergeFields.Find(key);
            if (def is null || !def.WritableToEmployee || !used.Contains(key)) continue;
            if (record.GetValueOrDefault(key).Display is not null) continue;   // never overwrite the record
            if (!typed.TryGetValue(key, out var value)) continue;

            switch (key)
            {
                // Fitted to the columns by FillMissingFieldsAsync, after it
                // shapes them for storage (the address grows when joined).
                case "employee.idNumber": dto.IdNumber = value; break;
                case "employee.address": dto.Address = value; break;
                case "employee.jobTitle": dto.JobTitle = value; break;
                case "employee.department": dto.Department = value; break;
                case "employee.location": dto.Location = value; break;
                case "employee.probationMonths":
                    if (!int.TryParse(value, NumberStyles.Integer, Invariant, out var months) || months is < 0 or > 120)
                    {
                        error = "Probation must be a whole number of months (0–120) to save it to the employee record.";
                        return null;
                    }
                    dto.ProbationMonths = months;
                    break;
                case "employee.confirmationDate":
                    if (!DateTime.TryParseExact(value, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var date))
                    {
                        error = "Pick the confirmation date from the calendar to save it to the employee record.";
                        return null;
                    }
                    dto.ConfirmationDate = date;
                    break;
                default: continue;
            }
            any = true;
        }

        return any ? dto : null;
    }

    // Why the caller may not read (or, with write, change) an employee's
    // record through a letter; null = allowed. The level comes from
    // IModuleAccessService.GetCallerLevelAsync: Owners and unrestricted admins
    // are Manage, an API key is capped by its employees:* scopes.
    private string? EmployeeRecordDenied(ModuleLevel level, bool write)
    {
        if (level >= (write ? ModuleLevel.Manage : ModuleLevel.View)) return null;

        // Same wording as [RequireScope], for integrators.
        if (_currentUser.IsScopedMachine)
            return $"Caller is missing required scope: {OrgModules.Employees}:{(write ? "write" : "read")}.";

        return write
            ? "Saving to the employee record needs manage access to Employees. Untick \"Also save to employee record\", or ask the organization's owner for manage access."
            : "Letters are filled from the employee's record, and your admin access doesn't include Employees. Ask the organization's owner to grant it.";
    }

    // ─── Letters on file ─────────────────────────────────────────────────

    // A kept letter is the employee's record in print, so the same Employees
    // View requirement as Resolve applies to every letter on file.
    private async Task<string?> SavedLettersDeniedAsync() =>
        EmployeeRecordDenied(await _access.GetCallerLevelAsync(OrgModules.Employees), write: false);

    public async Task<LetterListResult> ListAsync(string? employeeUserId)
    {
        if (await SavedLettersDeniedAsync() is { } denied) return LetterListResult.Denied(denied);

        // A limited admin asking for someone outside their scope sees nothing,
        // as if the person had no letters. (The query filter enforces this
        // anyway; this just skips the query.)
        if (!string.IsNullOrEmpty(employeeUserId) && !_scope.Contains(employeeUserId)) return LetterListResult.Of([]);

        var rows = await _generated.ListAsync(employeeUserId, MaxListed);
        var names = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var id in rows.Select(r => r.EmployeeUserId).Distinct())
            names[id] = (await _users.GetByIdAsync(id))?.Name;

        return LetterListResult.Of(rows.Select(r => new GeneratedDocumentDto
        {
            Id = r.Id,
            TemplateId = r.TemplateId,
            TemplateName = r.TemplateName,
            Category = r.Category,
            EmployeeUserId = r.EmployeeUserId,
            EmployeeName = names.GetValueOrDefault(r.EmployeeUserId),
            GeneratedByName = r.GeneratedByName,
            FileName = r.FileName,
            SizeBytes = r.SizeBytes,
            CreatedAt = r.CreatedAt,
        }).ToList());
    }

    public async Task<LetterFileResult> DownloadAsync(string id)
    {
        if (await SavedLettersDeniedAsync() is { } denied) return LetterFileResult.Denied(denied);

        var doc = await _generated.GetByIdAsync(id);
        if (doc is null) return LetterFileResult.NotFound();

        var content = await _storage.ReadAsync(doc.OrganizationId, doc.EmployeeUserId, doc.StoredFileName);
        return content is null
            ? LetterFileResult.Refused("The file for this letter is missing from storage.")
            : LetterFileResult.File(doc.FileName, content);
    }

    public async Task<LetterDeleteResult> DeleteAsync(string id)
    {
        if (await SavedLettersDeniedAsync() is { } denied) return LetterDeleteResult.Denied(denied);

        var doc = await _generated.GetByIdAsync(id);
        if (doc is null) return LetterDeleteResult.NotFound;

        await _generated.DeleteAsync(doc);
        _storage.Delete(doc.OrganizationId, doc.EmployeeUserId, doc.StoredFileName);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.DocumentsLetterDelete,
            $"Deleted letter \"{doc.TemplateName}\" ({doc.CreatedAt:yyyy-MM-dd}) from an employee's file",
            TargetType: "GeneratedDocument",
            TargetId: doc.Id,
            Metadata: new { doc.EmployeeUserId, doc.TemplateName, doc.FileName }));
        return LetterDeleteResult.Deleted;
    }

    // ─── Records → values ────────────────────────────────────────────────

    private sealed record EmployeeContext(
        User User, OrganizationMembership Membership, EmployeeProfile? Profile, DateTime? LastLeaveDate);

    private sealed record CompanyContext(string Name, PayrollCompanyInfo? Info);

    // Null when the person isn't a member of this company — or is outside a
    // policy-limited admin's scope, which reads exactly the same.
    private async Task<EmployeeContext?> LoadEmployeeAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || !_scope.Contains(userId)) return null;

        var membership = await _memberships.GetForUserInCurrentOrgAsync(userId);
        if (membership is null) return null;

        var user = await _users.GetByIdAsync(userId);
        if (user is null) return null;

        var profile = await _profiles.GetByUserAsync(userId);

        // Someone who left and was moved on keeps their last leave date only
        // in the history — the newest closed period. Not someone REHIRED who
        // works here now (an open period): their previous stint's last day
        // isn't this employment's leave date, and printing it would put a
        // resignation before the join date. For them it is missing, so the
        // admin is asked.
        DateTime? lastLeave = null;
        if (profile?.LeaveDate is null)
        {
            var periods = await _periods.GetForUserAsync(userId);
            var currentlyEmployed = periods.Any(p => p.LeaveDate is null);
            var joined = profile?.JoinDate ?? membership.JoinDate;
            if (!currentlyEmployed
                && periods.FirstOrDefault(p => p.LeaveDate is not null)?.LeaveDate is { } last
                && (joined is null || last.Date >= joined.Value.Date))
                lastLeave = last;
        }

        return new EmployeeContext(user, membership, profile, lastLeave);
    }

    private async Task<CompanyContext> LoadCompanyAsync()
    {
        var info = await _companyInfo.GetAsync();
        var organization = _currentUser.OrganizationId is { } orgId
            ? await _organizations.GetByIdAsync(orgId)
            : null;
        var name = !string.IsNullOrWhiteSpace(info?.EmployerName) ? info!.EmployerName! : organization?.Name ?? "";
        return new CompanyContext(name, info);
    }

    // Every registry field this employee and company can answer: the text it
    // prints as, and the text an input starts from. (null, null) = no value.
    // Employee may be null (a preview without one): only company, signatory
    // and the date are filled then.
    private static Dictionary<string, (string? Display, string? Edit)> RecordValues(
        EmployeeContext? e, CompanyContext c)
    {
        var v = new Dictionary<string, (string? Display, string? Edit)>(StringComparer.Ordinal);

        void Text(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) v[key] = (value.Trim(), value.Trim());
        }

        void Date(string key, DateTime? value)
        {
            if (value is { } d) v[key] = (MergeFields.FormatDate(d), d.ToString("yyyy-MM-dd", Invariant));
        }

        if (e is not null)
        {
            var p = e.Profile;
            var m = e.Membership;

            Text("employee.name", e.User.Name);
            Text("employee.employeeNumber", m.EmployeeNumber);
            Text("employee.idNumber", p?.IdNumber);
            Text("employee.address", JoinLines(
                p?.AddressLine1, p?.AddressLine2, $"{p?.Postcode} {p?.City}".Trim(), p?.State));
            Text("employee.email", e.User.Email);
            Text("employee.jobTitle", m.JobTitle);
            Text("employee.department", p?.Department);
            Text("employee.location", p?.Location);
            Text("employee.employmentStatus", p?.EmploymentStatus is { } status ? StatusLabel(status) : null);
            Date("employee.joinDate", p?.JoinDate ?? m.JoinDate);
            Date("employee.leaveDate", p?.LeaveDate ?? e.LastLeaveDate);
            if (p?.ProbationMonths is { } months)
                v["employee.probationMonths"] = (months.ToString(Invariant), months.ToString(Invariant));
            Date("employee.confirmationDate", p?.ConfirmationDate);
            if (p?.MonthlySalary is > 0m and { } salary)
                v["employee.monthlySalary"] = (MergeFields.FormatMoney(salary), salary.ToString("0.00", Invariant));
        }

        var info = c.Info;
        Text("company.name", c.Name);
        Text("company.registrationNo", info?.RegistrationNo);
        Text("company.address", CompanyAddress(info, multiline: true));
        Text("company.phone", !string.IsNullOrWhiteSpace(info?.Phone) ? info!.Phone : info?.Handphone);
        Text("company.email", info?.Email);
        Text("signatory.name", info?.DeclarantName);
        Text("signatory.position", info?.DeclarantPosition);
        Date("today", TodayInMalaysia());

        return v;
    }

    private static string? JoinLines(params string?[] parts)
    {
        var lines = parts.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList();
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private static string? CompanyAddress(PayrollCompanyInfo? info, bool multiline)
    {
        if (info is null) return null;
        var parts = new[]
        {
            info.AddressLine1, info.AddressLine2, $"{info.Postcode} {info.City}".Trim(), info.State,
        }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList();
        return parts.Count == 0 ? null : string.Join(multiline ? "\n" : ", ", parts);
    }

    private static string StatusLabel(EmploymentStatus status) => status switch
    {
        EmploymentStatus.MANAGEMENT => "Management",
        EmploymentStatus.PERMANENT => "Permanent",
        EmploymentStatus.CONTRACT => "Contract",
        EmploymentStatus.PART_TIME => "Part-time",
        EmploymentStatus.INDUSTRIAL_TRAINEE => "Industrial trainee",
        _ => "Other",
    };

    // ─── Rendering ───────────────────────────────────────────────────────

    private static byte[] Render(
        string title, string body, IReadOnlyDictionary<string, string> values, CompanyContext company, string? watermark)
    {
        string Value(string key) => values.TryGetValue(key, out var s) ? s : "";

        var blocks = LetterMarkup.Parse(body, Value);
        var info = company.Info;

        return LetterPdf.Render(new LetterPdfModel(
            Title: title,
            CompanyName: values.TryGetValue("company.name", out var cn) && cn.Length > 0 ? cn : company.Name,
            RegistrationNo: info?.RegistrationNo,
            CompanyAddressLine: CompanyAddress(info, multiline: false),
            CompanyPhone: !string.IsNullOrWhiteSpace(info?.Phone) ? info!.Phone : info?.Handphone,
            CompanyEmail: info?.Email,
            DateText: Value("today"),
            Blocks: blocks,
            SignatoryName: Value("signatory.name"),
            SignatoryPosition: Value("signatory.position"),
            Watermark: watermark));
    }

    private static DateTime TodayInMalaysia() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Myt).Date;

    // Download names come from template and employee names — strip anything a
    // filesystem or a Content-Disposition header would choke on.
    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['"', '\\', '/', ':', '*', '?', '<', '>', '|']).ToHashSet();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '-' : ch).ToArray()).Trim();
        if (cleaned.Length > 200) cleaned = cleaned[..196].TrimEnd() + ".pdf";
        return cleaned.Length == 0 ? "letter.pdf" : cleaned;
    }
}
