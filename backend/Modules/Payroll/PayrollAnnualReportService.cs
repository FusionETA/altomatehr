using System.IO.Compression;
using System.Text.Json;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Pdf;

namespace AltomateHR.Api.Modules.Payroll;

public class PayrollAnnualReportService : IPayrollAnnualReportService
{
    private readonly IPayrollRunRepository _runs;
    private readonly IPayslipRepository _payslips;
    private readonly IPayrollCompanyInfoRepository _companyInfo;
    private readonly IDirectoryService _directory;
    private readonly IOrganizationService _organizations;
    private readonly ICurrentUser _currentUser;

    public PayrollAnnualReportService(
        IPayrollRunRepository runs,
        IPayslipRepository payslips,
        IPayrollCompanyInfoRepository companyInfo,
        IDirectoryService directory,
        IOrganizationService organizations,
        ICurrentUser currentUser)
    {
        _runs = runs;
        _payslips = payslips;
        _companyInfo = companyInfo;
        _directory = directory;
        _organizations = organizations;
        _currentUser = currentUser;
    }

    public IReadOnlyList<PayrollAnnualReports.Meta> GetAvailable() =>
        [.. PayrollAnnualReports.All.Values];

    public async Task<StatutoryFileResult> RenderAsync(PayrollAnnualReportKind kind, int year)
    {
        var payload = await LoadAsync(year);

        // The year gate, enforced here and not only on the page, so no direct
        // URL or API caller gets a partial-year return either. PCB 2(II) is a
        // statement of deductions so far, and is exempt.
        var meta = PayrollAnnualReports.All.GetValueOrDefault(kind);
        if (meta is null) return StatutoryFileResult.Refused("Unknown annual report.");

        if (meta.RequiresFullYear && !payload.CanGenerate)
        {
            var missing = string.Join(", ", payload.MissingMonths.Select(m =>
                System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(m)));

            return StatutoryFileResult.Refused(
                $"{payload.RequiredMonths - payload.MissingMonths.Count}/{payload.RequiredMonths} monthly runs "
                + $"approved for {year}. The annual forms cover every month this company ran payroll "
                + $"through December, so approve those first (missing: {missing}).");
        }

        return kind switch
        {
            PayrollAnnualReportKind.CP8D_EMPLOYER_TXT => Cp8dTxt.RenderEmployer(payload),
            PayrollAnnualReportKind.CP8D_EMPLOYEE_TXT => Cp8dTxt.RenderEmployees(payload),
            PayrollAnnualReportKind.FORM_EA_BULK_PDF => Pdf(
                kind, payload, FormEaPdf.Render(payload), FormEaPdf.ContentType),
            PayrollAnnualReportKind.PCB2II_BULK_PDF => Pdf(
                kind, payload,
                LhdnForms.Pdf.Pcb2IiPdf.Render([.. payload.Employees
                    .Select(e => LhdnForms.Pdf.Pcb2IiStatement.From(payload, e, DateTime.UtcNow))]),
                LhdnForms.Pdf.Pcb2IiPdf.ContentType),
            PayrollAnnualReportKind.FORM_E_CP8D_PDF => Pdf(
                kind, payload,
                FormECp8dPdf.Render(payload, await PartAAsync(year)),
                FormECp8dPdf.ContentType),
            _ => StatutoryFileResult.Refused("Unknown annual report."),
        };
    }

    private static StatutoryFileResult Pdf(
        PayrollAnnualReportKind kind, PayrollAnnualPayload payload, byte[] bytes, string mime) =>
        new(true,
            PayrollAnnualReports.FileName(kind, payload.Year, payload.EmployerNo),
            bytes, mime, null);

    // ─── One employee's Form EA ─────────────────────────────────────────

