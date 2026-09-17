using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Policies.Entities;   // SalaryType
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class PayrollAdjustmentImportService : IPayrollAdjustmentImportService
{
    private readonly IPayrollRunRepository _runs;
    private readonly IPayrollRunMemberRepository _members;
    private readonly IPayrollRunAdjustmentService _adjustments;
    private readonly IPayrollRunAdjustmentRepository _adjustmentRows;
    private readonly IEmployeeProfileRepository _profiles;
    private readonly IDirectoryService _directory;
    private readonly ISalaryChangeService _salaryChanges;

    public PayrollAdjustmentImportService(
        IPayrollRunRepository runs,
        IPayrollRunMemberRepository members,
        IPayrollRunAdjustmentService adjustments,
        IPayrollRunAdjustmentRepository adjustmentRows,
        IEmployeeProfileRepository profiles,
        IDirectoryService directory,
        ISalaryChangeService salaryChanges)
    {
        _runs = runs;
        _members = members;
        _adjustments = adjustments;
        _adjustmentRows = adjustmentRows;
        _profiles = profiles;
        _directory = directory;
        _salaryChanges = salaryChanges;
    }

    // ─── Template ───────────────────────────────────────────────────────

    public async Task<TabularExportResult?> BuildTemplateAsync(string runId)
    {
        var run = await _runs.GetByIdAsync(runId);
        if (run is null) return null;

        var scope = await ScopeAsync(run);
        var names = await NamesAsync(scope);
        var existing = (await _adjustmentRows.GetForRunAsync(runId))
            .ToDictionary(a => a.EmployeeProfileId, StringComparer.Ordinal);

        var seed = new List<AdjustmentImportSeedRow>();
        foreach (var profile in scope.OrderBy(p => names[p.Id], StringComparer.OrdinalIgnoreCase))
        {
            var name = names[profile.Id];
            var lines = existing.TryGetValue(profile.Id, out var row)
                ? PayrollRunAdjustments.ParseManualLineItems(row.ManualLineItemsJson)
                : [];

            if (lines.Count == 0)
            {
                seed.Add(new AdjustmentImportSeedRow(name, profile.MonthlySalary));
                continue;
            }

            // One spreadsheet row per existing line, so replacing them is an
            // edit rather than a rewrite from memory.
            foreach (var line in lines)
            {
                seed.Add(new AdjustmentImportSeedRow(
                    name, profile.MonthlySalary, line.Category, line.Label, line.Amount,
                    line.TreatAsRecurring));
            }
        }

        var sheets = new List<TabularSheet>
        {
            PayrollAdjustmentImportSheet.BuildTemplate(seed),
            PayrollAdjustmentImportSheet.BuildCategories(),
        };

        return TabularExportResult.From(
            sheets, TabularFormat.Xlsx,
            $"payroll-adjustments-{run.PeriodYear}-{run.PeriodMonth:D2}");
    }

    // ─── Import ─────────────────────────────────────────────────────────

    public async Task<PayrollAdjustmentImportResult> ImportAsync(
        string runId, byte[] content, TabularFormat format)
    {
        var run = await _runs.GetByIdAsync(runId);
        if (run is null) return new PayrollAdjustmentImportResult { Ok = false, Found = false };

        // Only a DRAFT accepts adjustments at all — the same gate SaveAsync
        // applies, checked here so the whole file is refused up front rather
        // than row by row.
        if (run.Status != PayrollRunStatus.DRAFT)
        {
            return Fail("This run is no longer a draft, so its adjustments are locked.");
        }

        IReadOnlyList<IReadOnlyList<string>> rows;
        try
        {
            rows = TabularReader.Read(content, format);
        }
        catch (InvalidDataException ex)
        {
            return Fail(ex.Message);
        }

        if (rows.Count == 0) return Fail("The file is empty.");

        var (map, missing) = TabularHeaderMap.Build(rows[0], PayrollAdjustmentImportSheet.Columns);
        if (map is null)
            return Fail($"Missing required column(s): {string.Join(", ", missing)}.");
        if (rows.Count == 1) return Fail("The file has a header row but no data rows.");

        var scope = await ScopeAsync(run);
        var byName = BuildNameIndex(scope, await NamesAsync(scope));

        var errors = new List<TabularImportError>();
        var parsed = new List<(ParsedAdjustmentRow Row, EmployeeProfile Profile)>();

        for (var i = 1; i < rows.Count; i++)
        {
            var rowNumber = i + 1;   // header is row 1, as the admin sees it
            var row = rows[i];

            var name = TabularCell.Text(map.Cell(row, PayrollAdjustmentImportSheet.NameKey));
            if (name is null) continue;   // a blank spacer line, not an error

            // Unknown or ambiguous names fail the FILE, not the row: the admin
            // has to say who they meant, and half-importing the rest would
            // leave the run in a state nobody asked for.
            if (!byName.TryGetValue(name, out var matches))
            {
                errors.Add(new TabularImportError(
                    rowNumber, $"\"{name}\" is not a payable employee on this run."));
                continue;
            }

            if (matches.Count > 1)
            {
                errors.Add(new TabularImportError(
                    rowNumber,
                    $"\"{name}\" matches {matches.Count} employees. Rename one, or use the editor."));
                continue;
            }

            var parsedRow = ParseRow(row, map, rowNumber, errors);
            if (parsedRow is not null) parsed.Add((parsedRow, matches[0]));
        }

        if (errors.Count > 0)
        {
            return new PayrollAdjustmentImportResult { Ok = false, Errors = errors };
        }

        return await ApplyAsync(run, scope, parsed);
    }

    // ─── Parsing ────────────────────────────────────────────────────────

    private static ParsedAdjustmentRow? ParseRow(
        IReadOnlyList<string> row, TabularHeaderMap map, int rowNumber, List<TabularImportError> errors)
    {
        var name = TabularCell.Text(map.Cell(row, PayrollAdjustmentImportSheet.NameKey))!;
        var category = TabularCell.Text(map.Cell(row, PayrollAdjustmentImportSheet.CategoryKey));
        var amountCell = map.Cell(row, PayrollAdjustmentImportSheet.AmountKey);
        var newSalaryCell = map.Cell(row, PayrollAdjustmentImportSheet.NewSalaryKey);

        decimal amount = 0m;
        if (category is not null)
        {
            if (PayrollAdjustmentCategories.Find(category) is null)
            {
                errors.Add(new TabularImportError(
                    rowNumber, $"Unknown category \"{category}\". See the Categories sheet."));
                return null;
            }

            var parsedAmount = TabularCell.Money(amountCell);
            if (parsedAmount is null)
            {
                errors.Add(new TabularImportError(
                    rowNumber, "A category needs an amount."));
                return null;
            }

            // Negative is refused rather than flipped, matching ManualLineItemDto:
            // the category already decides the direction, so a negative deduction
            // would quietly ADD to someone's pay.
            if (parsedAmount < 0)
            {
                errors.Add(new TabularImportError(rowNumber, "Amount cannot be negative."));
                return null;
            }

            amount = parsedAmount.Value;
        }
        else if (!TabularCell.IsBlank(amountCell))
        {
            errors.Add(new TabularImportError(rowNumber, "An amount needs a category."));
            return null;
        }

        decimal? newSalary = null;
        if (!TabularCell.IsBlank(newSalaryCell))
        {
            newSalary = TabularCell.Money(newSalaryCell);
            if (newSalary is null or < 0)
            {
                errors.Add(new TabularImportError(
                    rowNumber, "New Basic Salary must be a positive amount, or blank to leave it alone."));
                return null;
            }
        }

        var reasonCell = map.Cell(row, PayrollAdjustmentImportSheet.ReasonKey);
        var reason = SalaryChangeReason.RAISE;
        if (!TabularCell.IsBlank(reasonCell))
        {
            var parsedReason = TabularCell.Enum<SalaryChangeReason>(reasonCell);
            if (parsedReason is null)
            {
                errors.Add(new TabularImportError(
                    rowNumber,
                    $"Unknown salary reason \"{reasonCell.Trim()}\". Use one of: "
                    + string.Join(", ", Enum.GetNames<SalaryChangeReason>()) + "."));
                return null;
            }
            reason = parsedReason.Value;
        }

        return new ParsedAdjustmentRow(
            rowNumber,
            name,
            category,
            TabularCell.Text(map.Cell(row, PayrollAdjustmentImportSheet.LabelKey), 200),
            amount,
            Flag(map.Cell(row, PayrollAdjustmentImportSheet.RecurringKey)),
            newSalary,
            reason,
            TabularCell.Date(map.Cell(row, PayrollAdjustmentImportSheet.EffectiveKey)),
            TabularCell.Text(map.Cell(row, PayrollAdjustmentImportSheet.SalaryNotesKey), 500));
    }

    // ─── Applying ───────────────────────────────────────────────────────

    private async Task<PayrollAdjustmentImportResult> ApplyAsync(
        PayrollRun run,
        IReadOnlyList<EmployeeProfile> scope,
        List<(ParsedAdjustmentRow Row, EmployeeProfile Profile)> parsed)
    {
        // Two employees cannot disagree about their own new salary.
        var conflicting = parsed
            .Where(p => p.Row.HasSalaryChange)
            .GroupBy(p => p.Profile.Id, StringComparer.Ordinal)
            .Where(g => g.Select(p => p.Row.NewSalary).Distinct().Count() > 1)
            .Select(g => g.First().Row.Name)
            .ToList();

        if (conflicting.Count > 0)
        {
            return Fail(
                $"Two different New Basic Salary values for: {string.Join(", ", conflicting)}. "
                + "Use one value per employee.");
        }

        var linesByProfile = parsed
            .Where(p => p.Row.HasLine)
            .GroupBy(p => p.Profile.Id, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => new ManualLineItemDto
                {
                    Category = p.Row.Category!,
                    Label = p.Row.Label,
                    Amount = p.Row.Amount,
                    TreatAsRecurring = p.Row.TreatAsRecurring,
                }).ToList(),
                StringComparer.Ordinal);

        var linesWritten = 0;
        var cleared = 0;

        // REPLACE, across the whole run — see the warning on the sheet. Saving
        // through the adjustment service rather than the repository keeps the
        // category validation and the stale-marking in one place.
        foreach (var profile in scope)
        {
            var existing = await _adjustments.GetAsync(run.Id, profile.Id);
            linesByProfile.TryGetValue(profile.Id, out var lines);

            if (lines is null)
            {
                // Nothing in the file for them. Only worth a write if they had
                // manual lines that now have to go.
                if (existing is null || existing.ManualLineItems.Count == 0) continue;
                cleared++;
            }

            var save = new SavePayrollRunAdjustmentDto
            {
                // Everything except the manual lines is carried over untouched:
                // the importer owns one column of this row, not the whole thing.
                OtNormalHours = existing?.OtNormalHours ?? 0m,
                OtRestHours = existing?.OtRestHours ?? 0m,
                OtPublicHours = existing?.OtPublicHours ?? 0m,
                FixedAllowanceOverrides = existing?.FixedAllowanceOverrides ?? [],
                WorkedHours = existing?.WorkedHours,
                ExpectedHours = existing?.ExpectedHours,
                Notes = existing?.Notes,
                ManualLineItems = lines ?? [],
            };

            var result = await _adjustments.SaveAsync(run.Id, profile.Id, save);
            if (!result.Ok) return Fail(result.Error ?? "Could not save an adjustment.");

            linesWritten += lines?.Count ?? 0;
        }

        var salaryApplied = 0;
        var skipped = new List<string>();

        foreach (var (row, profile) in parsed
                     .Where(p => p.Row.HasSalaryChange)
                     .GroupBy(p => p.Profile.Id, StringComparer.Ordinal)
                     .Select(g => g.First()))
        {
            // The column sets a MONTHLY figure. Writing it onto an hourly
            // employee would silently change the basis they are paid on.
            if (profile.SalaryType != SalaryType.MONTHLY)
            {
                skipped.Add(row.Name);
                continue;
            }

            var before = Snapshot(profile);
            profile.MonthlySalary = row.NewSalary;
            profile.UpdatedAt = DateTime.UtcNow;
            await _profiles.UpdateAsync(profile);

            // RecordAsync returns null when nothing actually moved, so typing
            // the current salary back in does not litter the history.
            var recorded = await _salaryChanges.RecordAsync(before, profile, new RecordSalaryChangeDto
            {
                EffectiveDate = row.EffectiveDate ?? new DateTime(run.PeriodYear, run.PeriodMonth, 1),
                Reason = row.Reason,
                Notes = row.SalaryNotes,
            });

            if (recorded is not null) salaryApplied++;
        }

        return new PayrollAdjustmentImportResult
        {
            Ok = true,
            EmployeesAffected = linesByProfile.Count,
            LinesWritten = linesWritten,
            EmployeesCleared = cleared,
            SalaryChangesApplied = salaryApplied,
            SalarySkippedNotMonthly = skipped,
        };
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    // The employees this run can pay. A run created with a member scope keeps
    // it; an older one without falls back to everyone payable, which is what
    // generation does too.
    private async Task<IReadOnlyList<EmployeeProfile>> ScopeAsync(PayrollRun run)
    {
        var payable = (await _directory.GetProfilesForCurrentOrgAsync())
            .Where(p => !p.IsArchived && PayrollProfileReadiness.IsComplete(p))
            .ToList();

        var members = await _members.GetForRunAsync(run.Id);
        if (members.Count == 0) return payable;

        var scoped = members.Select(m => m.EmployeeProfileId).ToHashSet(StringComparer.Ordinal);
        return payable.Where(p => scoped.Contains(p.Id)).ToList();
    }

    // profileId → the person's name, which is what the spreadsheet matches on.
    // Read from the User rather than the profile: the name lives there, and the
    // template and the parser have to agree on it exactly.
    private async Task<Dictionary<string, string>> NamesAsync(IReadOnlyList<EmployeeProfile> scope)
    {
        var users = (await _directory.GetUsersAsync())
            .ToDictionary(u => u.Id, u => u.Name ?? string.Empty, StringComparer.Ordinal);

        return scope.ToDictionary(
            p => p.Id,
            p => users.TryGetValue(p.UserId, out var name) && name.Length > 0 ? name : p.UserId,
            StringComparer.Ordinal);
    }

    // Case-insensitive, and a list rather than a single profile so two people
    // with the same name are caught instead of one silently winning.
    private static Dictionary<string, List<EmployeeProfile>> BuildNameIndex(
        IReadOnlyList<EmployeeProfile> scope, IReadOnlyDictionary<string, string> names)
    {
        var index = new Dictionary<string, List<EmployeeProfile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in scope)
        {
            var name = names[profile.Id];
            if (!index.TryGetValue(name, out var list)) index[name] = list = [];
            list.Add(profile);
        }
        return index;
    }

    // Only the three fields RecordAsync compares — a full clone would invite
    // the copy drifting from the entity.
    private static EmployeeProfile Snapshot(EmployeeProfile p) => new()
    {
        Id = p.Id,
        SalaryType = p.SalaryType,
        MonthlySalary = p.MonthlySalary,
        HourlyRate = p.HourlyRate,
    };

    private static bool Flag(string value) =>
        value.Trim().ToLowerInvariant() is "yes" or "y" or "true" or "1";

    private static PayrollAdjustmentImportResult Fail(string message) =>
        new() { Ok = false, Message = message };
}
