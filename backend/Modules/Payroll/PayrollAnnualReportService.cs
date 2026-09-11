using System.Text.Json;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Payroll.Entities;
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

        return kind switch
        {
            PayrollAnnualReportKind.CP8D_EMPLOYER_TXT => Cp8dTxt.RenderEmployer(payload),
            PayrollAnnualReportKind.CP8D_EMPLOYEE_TXT => Cp8dTxt.RenderEmployees(payload),
            PayrollAnnualReportKind.FORM_EA_BULK_PDF => Pdf(
                kind, payload, FormEaPdf.Render(payload), FormEaPdf.ContentType),
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

        var runs = (await _runs.GetAllAsync())
            .Where(r => r.PeriodYear == year && r.Status == PayrollRunStatus.SUBMITTED)
            .ToList();

        var payload = new PayrollAnnualPayload
        {
            Year = year,
            OrganizationName = org?.Name ?? string.Empty,
            CompanyInfo = info,
            EmployerNo = PayrollAnnualReports.EmployerNumber(info?.EmployerTin),
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

                Accumulate(acc, payslip, lineItems.GetValueOrDefault(payslip.Id) ?? []);
            }
        }

        return payload with
        {
            Employees = [.. totals
                .Select(kv => ToRow(kv.Key, kv.Value, profiles, users))
                // By employee code so regenerating a file produces the same
                // bytes, and so the CP8D rows line up with the EA pages.
                .OrderBy(r => r.EmployeeCode, StringComparer.Ordinal)
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
        public decimal Cp38 { get; set; }
        public decimal Zakat { get; set; }
        public decimal Epf { get; set; }
        public decimal Socso { get; set; }
        public decimal Eis { get; set; }
    }

    private static void Accumulate(
        Accumulator acc, Payslip payslip, IReadOnlyList<PayslipLineItem> lineItems)
    {
        acc.Snapshot = payslip;

        acc.Pcb += payslip.Pcb;
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
            ? user.Name ?? user.Email
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
        };
    }

    // Malformed JSON reads as no children rather than failing the filing. An
    // under-claimed relief is recoverable by the employee on their own return;
    // a filing that cannot be produced at all is not.
    private static IReadOnlyList<ChildRelief> ParseChildren(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            return JsonSerializer.Deserialize<List<ChildRelief>>(json, ProfileJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

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
}
