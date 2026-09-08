using System.Text.Json;
using System.Text.Json.Serialization;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.LhdnForms.Dtos;
using AltomateHR.Api.Modules.LhdnForms.Pdf;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Policies.Entities;   // SalaryType

namespace AltomateHR.Api.Modules.LhdnForms;

// Assembles the per-employee LHDN form payload and dispatches to the matching
// PDF renderer. Reads other modules only through their public service
// surfaces (IEmployeeProfileService, IDirectoryService, IOrganizationService)
// — never their repositories — per this codebase's module-isolation rule.
public class LhdnFormsService : ILhdnFormsService
{
    private readonly IEmployeeProfileService _profiles;
    private readonly IDirectoryService _directory;
    private readonly IOrganizationService _organizations;
    private readonly ICurrentUser _currentUser;

    public LhdnFormsService(
        IEmployeeProfileService profiles,
        IDirectoryService directory,
        IOrganizationService organizations,
        ICurrentUser currentUser)
    {
        _profiles = profiles;
        _directory = directory;
        _organizations = organizations;
        _currentUser = currentUser;
    }

    public async Task<IEnumerable<LhdnFormDescriptorDto>?> GetDescriptorsAsync(string userId)
    {
        var profile = await _profiles.GetAsync(userId);
        if (profile is null) return null;

        var today = DateTime.UtcNow;
        return LhdnFormMeta.All.Values.Select(meta =>
        {
            var available = LhdnFormMeta.IsAvailable(meta.Kind, profile.IsArchived);
            var dto = new LhdnFormDescriptorDto
            {
                Kind = meta.Kind.ToString(),
                Code = meta.Code,
                Title = meta.Title,
                Description = meta.Description,
                NeedsYearPicker = meta.NeedsYearPicker,
                Enabled = available,
                DisabledReason = available ? null : meta.Requires switch
                {
                    LhdnFormAvailability.ArchivedOnly => "Available after archiving",
                    LhdnFormAvailability.ActiveOnly => "Available only for active employees",
                    _ => null,
                },
            };
            if (meta.Kind == LhdnFormKind.CP22 && available)
            {
                var badge = LhdnFormMeta.Cp22DeadlineBadge(profile.JoinDate, today);
                if (badge is { } b)
                {
                    dto.Badge = b.Text;
                    dto.BadgeVariant = b.Variant;
                }
            }
            return dto;
        }).ToList();
    }

