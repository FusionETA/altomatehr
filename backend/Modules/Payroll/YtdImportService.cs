using System.Text;
using AltomateHR.Api.Common;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class YtdImportService : IYtdImportService
{
    private readonly IPayrollRunRepository _runs;
    private readonly IPayslipRepository _payslips;
    private readonly IDirectoryService _directory;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IOrganizationRepository _organizations;

    public YtdImportService(
        IPayrollRunRepository runs,
        IPayslipRepository payslips,
        IDirectoryService directory,
        ICurrentUser currentUser,
        IAuditService audit,
        IOrganizationRepository organizations)
    {
        _runs = runs;
        _payslips = payslips;
        _directory = directory;
        _currentUser = currentUser;
        _audit = audit;
        _organizations = organizations;
    }

    // A sheet pre-filled with the org's roster, so the admin fills figures
    // rather than retyping names the system already knows — and so the names
    // match on the way back in.
    public async Task<TabularExportResult> BuildTemplateAsync(int year, TabularFormat format)
    {
        var (profiles, users) = await RosterAsync();

        // XLSX gets the reference system's own three-sheet workbook, so a file
        // exported there and a file exported here are the same shape. CSV has
        // no sheets or styling to match, so it keeps the flat layout below.
        if (format == TabularFormat.Xlsx)
        {
            var org = _currentUser.OrganizationId is null
                ? null
                : await _organizations.GetByIdAsync(_currentUser.OrganizationId);

            var roster = profiles.Select(p =>
            {
                users.TryGetValue(p.UserId, out var u);
                return new YtdImportTemplateWorkbook.Employee(
                    u?.Name ?? u?.Email ?? p.Id,
                    PersonalIdLabel(p));
            }).ToList();

            return new TabularExportResult(
                YtdImportTemplateWorkbook.Build(year, org?.Name ?? "Your organisation", roster),
                $"ytd-import-template-{year}.xlsx",
                TabularFormat.Xlsx.ContentType());
        }

        // The headers ARE the first row — the parser finds them by text, and
        // keeping the sheet machine-readable means the file it hands out is
        // the same shape as the file it takes back.
        var sheet = new TabularSheet(
            $"YTD {year}",
            YtdImportParser.TemplateHeaders(),
            $"Year-to-date payroll for {year}. Fill each employee's twelve month rows with what "
            + "was ACTUALLY PAID; leave a month blank if there was no payroll. Do not rename the "
            + "columns — an unrecognised heading is reported, not guessed at. You may ADD a "
            + "column for any adjustment category, using its name exactly as the adjustment "
            + "form shows it (for example \"Living Accommodation\" or \"Unpaid Leave "
            + "deduction\") — benefits in kind stay out of gross, and deductions come off "
            + "rather than being added. Employee SKBBK applies from June 2026 only.");

        var columnCount = YtdImportParser.TemplateHeaders().Count;

        // Columns 0 and 1 are Employee Name and Personal ID, both of which the
        // parser DOES read off an employee row. Everything after is money.
        const int FirstAmountColumn = 2;

        static string[] Row(string first, string second, int width)
        {
            var cells = new string[width];
            Array.Fill(cells, string.Empty);
            cells[0] = first;
            if (width > 1) cells[1] = second;
            return cells;
        }

        foreach (var profile in profiles)
        {
            users.TryGetValue(profile.UserId, out var user);

            // The employee's own row opens their block; the twelve month rows
            // sit under it for the admin to fill.
            //
            // Its amount columns are greyed, because the parser ignores them —
            // they exist for the admin's eye. Handing over an empty, editable
            // cell that is going to be discarded is how a whole year of figures
            // got typed onto the wrong row and imported as nothing at all.
            sheet.AddRow(Row(
                user?.Name ?? user?.Email ?? profile.Id,
                profile.IdNumber ?? string.Empty,
                columnCount));
            sheet.MuteLastRowFrom(FirstAmountColumn);

            foreach (var month in YtdImportParser.MonthLabels())
            {
                sheet.AddRow(Row(month, string.Empty, columnCount));
            }
        }

        return TabularExportResult.From(sheet, format, $"ytd-import-template-{year}");
    }

    // "NRIC: 001127-08-0576" / "Passport: A1234567" — the reference system
    // labels the document kind in the cell, and its instructions tell admins
    // to keep the prefix, so the template hands it back the same way. The
    // parser strips it again on the way in.
    private static string PersonalIdLabel(EmployeeProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.IdNumber)) return string.Empty;

        var kind = profile.IdType == Employees.Entities.IdType.PASSPORT ? "Passport" : "NRIC";
        return $"{kind}: {profile.IdNumber}";
    }

    // Read the file and report what WOULD happen. Writes nothing: an admin
    // importing a year of history should see the match list before any of it
    // becomes payroll.
    public async Task<YtdImportPreview> PreviewAsync(int year, byte[] content, TabularFormat format)
    {
        var parsed = YtdImportParser.Parse(content, format);
        if (!parsed.Ok)
        {
            return new YtdImportPreview(false, parsed.Errors, parsed.UnrecognisedColumns, [], []);
        }

        var (matched, unmatched) = await MatchAsync(parsed.Employees);
        var existing = await ExistingImportableMonthsAsync(year);

        var rows = matched.Select(m => new YtdImportPreviewRow(
            m.Parsed.EmployeeName,
            m.Profile.Id,
            [.. m.Parsed.Months.Select(mo => mo.Month).Order()],
            m.Parsed.Months.Sum(mo => mo.Gross),
            m.Parsed.Months.Sum(mo => mo.Pcb))).ToList();

        var warnings = new List<string>(parsed.UnrecognisedColumns.Select(
            c => $"Column '{c}' was not recognised and will be ignored."));

        // Months that already have a COMPUTED run are the dangerous case:
        // importing over one would replace payroll this system produced with
        // figures typed into a spreadsheet.
        var blocked = await BlockedMonthsAsync(year);
        if (blocked.Count > 0)
        {
            warnings.Add(
                "These months already have payroll in this system and will be skipped: "
                + string.Join(", ", blocked.Select(m => PayrollPeriodLabel.For(year, m))) + ".");
        }

        if (existing.Count > 0)
        {
            warnings.Add(
                "These months were previously imported and will be replaced: "
                + string.Join(", ", existing.Select(m => PayrollPeriodLabel.For(year, m))) + ".");
        }

        return new YtdImportPreview(true, [], warnings, rows, unmatched);
    }

    public async Task<YtdImportResult> ImportAsync(int year, byte[] content, TabularFormat format)
    {
        var parsed = YtdImportParser.Parse(content, format);
        if (!parsed.Ok) return YtdImportResult.Failed(parsed.Errors);

        var (matched, unmatched) = await MatchAsync(parsed.Employees);
        if (matched.Count == 0)
        {
            return YtdImportResult.Failed([
                "None of the names in the sheet matched an employee in this organisation.",
            ]);
        }

        // A month this system COMPUTED is never overwritten. Replacing a real
        // run with typed figures would destroy the payslips those figures were
        // reconciled against.
        var blocked = await BlockedMonthsAsync(year);

        var byMonth = new Dictionary<int, List<(MatchedEmployee Employee, YtdImportParser.YtdMonthAmounts Amounts)>>();

        foreach (var employee in matched)
        {
            foreach (var month in employee.Parsed.Months)
            {
                if (blocked.Contains(month.Month)) continue;

                if (!byMonth.TryGetValue(month.Month, out var list))
                {
                    list = [];
                    byMonth[month.Month] = list;
                }

                list.Add((employee, month));
            }
        }

        var now = DateTime.UtcNow;
        var monthsImported = 0;
        var payslipsImported = 0;

        foreach (var (month, entries) in byMonth.OrderBy(kv => kv.Key))
        {
            var run = await _runs.GetByPeriodAsync(year, month);

            if (run is null)
            {
                run = new PayrollRun
                {
                    PeriodYear = year,
                    PeriodMonth = month,
                    CreatedAt = now,
                };
                await _runs.AddAsync(run);
            }

            // Imported months land SUBMITTED, because that is the only status
            // `GetYtdByEmployeeAsync` reads — a history that does not count
            // towards year-to-date would leave PCB exactly as wrong as having
            // no history at all.
            run.Status = PayrollRunStatus.SUBMITTED;
            run.Source = PayrollRunSource.IMPORTED;
            run.SubmittedAt = now;
            run.SubmittedById = _currentUser.UserId;
            run.GeneratedAt = now;
            run.LastMutatedAt = null;
            run.UpdatedAt = now;

            var payslips = new List<Payslip>();
            var lineItems = new List<PayslipLineItem>();

            foreach (var (employee, amounts) in entries)
            {
                var payslip = ToPayslip(run, employee, amounts, now);
                payslips.Add(payslip);

                foreach (var (category, amount) in amounts.CategoryAmounts)
                {
                    if (amount <= 0m) continue;

                    var meta = PayrollAdjustmentCategories.Find(category);

                    lineItems.Add(new PayslipLineItem
                    {
                        PayslipId = payslip.Id,
                        Kind = meta?.Kind ?? PayslipLineKind.ALLOWANCE,
                        Category = category,
                        Label = meta?.Label ?? category,
                        Amount = amount,
                        SubjectToEpf = meta?.SubjectToEpf ?? false,
                        SubjectToSocso = meta?.SubjectToSocso ?? false,
                        SubjectToEis = meta?.SubjectToEis ?? false,
                        SubjectToPcb = meta?.SubjectToPcb ?? true,
                        CreatedAt = now,
                    });
                }
            }

            await _payslips.ReplaceForRunAsync(run.Id, payslips, lineItems);
            ApplyTotals(run, payslips);
            await _runs.UpdateAsync(run);

            monthsImported++;
            payslipsImported += payslips.Count;
        }

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollYtdImport,
            $"Imported {payslipsImported} payslip(s) across {monthsImported} month(s) of {year} history",
            TargetType: "PayrollRun",
            Metadata: new
            {
                Year = year,
                Months = monthsImported,
                Payslips = payslipsImported,
                Unmatched = unmatched.Count,
                SkippedMonths = blocked,
            }));

        return new YtdImportResult(true, [], monthsImported, payslipsImported, unmatched,
            [.. blocked.Select(m => PayrollPeriodLabel.For(year, m))]);
    }

    // ─── Matching ───────────────────────────────────────────────────────

    private sealed record MatchedEmployee(
        YtdImportParser.YtdEmployeeRows Parsed, EmployeeProfile Profile, string Name);

    // By IC first, then by name. The IC is the reliable key — two people can
    // share a name, and a spreadsheet from another system will not carry our
    // ids. A name that matches more than one employee is NOT guessed at.
    private async Task<(List<MatchedEmployee> Matched, List<string> Unmatched)> MatchAsync(
        IReadOnlyList<YtdImportParser.YtdEmployeeRows> parsed)
    {
        var (profiles, users) = await RosterAsync();

        var byIc = profiles
            .Where(p => !string.IsNullOrWhiteSpace(p.IdNumber))
            .GroupBy(p => Digits(p.IdNumber!), StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);

        var byName = profiles
            .Select(p => (Profile: p, Name: users.GetValueOrDefault(p.UserId)?.Name))
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => Normalise(x.Name!), StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Profile, StringComparer.Ordinal);

        var matched = new List<MatchedEmployee>();
        var unmatched = new List<string>();

        foreach (var row in parsed)
        {
            EmployeeProfile? profile = null;

            if (!string.IsNullOrWhiteSpace(row.PersonalId))
            {
                byIc.TryGetValue(Digits(row.PersonalId), out profile);
            }

            profile ??= byName.GetValueOrDefault(Normalise(row.EmployeeName));

            if (profile is null)
            {
                unmatched.Add(row.EmployeeName);
                continue;
            }

            matched.Add(new MatchedEmployee(row, profile, row.EmployeeName));
        }

        return (matched, unmatched);
    }

    private async Task<(List<EmployeeProfile> Profiles, Dictionary<string, Auth.Entities.User> Users)>
        RosterAsync()
    {
        var profiles = await _directory.GetProfilesForCurrentOrgAsync();
        var users = (await _directory.GetUsersAsync())
            .ToDictionary(u => u.Id, u => u, StringComparer.Ordinal);

        return (profiles, users);
    }

    // Months that already carry payroll THIS SYSTEM produced. Never
    // overwritten — see ImportAsync.
    private async Task<List<int>> BlockedMonthsAsync(int year) =>
        [.. (await _runs.GetAllAsync())
            .Where(r => r.PeriodYear == year && r.Source == PayrollRunSource.COMPUTED)
            .Select(r => r.PeriodMonth)
            .Order()];

    private async Task<List<int>> ExistingImportableMonthsAsync(int year) =>
        [.. (await _runs.GetAllAsync())
            .Where(r => r.PeriodYear == year && r.Source == PayrollRunSource.IMPORTED)
            .Select(r => r.PeriodMonth)
            .Order()];

    // ─── Mapping ────────────────────────────────────────────────────────

    // The figures are taken EXACTLY as typed. Nothing here recomputes EPF or
    // PCB from the salary: these months were paid, and what this engine would
    // have calculated is beside the point.
    private static Payslip ToPayslip(
        PayrollRun run, MatchedEmployee employee, YtdImportParser.YtdMonthAmounts amounts,
        DateTime now) => new()
        {
            PayrollRunId = run.Id,
            EmployeeProfileId = employee.Profile.Id,
            UserId = employee.Profile.UserId,
            SnapshotName = employee.Name,
            SnapshotEmployeeNumber = employee.Profile.Id,
            SnapshotSalaryType = employee.Profile.SalaryType,
            SnapshotMonthlySalary = amounts.BasicSalary,

            BasicPay = amounts.BasicSalary,
            ProratedPay = amounts.BasicSalary,
            GrossPay = amounts.Gross,
            NetPay = amounts.Net,

            EpfEmployee = amounts.EpfEmployee,
            EpfEmployer = amounts.EpfEmployer,
            SocsoEmployee = amounts.SocsoEmployee,
            SocsoEmployer = amounts.SocsoEmployer,
            EisEmployee = amounts.EisEmployee,
            EisEmployer = amounts.EisEmployer,

            // Carried through so it lands on the payslip's own statutory field
            // and counts toward the RM 350 PERKESO relief. SkbbkWage stays 0:
            // the sheet gives the contribution, not the wage it was looked up
            // against, and nothing recomputes from it on an imported row.
            SkbbkEmployee = amounts.SkbbkEmployee,

            // The summary fields, which were left at zero while the line items
            // beneath them were written correctly. A payslip showed its
            // adjustments but totalled none of them, and Form EA read a benefit
            // in kind of nothing however many BIK rows the import carried.
            TotalAllowances = amounts.Allowances,
            TotalBenefitsInKind = amounts.BenefitsInKind,
            TotalReimbursements = amounts.Reimbursements,
            // Excludes the gross-reducing ones by construction — they already
            // came off gross, and counting them here would dock the employee
            // twice for one absence.
            TotalDeductions = amounts.NetOnlyDeductions,
            Pcb = amounts.Pcb,
            Cp38 = amounts.Cp38,
            Zakat = amounts.Zakat,
            Hrdf = amounts.Hrdf,

            TotalCostToEmployer = amounts.Gross + amounts.EpfEmployer
                + amounts.SocsoEmployer + amounts.EisEmployer + amounts.Hrdf,

            // No breakdown: the PCB was decided by whatever system produced
            // it, and inventing a worksheet would claim a derivation this
            // engine never performed.
            PcbCalculationJson = null,

            CreatedAt = now,
            UpdatedAt = now,
        };

    private static void ApplyTotals(PayrollRun run, IReadOnlyList<Payslip> payslips)
    {
        run.EmployeeCount = payslips.Count;
        run.TotalGross = payslips.Sum(p => p.GrossPay);
        run.TotalNet = payslips.Sum(p => p.NetPay);
        run.TotalEmployeeEpf = payslips.Sum(p => p.EpfEmployee);
        run.TotalEmployerEpf = payslips.Sum(p => p.EpfEmployer);
        run.TotalEmployeeSocso = payslips.Sum(p => p.SocsoEmployee);
        run.TotalEmployerSocso = payslips.Sum(p => p.SocsoEmployer);
        run.TotalEmployeeEis = payslips.Sum(p => p.EisEmployee);
        run.TotalEmployeeSkbbk = payslips.Sum(p => p.SkbbkEmployee);
        run.TotalEmployerEis = payslips.Sum(p => p.EisEmployer);
        run.TotalPcb = payslips.Sum(p => p.Pcb);
        run.TotalZakat = payslips.Sum(p => p.Zakat);
        run.TotalHrdf = payslips.Sum(p => p.Hrdf);
        run.TotalCostToEmployer = payslips.Sum(p => p.TotalCostToEmployer);
    }

    private static string Digits(string value) =>
        new([.. value.Where(char.IsDigit)]);

    private static string Normalise(string value) =>
        string.Join(' ', value.ToLowerInvariant()
            .Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));
}
