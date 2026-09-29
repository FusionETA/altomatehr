using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies.Entities;   // SalaryType

namespace AltomateHR.Api.Modules.Payroll;

// See SalaryAdjustmentImportSheet. The rules a row follows are the ones the
// employee screen's salary prompt follows, so the two cannot disagree:
//   • the amount is the person's own basis — monthly salary or hourly rate;
//   • a first salary (nothing, or RM 0, before) is set but not recorded;
//   • a real change is recorded with its effective date and reason;
//   • an effective date cannot be in the future: v2 has ONE current salary,
//     applied straight away, so a raise dated next month would already be
//     paid in this month's run.
public class SalaryAdjustmentImportService : ISalaryAdjustmentImportService
{
    // "Today" is Malaysia's today — a raise effective on the 1st, uploaded at
    // 7am on the 1st, is not in the future just because UTC is still on the 31st.
    private static readonly TimeZoneInfo Myt = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

    private readonly IDirectoryService _directory;
    private readonly IEmployeeProfileRepository _profiles;
    private readonly ISalaryChangeService _salaryChanges;
    private readonly IPayrollDraftStaleness? _drafts;
    private readonly Func<DateTime> _today;

    public SalaryAdjustmentImportService(
        IDirectoryService directory,
        IEmployeeProfileRepository profiles,
        ISalaryChangeService salaryChanges,
        IPayrollDraftStaleness? drafts = null,
        Func<DateTime>? today = null)
    {
        _directory = directory;
        _profiles = profiles;
        _salaryChanges = salaryChanges;
        _drafts = drafts;
        _today = today ?? (() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Myt).Date);
    }

    public async Task<TabularExportResult> BuildTemplateAsync()
    {
        var staff = await StaffAsync();
        var seed = staff
            .Where(s => !s.Profile.IsArchived)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new SalaryAdjustmentImportSheet.SeedRow(
                s.EmployeeNumber, s.Email, s.Name, s.Profile.SalaryType.ToString(), CurrentSalary(s.Profile)));

        return TabularExportResult.From(
            [SalaryAdjustmentImportSheet.Build(seed), SalaryAdjustmentImportSheet.Guide()],
            TabularFormat.Xlsx,
            $"salary-adjustments-{_today():yyyy-MM-dd}");
    }

    public async Task<SalaryAdjustmentImportResult> ImportAsync(byte[] content, TabularFormat format)
    {
        IReadOnlyList<IReadOnlyList<string>> rows;
        try
        {
            // The data sheet by name; a CSV (or a renamed tab) is its first sheet.
            var sheets = TabularReader.ReadAllSheets(content, format);
            rows = (sheets.FirstOrDefault(s => string.Equals(
                        s.Name.Trim(), SalaryAdjustmentImportSheet.SheetName, StringComparison.OrdinalIgnoreCase))
                    ?? sheets.FirstOrDefault())?.Rows ?? [];
        }
        catch (InvalidDataException ex)
        {
            return SalaryAdjustmentImportResult.Fail(ex.Message);
        }

        if (rows.Count == 0) return SalaryAdjustmentImportResult.Fail("The file is empty.");

        var (map, missing) = TabularHeaderMap.Build(
            rows[0], SalaryAdjustmentImportSheet.Columns, SalaryAdjustmentImportSheet.IdentifiedBy);
        if (map is null)
            return SalaryAdjustmentImportResult.Fail($"Missing column(s): {string.Join(", ", missing)}.");

        var staff = await StaffAsync();
        var byNumber = staff
            .Where(s => !string.IsNullOrWhiteSpace(s.EmployeeNumber))
            .GroupBy(s => s.EmployeeNumber!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var byEmail = staff.ToDictionary(s => s.Email.Trim(), StringComparer.OrdinalIgnoreCase);
        var today = _today();

        var errors = new List<TabularImportError>();
        var parsed = new List<(Staff Staff, decimal NewSalary, DateTime Effective, SalaryChangeReason Reason, string? Notes, int Row)>();

        for (var i = 1; i < rows.Count; i++)
        {
            var rowNumber = i + 1;   // the header is row 1, as the admin sees it
            var row = rows[i];

            var newSalaryCell = map.Cell(row, SalaryAdjustmentImportSheet.NewSalaryKey);
            if (TabularCell.IsBlank(newSalaryCell)) continue;   // pay not changing for this person

            var who = Resolve(map, row, byNumber, byEmail, out var whoError);
            if (who is null)
            {
                errors.Add(new TabularImportError(rowNumber, whoError!));
                continue;
            }

            var problems = new List<string>();

            var newSalary = TabularCell.Money(newSalaryCell);
            if (newSalary is null or <= 0m) problems.Add("New Salary must be an amount above 0");

            var effective = today;
            var dateCell = map.Cell(row, SalaryAdjustmentImportSheet.EffectiveDateKey);
            if (!TabularCell.IsBlank(dateCell))
            {
                if (TabularCell.Date(dateCell) is { } d) effective = d.Date;
                else problems.Add($"Effective Date \"{dateCell.Trim()}\" isn't a date (use YYYY-MM-DD)");
            }
            if (effective > today)
            {
                problems.Add($"Effective Date {effective:d MMM yyyy} is in the future — the new salary "
                    + "applies straight away, so upload it on or after that date");
            }

            var reason = SalaryChangeReason.RAISE;
            var reasonCell = map.Cell(row, SalaryAdjustmentImportSheet.ReasonKey);
            if (!TabularCell.IsBlank(reasonCell))
            {
                if (TabularCell.Enum<SalaryChangeReason>(reasonCell) is { } r) reason = r;
                else problems.Add($"Reason \"{reasonCell.Trim()}\" isn't one of "
                    + string.Join(", ", Enum.GetNames<SalaryChangeReason>()));
            }

            if (problems.Count > 0)
            {
                errors.Add(new TabularImportError(rowNumber, $"{who.Name}: {string.Join("; ", problems)}."));
                continue;
            }

            parsed.Add((who, newSalary!.Value, effective, reason,
                TabularCell.Text(map.Cell(row, SalaryAdjustmentImportSheet.NotesKey), 500), rowNumber));
        }

        // One person, one new salary: two rows for the same employee would
        // leave the history saying whichever happened to be read last.
        foreach (var dup in parsed.GroupBy(p => p.Staff.Profile.Id).Where(g => g.Count() > 1))
        {
            errors.Add(new TabularImportError(
                dup.Skip(1).First().Row,
                $"{dup.First().Staff.Name} appears more than once (rows {string.Join(", ", dup.Select(d => d.Row))})."));
        }

        if (errors.Count > 0)
            return new SalaryAdjustmentImportResult { Ok = false, Errors = [.. errors.OrderBy(e => e.Row)] };

        int changed = 0, first = 0, unchanged = 0;
        foreach (var p in parsed)
        {
            var profile = p.Staff.Profile;
            var current = CurrentSalary(profile);
            if (current == p.NewSalary)
            {
                unchanged++;
                continue;
            }

            var before = new EmployeeProfile
            {
                Id = profile.Id,
                SalaryType = profile.SalaryType,
                MonthlySalary = profile.MonthlySalary,
                HourlyRate = profile.HourlyRate,
            };

            if (profile.SalaryType == SalaryType.HOURLY) profile.HourlyRate = p.NewSalary;
            else profile.MonthlySalary = p.NewSalary;
            profile.UpdatedAt = DateTime.UtcNow;
            await _profiles.UpdateAsync(profile);

            if (current is null or <= 0m)
            {
                first++;   // filling in an empty salary is not a change to anyone's pay
                continue;
            }

            await _salaryChanges.RecordAsync(before, profile, new RecordSalaryChangeDto
            {
                EffectiveDate = p.Effective,
                Reason = p.Reason,
                Notes = p.Notes,
            });
            changed++;
        }

        // Every draft was built on the old figures.
        if (changed + first > 0 && _drafts is not null) await _drafts.MarkAllDraftsAsync();

        return new SalaryAdjustmentImportResult { Ok = true, Changed = changed, FirstSalaries = first, Unchanged = unchanged };
    }

    // ─── Who ────────────────────────────────────────────────────────────

    private sealed record Staff(EmployeeProfile Profile, string Name, string Email, string? EmployeeNumber);

    // Employees and supervisors with a payroll profile — the people payroll pays.
    private async Task<List<Staff>> StaffAsync()
    {
        var profiles = await _directory.PayrollProfilesAsync();
        var users = (await _directory.GetUsersAsync()).ToDictionary(u => u.Id, StringComparer.Ordinal);
        var members = (await _directory.GetMembershipsForCurrentOrgAsync())
            .ToDictionary(m => m.UserId, StringComparer.Ordinal);

        return
        [
            .. profiles
                .Where(p => users.ContainsKey(p.UserId))
                .Select(p => new Staff(
                    p,
                    users[p.UserId].Name,
                    users[p.UserId].Email,
                    members.GetValueOrDefault(p.UserId)?.EmployeeNumber)),
        ];
    }

    private static Staff? Resolve(
        TabularHeaderMap map,
        IReadOnlyList<string> row,
        Dictionary<string, List<Staff>> byNumber,
        Dictionary<string, Staff> byEmail,
        out string? error)
    {
        error = null;
        var number = TabularCell.Text(map.Cell(row, SalaryAdjustmentImportSheet.EmployeeNumberKey));
        var email = TabularCell.Text(map.Cell(row, SalaryAdjustmentImportSheet.EmailKey));

        Staff? byNo = null;
        if (number is not null)
        {
            if (!byNumber.TryGetValue(number, out var matches))
            {
                error = $"No payroll employee has Employee No \"{number}\".";
                return null;
            }
            if (matches.Count > 1)
            {
                error = $"Employee No \"{number}\" belongs to {matches.Count} people — fix the duplicate first.";
                return null;
            }
            byNo = matches[0];
        }

        Staff? byMail = null;
        if (email is not null && !byEmail.TryGetValue(email, out byMail) && byNo is null)
        {
            error = $"No payroll employee has the email \"{email}\".";
            return null;
        }

        // Both given and naming different people: which one was meant is the
        // admin's call, not a guess.
        if (byNo is not null && byMail is not null && byNo.Profile.Id != byMail.Profile.Id)
        {
            error = $"Employee No \"{number}\" is {byNo.Name}, but the email \"{email}\" is {byMail.Name}.";
            return null;
        }

        var who = byNo ?? byMail;
        if (who is null) error = "Give an Employee No or an Email.";
        else if (who.Profile.IsArchived)
        {
            error = $"{who.Name} is archived.";
            return null;
        }
        return who;
    }

    private static decimal? CurrentSalary(EmployeeProfile p) =>
        p.SalaryType == SalaryType.HOURLY ? p.HourlyRate : p.MonthlySalary;
}