    public async Task<IReadOnlyList<EmployeeEaFormDto>> GetEmployeeEaYearsAsync(string employeeProfileId)
    {
        // GetForEmployeeAsync returns submitted runs only, so a year paid
        // only in drafts does not appear.
        var years = (await _payslips.GetForEmployeeAsync(employeeProfileId))
            .Select(row => row.Run.PeriodYear)
            .Distinct()
            .ToList();

        if (years.Count == 0) return [];

        // Approval is the company's, not the employee's: a month counts once
        // its run is submitted, whoever was on it.
        var allRuns = await _runs.GetAllAsync();
        var submitted = allRuns
            .Where(r => r.Status == PayrollRunStatus.SUBMITTED)
            .GroupBy(r => r.PeriodYear)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<int>)g.Select(r => r.PeriodMonth).ToHashSet());
        var anyRun = allRuns
            .GroupBy(r => r.PeriodYear)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<int>)g.Select(r => r.PeriodMonth).ToHashSet());

        return [.. years
            .OrderDescending()
            .Select(year =>
            {
                var ea = EaYear.For(submitted.GetValueOrDefault(year) ?? [], anyRun.GetValueOrDefault(year));
                return new EmployeeEaFormDto
                {
                    Year = year,
                    Available = ea.Ready,
                    ApprovedMonths = ea.ApprovedMonths,
                    RequiredMonths = ea.RequiredMonths,
                    NotReadyReason = ea.Ready ? null : EaYear.NotReadyReason(year),
                };
            })];
    }

    public async Task<StatutoryFileResult> RenderEmployeeEaAsync(string employeeProfileId, int year)
    {
        var payload = await LoadAsync(year);

        var row = payload.Employees.FirstOrDefault(e =>
            string.Equals(e.EmployeeProfileId, employeeProfileId, StringComparison.Ordinal));
        if (row is null) return new StatutoryFileResult(false, null, null, null, null);

        // Held here, not only on the lists, so a direct URL cannot fetch a
        // part-year form that under-declares.
        if (!payload.CanGenerate)
            return StatutoryFileResult.Refused(EaYear.NotReadyReason(year));

        // Only this employee's page: the bulk form is every employee's pay.
        var bytes = FormEaPdf.Render(payload with { Employees = [row] });

        return new StatutoryFileResult(true, $"Form_EA_{year}.pdf", bytes, FormEaPdf.ContentType, null);
    }

    // ─── Loading the year ───────────────────────────────────────────────

    // One row per employee, summed across the year's SUBMITTED runs.
    //
    // As everywhere else in this module, the MONEY comes from the payslip
    // snapshots — those are the filed figures — while the IDENTIFIERS are read
    // live from the profile, so a tax number corrected in March reaches the
    // filing rather than being frozen wrong at generation time.
    public async Task<PayrollAnnualPayload> LoadAsync(int year)
    {
        var info = await _companyInfo.GetAsync();
        var org = await _organizations.GetByIdAsync(_currentUser.OrganizationId ?? string.Empty);

        var yearRuns = (await _runs.GetAllAsync()).Where(r => r.PeriodYear == year).ToList();
        var runs = yearRuns.Where(r => r.Status == PayrollRunStatus.SUBMITTED).ToList();

        var payload = new PayrollAnnualPayload
        {
            Year = year,
            OrganizationName = org?.Name ?? string.Empty,
            CompanyInfo = info,
            EmployerNo = PayrollAnnualReports.EmployerNumber(info?.EmployerTin),
            SubmittedMonths = [.. runs.Select(r => r.PeriodMonth).Distinct().Order()],
            RunMonths = [.. yearRuns.Select(r => r.PeriodMonth).Distinct().Order()],
            Receipts = runs
                .GroupBy(r => r.PeriodMonth)
                .ToDictionary(g => g.Key, g => new LhdnMonthReceipts(
                    g.First().PcbReceiptNo, g.First().PcbReceiptDate,
                    g.First().Cp38ReceiptNo, g.First().Cp38ReceiptDate)),
        };

        if (runs.Count == 0) return payload;

        var profiles = (await _directory.GetProfilesForCurrentOrgAsync())
            .ToDictionary(p => p.Id, p => p, StringComparer.Ordinal);
        var users = (await _directory.GetUsersAsync())
            .ToDictionary(u => u.Id, u => u, StringComparer.Ordinal);

        var totals = new Dictionary<string, Accumulator>(StringComparer.Ordinal);

        foreach (var run in runs)
        {
            var payslips = await _payslips.GetForRunAsync(run.Id);
            var lineItems = (await _payslips.GetLineItemsForRunAsync(run.Id))
                .GroupBy(li => li.PayslipId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            foreach (var payslip in payslips)
            {
                if (!totals.TryGetValue(payslip.EmployeeProfileId, out var acc))
                {
                    acc = new Accumulator { Snapshot = payslip };
                    totals[payslip.EmployeeProfileId] = acc;
                }

                Accumulate(acc, payslip, lineItems.GetValueOrDefault(payslip.Id) ?? [], run.PeriodMonth);
            }
        }

        return payload with
        {
            Employees = [.. totals
                .Select(kv => ToRow(kv.Key, kv.Value, profiles, users))
                // By employee code so regenerating a file produces the same
                // bytes, and so the CP8D rows line up with the EA pages.
                // Culture-aware, matching the previous system's localeCompare.
                .OrderBy(r => r.EmployeeCode, StringComparer.InvariantCulture)
                .ThenBy(r => r.EmployeeName, StringComparer.Ordinal)],
        };
    }

    private sealed class Accumulator
    {
        // The most recent payslip seen, used only for the name when the
        // employee's profile has since been deleted.
        public Payslip Snapshot { get; set; } = null!;

        public decimal Gross { get; set; }
        public decimal Bonus { get; set; }
        public decimal Bik { get; set; }
        public decimal Pcb { get; set; }
        public decimal Mtd { get; set; }
        public decimal Cp38 { get; set; }
        public decimal Zakat { get; set; }
        public decimal Epf { get; set; }
        public decimal Socso { get; set; }
        public decimal Eis { get; set; }

        public SortedDictionary<int, (decimal Pcb, decimal Cp38, decimal Zakat)> Months { get; } = new();

        // Form EA, line by line (FormEaLines).
        public FormEaFigures Ea { get; } = new();
    }

    private static void Accumulate(
        Accumulator acc, Payslip payslip, IReadOnlyList<PayslipLineItem> lineItems, int month)
    {
        acc.Snapshot = payslip;

        var m = acc.Months.GetValueOrDefault(month);
        acc.Months[month] = (m.Pcb + payslip.Pcb + payslip.VoluntaryPcb, m.Cp38 + payslip.Cp38, m.Zakat + payslip.Zakat);

        acc.Pcb += payslip.Pcb;
        acc.Mtd += payslip.Pcb + payslip.VoluntaryPcb;
        acc.Cp38 += payslip.Cp38;
        acc.Zakat += payslip.Zakat;
        acc.Epf += payslip.EpfEmployee;
        // SKBBK is a PERKESO contribution collected with SOCSO, so it belongs
        // in the same reported figure.
        acc.Socso += payslip.SocsoEmployee + payslip.SkbbkEmployee;
        acc.Eis += payslip.EisEmployee;

        // Bonus / commission and benefits in kind are reported APART from
        // salary on both forms, so they are pulled out of the line items and
        // gross is what remains.
        var bonus = 0m;
        var bik = 0m;

        foreach (var item in lineItems)
        {
            if (item.Kind != PayslipLineKind.ALLOWANCE) continue;

            var meta = PayrollAdjustmentCategories.Find(item.Category);
            if (meta is null) continue;

            if (meta.NonCash) bik += item.Amount;
            else if (meta.IsAdditionalRemuneration) bonus += item.Amount;
        }

        acc.Bonus += bonus;
        acc.Bik += bik;
        acc.Ea.Add(FormEaLines.For(payslip, lineItems));

        // Gross on the payslip already includes the bonus and excludes BIK,
        // so the salary line is gross less what is reported separately.
        acc.Gross += payslip.GrossPay - bonus;
    }

    private static AnnualEmployeeRow ToRow(
        string employeeProfileId,
        Accumulator acc,
        IReadOnlyDictionary<string, EmployeeProfile> profiles,
        IReadOnlyDictionary<string, Auth.Entities.User> users)
    {
        profiles.TryGetValue(employeeProfileId, out var profile);

        var name = profile is not null && users.TryGetValue(profile.UserId, out var user)
            ? PersonName.Display(user.Name, user.Email)
            // The profile was archived or deleted since the run. Falling back
            // to the snapshot keeps them on the filing rather than dropping
            // someone the employer genuinely paid that year.
            : acc.Snapshot.SnapshotName;

        var children = ParseChildren(profile?.ChildReliefJson);

        return new AnnualEmployeeRow
        {
            EmployeeProfileId = employeeProfileId,
            EmployeeName = name,
            EmployeeCode = acc.Snapshot.SnapshotEmployeeNumber ?? string.Empty,
            JobTitle = acc.Snapshot.SnapshotPosition,

            IdNumber = profile?.IdNumber,
            IdType = profile?.IdType,
            EpfNumber = profile?.EpfNumber,
            SocsoNumber = profile?.SocsoNumber,
            IncomeTaxNumber = profile?.IncomeTaxNumber,
            Gender = profile?.Gender,
            MaritalStatus = profile?.MaritalStatus,
            SpouseWorking = profile?.SpouseWorking,
            PcbBorneByEmployer = profile?.PcbBorneByEmployer ?? false,

            QualifyingChildren = children.Count(c => c.PcbDeduction != ChildPcbDeductionLevel.NONE),
            AnnualChildRelief = children.Sum(PcbReliefs.ForChild),

            GrossSalary = Money.Round2(acc.Gross),
            BonusAndCommission = Money.Round2(acc.Bonus),
            TotalBik = Money.Round2(acc.Bik),
            TotalPcb = Money.Round2(acc.Pcb),
            TotalCp38 = Money.Round2(acc.Cp38),
            TotalZakat = Money.Round2(acc.Zakat),
            TotalEpfEmployee = Money.Round2(acc.Epf),
            TotalSocsoEmployee = Money.Round2(acc.Socso),
            TotalEisEmployee = Money.Round2(acc.Eis),
            Months = [.. acc.Months.Select(kv => new AnnualMonth(
                kv.Key, Money.Round2(kv.Value.Pcb), Money.Round2(kv.Value.Cp38), Money.Round2(kv.Value.Zakat)))],
            Ea = acc.Ea.Rounded(),
            JoinDate = profile?.JoinDate,
            LeaveDate = profile?.LeaveDate,
            TotalMtdRemitted = Money.Round2(acc.Mtd),
            DateOfBirth = profile?.DateOfBirth,
            Cp8dStatusOverride = PayrollAnnualReports.Cp8dStatus(profile?.EmploymentStatus),
            Cp8dRetirementDateOverride = profile?.ContractEndDate,
        };
    }

    // Same lenient read as payroll, so EA / CP8D count the children PCB
    // relieved — including v1's legacy values (see ChildReliefJson).
    private static IReadOnlyList<ChildRelief> ParseChildren(string? json) =>
        ChildReliefJson.Parse(json);

    // The JSON on EmployeeProfile was written by the reference app, so reads
    // are case-insensitive and enums arrive as names.
    private static readonly JsonSerializerOptions ProfileJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    // ─── Form E Part A ──────────────────────────────────────────────────

    // Headcounts are about EMPLOYMENT, not about payslips: someone who joined
    // in December and was first paid in January still counts at year end.
    private async Task<FormECp8dPdf.PartA> PartAAsync(int year)
    {
        var yearEnd = new DateTime(year, 12, 31);
        var yearStart = new DateTime(year, 1, 1);

        var profiles = await _directory.GetProfilesForCurrentOrgAsync();

        var employedAtYearEnd = profiles.Count(p =>
            p.JoinDate is null || p.JoinDate <= yearEnd)
            - profiles.Count(p => p.LeaveDate is not null && p.LeaveDate < yearEnd);

        var newThisYear = profiles.Count(p => p.JoinDate >= yearStart && p.JoinDate <= yearEnd);

        // "Subject to MTD" is whoever actually had tax withheld in the year —
        // the figure LHDN reconciles against the CP8D rows.
        var payload = await LoadAsync(year);
        var subjectToMtd = payload.Employees.Count(e => e.TotalPcb > 0m);

        return new FormECp8dPdf.PartA(Math.Max(0, employedAtYearEnd), subjectToMtd, newThisYear);
    }

    public StatutoryFileResult ConvertCp8d(Cp8dConvertRequestDto request)
    {
        var employerNo = PayrollAnnualReports.EmployerNumber(request.EmployerNo);
        if (string.IsNullOrEmpty(employerNo))
            return StatutoryFileResult.Refused("The employer's LHDN E-number must contain digits.");

        // Built by hand rather than loaded: there is no year of runs behind
        // these rows, which is the entire reason the converter exists.
        var payload = new PayrollAnnualPayload
        {
            Year = request.Year,
            OrganizationName = request.EmployerName.Trim(),
            EmployerNo = employerNo,
            Employees = request.Employees.Select((row, index) => new AnnualEmployeeRow
            {
                // No profile behind a typed row, so the id only has to be
                // unique within this request — nothing reads it back.
                EmployeeProfileId = $"cp8d-manual-{index + 1}",
                EmployeeName = row.Name.Trim(),
                IncomeTaxNumber = row.TaxRef,
                IdNumber = row.NewIc,
                // A number with letters in it is a passport (or police / army
                // number), which CP8D files as written rather than as digits.
                IdType = row.NewIc.Any(char.IsLetter) ? IdType.PASSPORT : IdType.NRIC,
                Cp8dCategoryOverride = row.Category,
                Cp8dStatusOverride = row.Status,
                Cp8dRetirementDateOverride = row.RetirementDate,
                PcbBorneByEmployer = row.TaxBorneByEmployer,
                QualifyingChildren = row.Children,
                AnnualChildRelief = row.ChildRelief,
                // The typed gross is field 10 as it stands: the remuneration
                // other than what fields 11–14 report separately.
                GrossSalary = row.AnnualGross,
                Ea = new FormEaFigures
                {
                    B1a = row.AnnualGross,
                    B3 = row.BenefitsInKind,
                    B4 = row.LivingAccommodation,
                    B1e = row.Esos,
                    F = row.TaxExempt,
                    D5aTp1Relief = row.Tp1Relief,
                    D5bZakatSelfPaid = row.Tp1Zakat,
                    D3ZakatViaSalary = row.Zakat,
                },
                TotalEpfEmployee = row.Epf,
                TotalPcb = row.Pcb,
                TotalMtdRemitted = row.Pcb,
                TotalCp38 = row.Cp38,
                TotalSocsoEmployee = row.Perkeso,
            }).ToList(),
        };

        var employer = Cp8dTxt.RenderEmployer(payload);
        if (!employer.Ok) return employer;

        var employees = Cp8dTxt.RenderEmployees(payload);
        if (!employees.Ok) return employees;

        // Both files in one download: LHDN takes them as a pair, and handing
        // over one at a time is how you end up uploading last year's P beside
        // this year's M.
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, employer.FileName!, employer.Content!);
            Add(archive, employees.FileName!, employees.Content!);
        }

        return new StatutoryFileResult(
            true, $"CP8D_{employerNo}_{request.Year}.zip", buffer.ToArray(), "application/zip", null);

        static void Add(ZipArchive archive, string name, byte[] content)
        {
            using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
            stream.Write(content);
        }
    }

}