    public async Task<(bool Ok, byte[]? Bytes, string? FileName, string? Error)> GenerateAsync(
        string userId, LhdnFormKind kind, int? year)
    {
        var profile = await _profiles.GetAsync(userId);
        if (profile is null) return (false, null, null, null);

        if (!LhdnFormMeta.IsAvailable(kind, profile.IsArchived))
        {
            var reason = LhdnFormMeta.All[kind].Requires == LhdnFormAvailability.ArchivedOnly
                ? "Archive the employee first (with a last-working-day date) before generating this form."
                : "This form is only available for active employees.";
            return (false, null, null, reason);
        }

        var resolvedYear = year ?? DateTime.UtcNow.Year;
        var payload = await BuildPayloadAsync(userId, profile, resolvedYear);
        var bytes = kind switch
        {
            LhdnFormKind.PCB2II => Pcb2IiPdf.Render(payload),
            LhdnFormKind.CP22 => Cp22Pdf.Render(payload),
            LhdnFormKind.CP22A => Cp22APdf.Render(payload),
            LhdnFormKind.CP21 => Cp21Pdf.Render(payload),
            LhdnFormKind.TP3 => Tp3Pdf.Render(payload),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var fileName = LhdnFormMeta.FileName(kind, payload.Employee.EmployeeCode, resolvedYear);
        return (true, bytes, fileName, null);
    }

    private async Task<LhdnFormPayload> BuildPayloadAsync(
        string userId, EmployeeProfileDto profile, int year)
    {
        var membership = await _directory.GetMembershipForUserAsync(userId);
        var org = _currentUser.OrganizationId is { } orgId ? await _organizations.GetByIdAsync(orgId) : null;

        var (qualifyingChildren, annualChildRelief) = ComputeChildRelief(profile.ChildReliefJson);

        return new LhdnFormPayload
        {
            OrganizationName = org?.Name ?? "",
            // No company-level statutory config (tax number, address, declarant)
            // exists in this rebuild yet — every field renders as "—" until it does.
            Employer = new LhdnFormEmployer(),
            Employee = new LhdnFormEmployee
            {
                Name = profile.Name,
                EmployeeCode = FirstNonBlank(membership?.EmployeeNumber, profile.Name, userId),
                JobTitle = membership?.JobTitle,
                IdNumber = profile.IdNumber,
                IdType = profile.IdType?.ToString(),
                IncomeTaxNumber = profile.IncomeTaxNumber,
                Nationality = profile.Nationality,
                Gender = profile.Gender?.ToString(),
                DateOfBirth = profile.DateOfBirth,
                MaritalStatus = profile.MaritalStatus?.ToString(),
                Phone = profile.Phone,
                AlternateEmail = profile.AlternateEmail,
                Email = profile.Email,
                AddressLine1 = profile.AddressLine1,
                AddressLine2 = profile.AddressLine2,
                City = profile.City,
                Postcode = profile.Postcode,
                State = profile.State,
                JoinDate = profile.JoinDate,
                LeaveDate = profile.LeaveDate,
                IsArchived = profile.IsArchived,
                ArchiveReason = profile.ArchiveReason,
                SpouseIdNumber = profile.SpouseIdNumber,
                SpousePcbNumber = profile.SpousePcbNumber,
                MonthlySalary = profile.SalaryType == SalaryType.MONTHLY ? profile.MonthlySalary : null,
                PcbBorneByEmployer = profile.PcbBorneByEmployer,
                FixedAllowancesTotal = SumAllowances(profile.FixedAllowancesJson),
                QualifyingChildren = qualifyingChildren,
                AnnualChildRelief = annualChildRelief,
                PrevEmploymentYear = profile.PrevEmploymentYear,
                PrevRemuneration = profile.PrevRemuneration,
                PrevEpf = profile.PrevEpf,
            },
            PerMonth = new LhdnFormMonthPcb?[12], // no payroll-run engine yet — every month unknown
            Year = year,
            Ytd = new LhdnFormYtd(),               // same reason — every YTD figure unknown
            HasPayrollHistory = false,
            GeneratedAt = DateTime.UtcNow,
        };
    }

    private static string FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "employee";

    // ---- Child relief (JSON column written by the frontend, camelCase) ----

    private class ChildReliefRecord
    {
        [JsonPropertyName("abilityStatus")] public string? AbilityStatus { get; set; }
        [JsonPropertyName("currentlyStudying")] public string? CurrentlyStudying { get; set; }
        [JsonPropertyName("pcbDeduction")] public string? PcbDeduction { get; set; }
    }

    /// Mirrors the frontend's isAdultChild/relief tables — LHDN Public Ruling
    /// 5/2019 §7.3: RM 2,000 for a non-disabled UNDER_18/PRE_UNIVERSITY child,
    /// RM 8,000 for a disabled child or one in diploma-or-above education
    /// (either alone), RM 16,000 when both apply. Half when pcbDeduction is
    /// "HALF" (split with the spouse); excluded entirely when "NONE".
    private static (int Count, decimal Total) ComputeChildRelief(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (0, 0);
        List<ChildReliefRecord>? children;
        try { children = JsonSerializer.Deserialize<List<ChildReliefRecord>>(json); }
        catch (JsonException) { return (0, 0); }
        if (children is null) return (0, 0);

        var count = 0;
        decimal total = 0;
        foreach (var child in children)
        {
            if (child.PcbDeduction == "NONE") continue;
            count++;

            var isDisabled = child.AbilityStatus == "DISABLED";
            var isHigherEd = child.CurrentlyStudying is "DIPLOMA_MALAYSIA" or "DEGREE_ABROAD" or "HIGHER_ED";
            decimal amount = (isDisabled, isHigherEd) switch
            {
                (true, true) => 16000,
                (true, false) => 8000,
                (false, true) => 8000,
                (false, false) => 2000,
            };
            total += child.PcbDeduction == "HALF" ? Math.Round(amount / 2, 2) : amount;
        }
        return (count, total);
    }

    // ---- Fixed allowances (JSON column written by the frontend, camelCase) ----

    private class FixedAllowanceRecord
    {
        [JsonPropertyName("category")] public string? Category { get; set; }
        [JsonPropertyName("amount")] public decimal? Amount { get; set; }
    }

    /// Sum of recurring positive cash allowances — mirrors the frontend's
    /// sumAllowances: a "deduct_"-prefixed category never counts, matching
    /// how the category code itself is the kind marker (see
    /// frontend/src/features/employees/lib/payroll-adjustments.ts).
    private static decimal? SumAllowances(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        List<FixedAllowanceRecord>? items;
        try { items = JsonSerializer.Deserialize<List<FixedAllowanceRecord>>(json); }
        catch (JsonException) { return null; }
        if (items is not { Count: > 0 }) return null;

        decimal total = 0;
        foreach (var item in items)
        {
            if (item.Category is { } category && !category.StartsWith("deduct_") && item.Amount is > 0)
                total += item.Amount.Value;
        }
        return Math.Round(total, 2);
    }
}
