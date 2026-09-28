using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;

namespace AltomateHR.Api.Modules.Payroll;

// Borang PCB/TP1 for an employee and month, and the month's list of TP1 / TP3
// claimants (LHDN MTD Spec 2026, Section E item 16).
//
// Everything is read from payroll: this month's payslip line items for SEMASA,
// and those plus every earlier SUBMITTED month of the year for TERKUMPUL. A
// TP1 relief row counts what it was GRANTED (`PcbTaxableAmount` when its limits
// clamped it), so the form agrees with the PCB that was withheld.
//
// Like the other run documents, only an approved (SUBMITTED) month is printed —
// a draft's figures can still change.
public sealed class Tp1FormService : ITp1FormService
{
    private readonly IPayrollRunRepository _runs;
    private readonly IPayslipRepository _payslips;
    private readonly IPayrollCompanyInfoService _companyInfo;
    private readonly IDirectoryService _directory;

    public Tp1FormService(
        IPayrollRunRepository runs,
        IPayslipRepository payslips,
        IPayrollCompanyInfoService companyInfo,
        IDirectoryService directory)
    {
        _runs = runs;
        _payslips = payslips;
        _companyInfo = companyInfo;
        _directory = directory;
    }

    public async Task<StatutoryFileResult> RenderFormAsync(string runId, string employeeProfileId)
    {
        var (model, refusal) = await BuildFormAsync(runId, employeeProfileId);
        if (model is null) return refusal!;

        return new StatutoryFileResult(true,
            $"TP1_{Safe(model.EmployeeCode ?? "employee")}_{model.Month:D2}-{model.Year}.pdf",
            Tp1FormPdf.Render(model), Tp1FormPdf.ContentType, null);
    }

    public async Task<StatutoryFileResult> RenderClaimsListAsync(string runId)
    {
        var (model, refusal) = await BuildClaimsListAsync(runId);
        if (model is null) return refusal!;

        return new StatutoryFileResult(true,
            $"TP1_TP3_Claims_{model.Month:D2}-{model.Year}.pdf",
            Tp1ClaimsListPdf.Render(model), Tp1ClaimsListPdf.ContentType, null);
    }

    // The figures behind each document, apart from the PDF so they can be
    // checked directly. Exactly one of the pair is non-null.
    public async Task<(Tp1FormPdf.Model? Model, StatutoryFileResult? Refusal)> BuildFormAsync(
        string runId, string employeeProfileId)
    {
        var month = await LoadAsync(runId);
        if (month.Refusal is { } refusal) return (null, refusal);

        var payslip = month.Payslips.FirstOrDefault(p =>
            string.Equals(p.EmployeeProfileId, employeeProfileId, StringComparison.Ordinal));
        if (payslip is null) return (null, StatutoryFileResult.Refused("That employee has no payslip on this run."));

        var who = await PeopleAsync();
        var profile = who.Profiles.GetValueOrDefault(employeeProfileId);
        var info = await _companyInfo.GetEntityAsync();
        var run = month.Run!;

        var current = month.Current.GetValueOrDefault(employeeProfileId) ?? Empty;
        var cumulative = month.Cumulative.GetValueOrDefault(employeeProfileId) ?? Empty;

        // Bahagian F: the person in charge of this month's payroll — whoever
        // put it forward, else whoever approved it. An API caller is not a
        // person, so it names nobody rather than an id.
        var actorId = run.SubmittedForApprovalById ?? run.SubmittedById;
        var actor = actorId is not null && !actorId.StartsWith("apikey:", StringComparison.Ordinal)
            ? who.Users.GetValueOrDefault(actorId)
            : null;

        var model = new Tp1FormPdf.Model
        {
            EmployerName = Blank(info?.EmployerName) ?? string.Empty,
            EmployerTin = Blank(info?.EmployerTin),
            EmployerAddress = Address(info),
            Year = run.PeriodYear,
            Month = run.PeriodMonth,
            EmployeeName = who.NameOf(employeeProfileId) ?? payslip.SnapshotName,
            EmployeeCode = who.CodeOf(employeeProfileId) ?? payslip.SnapshotEmployeeNumber,
            IdNumber = Blank(profile?.IdNumber),
            IncomeTaxNumber = Blank(profile?.IncomeTaxNumber),
            Deductions = Tp1Form.Build(Tp1Form.Deductions, current, cumulative),
            Rebates = Tp1Form.Build(Tp1Form.Rebates, current, cumulative),
            ProcessedOn = run.GeneratedAt,
            ProcessedByName = actor is null ? null : PersonName.Display(actor.Name, actor.Email),
            ProcessedByDesignation = actorId is null ? null : Blank(who.JobTitleOfUser(actorId)),
            GeneratedAt = DateTime.UtcNow,
        };

        return (model, null);
    }

    public async Task<(Tp1ClaimsListPdf.Model? Model, StatutoryFileResult? Refusal)> BuildClaimsListAsync(
        string runId)
    {
        var month = await LoadAsync(runId);
        if (month.Refusal is { } refusal) return (null, refusal);

        var who = await PeopleAsync();
        var info = await _companyInfo.GetEntityAsync();
        var run = month.Run!;

        var rows = new List<Tp1ClaimsListPdf.Row>();
        foreach (var payslip in month.Payslips)
        {
            var id = payslip.EmployeeProfileId;
            var current = month.Current.GetValueOrDefault(id) ?? Empty;
            var cumulative = month.Cumulative.GetValueOrDefault(id) ?? Empty;
            var profile = who.Profiles.GetValueOrDefault(id);

            var tp1Current = SumWhere(current, IsDeduction);
            var rebateCurrent = SumWhere(current, IsRebate);

            // TP3 is declared once, on joining: a previous employer THIS year.
            var tp3 = profile is { PrevEmploymentYear: { } prevYear } && prevYear == run.PeriodYear
                      && ((profile.PrevRemuneration ?? 0m) > 0m || (profile.PrevPcb ?? 0m) > 0m)
                ? profile.PrevRemuneration ?? 0m
                : (decimal?)null;

            if (tp1Current <= 0m && rebateCurrent <= 0m && tp3 is null) continue;

            rows.Add(new Tp1ClaimsListPdf.Row
            {
                EmployeeName = who.NameOf(id) ?? payslip.SnapshotName,
                EmployeeCode = who.CodeOf(id) ?? payslip.SnapshotEmployeeNumber,
                IdNumber = Blank(profile?.IdNumber),
                IncomeTaxNumber = Blank(profile?.IncomeTaxNumber),
                Tp1Current = tp1Current,
                Tp1Cumulative = SumWhere(cumulative, IsDeduction),
                RebateCurrent = rebateCurrent,
                ItemsClaimed = Tp1Form.ClaimedRefs(current),
                Tp3PriorRemuneration = tp3,
            });
        }

        var model = new Tp1ClaimsListPdf.Model
        {
            EmployerName = Blank(info?.EmployerName) ?? string.Empty,
            Year = run.PeriodYear,
            Month = run.PeriodMonth,
            Rows = [.. rows.OrderBy(r => r.EmployeeCode ?? r.EmployeeName, StringComparer.InvariantCulture)],
            GeneratedAt = DateTime.UtcNow,
        };

        return (model, null);
    }

    // ─── Loading ────────────────────────────────────────────────────────

    private static readonly IReadOnlyDictionary<string, decimal> Empty =
        new Dictionary<string, decimal>(StringComparer.Ordinal);

    private sealed record MonthData(
        PayrollRun? Run,
        StatutoryFileResult? Refusal,
        IReadOnlyList<Payslip> Payslips,
        IReadOnlyDictionary<string, Dictionary<string, decimal>> Current,
        IReadOnlyDictionary<string, Dictionary<string, decimal>> Cumulative);

    private async Task<MonthData> LoadAsync(string runId)
    {
        var run = await _runs.GetByIdAsync(runId);
        if (run is null)
        {
            return new MonthData(null, new StatutoryFileResult(false, null, null, null, null), [], Empty2, Empty2);
        }

        if (run.Status != PayrollRunStatus.SUBMITTED)
        {
            return new MonthData(run, StatutoryFileResult.Refused(
                "This run has not been approved yet, so its figures can still change. "
                + "Submit and approve it before producing files."), [], Empty2, Empty2);
        }

        var earlier = (await _runs.GetAllAsync())
            .Where(r => r.Status == PayrollRunStatus.SUBMITTED
                        && r.PeriodYear == run.PeriodYear
                        && r.PeriodMonth < run.PeriodMonth)
            .ToList();

        var current = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.Ordinal);
        var cumulative = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.Ordinal);
        IReadOnlyList<Payslip> thisMonth = [];

        foreach (var r in earlier.Append(run))
        {
            var payslips = await _payslips.GetForRunAsync(r.Id);
            if (r.Id == run.Id) thisMonth = payslips;

            var employeeOf = payslips.ToDictionary(p => p.Id, p => p.EmployeeProfileId, StringComparer.Ordinal);

            foreach (var li in await _payslips.GetLineItemsForRunAsync(r.Id))
            {
                if (li.Category is not { } category || !Tp1Form.Categories.Contains(category)) continue;
                if (!employeeOf.TryGetValue(li.PayslipId, out var employee)) continue;

                // A relief counts what it was granted; a rebate what was paid.
                var amount = IsDeduction(category) ? li.PcbTaxableAmount ?? li.Amount : li.Amount;

                Add(cumulative, employee, category, amount);
                if (r.Id == run.Id) Add(current, employee, category, amount);
            }
        }

        return new MonthData(run, null, thisMonth, current, cumulative);
    }

    private static readonly IReadOnlyDictionary<string, Dictionary<string, decimal>> Empty2 =
        new Dictionary<string, Dictionary<string, decimal>>(StringComparer.Ordinal);

    private static void Add(
        Dictionary<string, Dictionary<string, decimal>> into, string employee, string category, decimal amount)
    {
        if (!into.TryGetValue(employee, out var byCategory))
        {
            byCategory = new Dictionary<string, decimal>(StringComparer.Ordinal);
            into[employee] = byCategory;
        }

        byCategory[category] = byCategory.GetValueOrDefault(category) + amount;
    }

    private static bool IsDeduction(string category) =>
        PayrollAdjustmentCategories.Find(category)?.FeedsLp1Relief == true;

    private static bool IsRebate(string category) =>
        PayrollAdjustmentCategories.Find(category)?.OffsetsPcb == true;

    private static decimal SumWhere(IReadOnlyDictionary<string, decimal> amounts, Func<string, bool> keep) =>
        Money.Round2(amounts.Where(kv => keep(kv.Key)).Sum(kv => kv.Value));

    // ─── People ─────────────────────────────────────────────────────────

    private sealed record People(
        IReadOnlyDictionary<string, Employees.Entities.EmployeeProfile> Profiles,
        IReadOnlyDictionary<string, Auth.Entities.User> Users,
        IReadOnlyDictionary<string, Employees.Entities.OrganizationMembership> MembershipsByUser)
    {
        public string? NameOf(string profileId) =>
            Profiles.GetValueOrDefault(profileId) is { } p && Users.GetValueOrDefault(p.UserId) is { } u
                ? PersonName.Display(u.Name, u.Email)
                : null;

        public string? CodeOf(string profileId) =>
            Profiles.GetValueOrDefault(profileId) is { } p
                ? Blank(MembershipsByUser.GetValueOrDefault(p.UserId)?.EmployeeNumber)
                : null;

        public string? JobTitleOfUser(string userId) => MembershipsByUser.GetValueOrDefault(userId)?.JobTitle;
    }

    private async Task<People> PeopleAsync() => new(
        (await _directory.GetProfilesForCurrentOrgAsync())
            .GroupBy(p => p.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
        (await _directory.GetUsersAsync()).ToDictionary(u => u.Id, u => u, StringComparer.Ordinal),
        (await _directory.GetMembershipsForCurrentOrgAsync())
            .GroupBy(m => m.UserId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal));

    private static string? Address(PayrollCompanyInfo? info)
    {
        if (info is null) return null;

        var place = string.Join(" ", new[] { info.Postcode, info.City }.Where(v => !string.IsNullOrWhiteSpace(v)));
        var parts = new[] { info.AddressLine1, info.AddressLine2, place, info.State }
            .Where(v => !string.IsNullOrWhiteSpace(v));

        return Blank(string.Join(", ", parts));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Safe(string value) =>
        new string([.. value.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_')]);
}
