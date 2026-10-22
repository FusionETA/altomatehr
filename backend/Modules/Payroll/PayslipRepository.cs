using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Payroll.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll;

public class PayslipRepository : IPayslipRepository
{
    private readonly AppDbContext _db;

    public PayslipRepository(AppDbContext db) => _db = db;

    public Task<List<Payslip>> GetForRunAsync(string payrollRunId) =>
        _db.Payslips
            .Where(p => p.PayrollRunId == payrollRunId)
            .OrderBy(p => p.SnapshotName)
            .ToListAsync();

    public async Task<List<(Payslip Payslip, PayrollRun Run)>> GetForEmployeeAsync(
        string employeeProfileId)
    {
        var rows = await (from p in _db.Payslips
                          join r in _db.PayrollRuns on p.PayrollRunId equals r.Id
                          where p.EmployeeProfileId == employeeProfileId
                             && r.Status == PayrollRunStatus.SUBMITTED
                          orderby r.PeriodYear descending, r.PeriodMonth descending
                          select new { Payslip = p, Run = r }).ToListAsync();

        return [.. rows.Select(x => (x.Payslip, x.Run))];
    }

    public async Task<(Payslip Payslip, PayrollRun Run)?> GetWithRunAsync(string payslipId)
    {
        var pair = await (from p in _db.Payslips
                          join r in _db.PayrollRuns on p.PayrollRunId equals r.Id
                          where p.Id == payslipId
                          select new { Payslip = p, Run = r }).FirstOrDefaultAsync();

        return pair is null ? null : (pair.Payslip, pair.Run);
    }

    public Task<List<PayslipLineItem>> GetLineItemsAsync(string payslipId) =>
        _db.PayslipLineItems
            .Where(li => li.PayslipId == payslipId)
            .OrderBy(li => li.SortOrder)
            .ThenBy(li => li.Kind)
            .ThenBy(li => li.CreatedAt)
            .ToListAsync();

    public Task<List<PayslipLineItem>> GetLineItemsForRunAsync(string payrollRunId)
    {
        var payslipIds = _db.Payslips
            .Where(p => p.PayrollRunId == payrollRunId)
            .Select(p => p.Id);

        // Ordered, like the single-payslip read above: with no ORDER BY the
        // payslip drawer and PDFs listed lines in whatever order MySQL chose.
        return _db.PayslipLineItems
            .Where(li => payslipIds.Contains(li.PayslipId))
            .OrderBy(li => li.PayslipId)
            .ThenBy(li => li.SortOrder)
            .ThenBy(li => li.Kind)
            .ThenBy(li => li.CreatedAt)
            .ToListAsync();
    }

    // One SaveChanges, so the delete and the insert land together — EF wraps it
    // in a transaction. A run that lost its old payslips but never got new ones
    // is worse than a stale run.
    public async Task ReplaceForRunAsync(
        string payrollRunId,
        IReadOnlyList<Payslip> payslips,
        IReadOnlyList<PayslipLineItem> lineItems)
    {
        var existingPayslips = await _db.Payslips
            .Where(p => p.PayrollRunId == payrollRunId)
            .ToListAsync();

        var existingIds = existingPayslips.Select(p => p.Id).ToList();

        var existingLineItems = await _db.PayslipLineItems
            .Where(li => existingIds.Contains(li.PayslipId))
            .ToListAsync();

        _db.PayslipLineItems.RemoveRange(existingLineItems);
        _db.Payslips.RemoveRange(existingPayslips);

        _db.Payslips.AddRange(payslips);              // StampTenant sets OrganizationId
        _db.PayslipLineItems.AddRange(lineItems);

        await _db.SaveChangesAsync();
    }

    // Which categories a TP1 relief can come from. Derived from the catalogue
    // rather than restated here, so adding a TP1 sub-category cannot silently
    // drop out of next month's ΣLP.
    private static readonly HashSet<string> Tp1Categories =
        PayrollAdjustmentCategories.All.Values
            .Where(m => m.FeedsLp1Relief)
            .Select(m => m.Code)
            .ToHashSet(StringComparer.Ordinal);

    public async Task<IReadOnlyDictionary<string, PayslipYtdSummary>> GetYtdThroughPeriodAsync(
        int year, int month, string? includeRunId)
    {
        // Submitted months up to and including this one, plus the run being
        // rendered — see the note on the interface for why the latter is here.
        var runIds = await _db.PayrollRuns
            .Where(r => r.PeriodYear == year
                        && r.PeriodMonth <= month
                        && (r.Status == PayrollRunStatus.SUBMITTED
                            || (includeRunId != null && r.Id == includeRunId)))
            .Select(r => r.Id)
            .ToListAsync();

        if (runIds.Count == 0) return new Dictionary<string, PayslipYtdSummary>();

        var payslips = await _db.Payslips
            .Where(p => runIds.Contains(p.PayrollRunId))
            .ToListAsync();

        return payslips
            .GroupBy(p => p.EmployeeProfileId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => new PayslipYtdSummary
                {
                    Gross = g.Sum(p => p.GrossPay),
                    Net = g.Sum(p => p.NetPay),
                    EpfEmployee = g.Sum(p => p.EpfEmployee),
                    EpfEmployer = g.Sum(p => p.EpfEmployer),
                    SocsoEmployee = g.Sum(p => p.SocsoEmployee),
                    SocsoEmployer = g.Sum(p => p.SocsoEmployer),
                    EisEmployee = g.Sum(p => p.EisEmployee),
                    EisEmployer = g.Sum(p => p.EisEmployer),
                    SkbbkEmployee = g.Sum(p => p.SkbbkEmployee),
                    // Tax actually withheld, so Additional PCB is included —
                    // unlike the MTD formula's X, which must exclude it.
                    Pcb = g.Sum(p => p.Pcb + p.VoluntaryPcb),
                    Hrdf = g.Sum(p => p.Hrdf),
                },
                StringComparer.Ordinal);
    }

    public Task<IReadOnlyDictionary<string, PayrollYtdTotals>> GetYtdByEmployeeAsync(
        int year, string? excludeRunId) =>
        ComputeYtdAsync(year, excludeRunId, organizationId: null, beforeMonth: null);

    public Task<IReadOnlyDictionary<string, PayrollYtdTotals>> GetYtdByEmployeeInOrgAsync(
        string organizationId, int year, int? beforeMonth = null) =>
        ComputeYtdAsync(year, excludeRunId: null, organizationId, beforeMonth);

    // `organizationId` null = the current org (tenant filter). Set = that org
    // exactly, ignoring the filter — for a transfer, which reads two companies.
    private async Task<IReadOnlyDictionary<string, PayrollYtdTotals>> ComputeYtdAsync(
        int year, string? excludeRunId, string? organizationId, int? beforeMonth)
    {
        var runsQuery = organizationId is null
            ? _db.PayrollRuns
            : _db.PayrollRuns.IgnoreQueryFilters().Where(r => r.OrganizationId == organizationId);
        var payslipsQuery = organizationId is null
            ? _db.Payslips
            : _db.Payslips.IgnoreQueryFilters().Where(p => p.OrganizationId == organizationId);
        var lineItemsQuery = organizationId is null
            ? _db.PayslipLineItems
            : _db.PayslipLineItems.IgnoreQueryFilters().Where(li => li.OrganizationId == organizationId);

        // Only SUBMITTED runs. A draft is not tax withheld, and the run being
        // generated right now must not feed its own YTD baseline.
        var runIds = await runsQuery
            .Where(r => r.PeriodYear == year
                        && (beforeMonth == null || r.PeriodMonth < beforeMonth)
                        && r.Status == PayrollRunStatus.SUBMITTED
                        && (excludeRunId == null || r.Id != excludeRunId))
            .Select(r => r.Id)
            .ToListAsync();

        if (runIds.Count == 0) return new Dictionary<string, PayrollYtdTotals>();

        var payslips = await payslipsQuery
            .Where(p => runIds.Contains(p.PayrollRunId))
            .Select(p => new
            {
                p.Id,
                p.EmployeeProfileId,
                p.ProratedPay,
                p.OtPay,
                p.EpfEmployee,
                p.SocsoEmployee,
                p.SkbbkEmployee,
                p.EisEmployee,
                p.Pcb,
                p.Zakat,
                p.PcbCalculationJson,
            })
            .ToListAsync();

        if (payslips.Count == 0) return new Dictionary<string, PayrollYtdTotals>();

        var employeeByPayslip = payslips.ToDictionary(
            p => p.Id, p => p.EmployeeProfileId, StringComparer.Ordinal);

        var lineItems = await lineItemsQuery
            .Where(li => employeeByPayslip.Keys.Contains(li.PayslipId))
            .Select(li => new
            {
                li.PayslipId,
                li.Kind,
                li.Category,
                li.Amount,
                li.PcbTaxableAmount,
                li.SubjectToPcb,
                li.Label,
            })
            .ToListAsync();

        var totals = new Dictionary<string, PayrollYtdTotals>(StringComparer.Ordinal);
        var taxableAllowances = new Dictionary<string, decimal>(StringComparer.Ordinal);
        // Per PAYSLIP, so each month's subtraction can be capped at what that
        // month's own PCB base actually held (see below).
        var reducingByPayslip = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var taxableAllowancesByPayslip = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var regularAllowancesByPayslip = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var byCategory = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.Ordinal);
        var tp1 = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var li in lineItems)
        {
            if (!employeeByPayslip.TryGetValue(li.PayslipId, out var employeeId)) continue;

            // The taxable portion, not the full amount. A travel allowance that
            // sat under its RM 6,000 ceiling in January was never taxed, and
            // adding it back here would inflate Y and compound all year.
            if (li.Kind == PayslipLineKind.ALLOWANCE && li.SubjectToPcb)
            {
                taxableAllowances[employeeId] =
                    Get(taxableAllowances, employeeId) + (li.PcbTaxableAmount ?? li.Amount);
                taxableAllowancesByPayslip[li.PayslipId] =
                    Get(taxableAllowancesByPayslip, li.PayslipId) + (li.PcbTaxableAmount ?? li.Amount);

                // The regular (not additional-remuneration) part of the month's
                // PCB base: a bonus or arrears is taxed separately and never
                // offsets a deduction.
                if (PayrollAdjustmentCategories.Find(li.Category) is not { IsAdditionalRemuneration: true })
                {
                    regularAllowancesByPayslip[li.PayslipId] =
                        Get(regularAllowancesByPayslip, li.PayslipId) + (li.PcbTaxableAmount ?? li.Amount);
                }
            }

            // A deduction that cuts the taxable wage itself — an advance
            // recovered, unpaid leave, a salary correction — lowered that
            // month's PCB base, so it has to come off the year-to-date too.
            // Otherwise every later month taxes it again (an imported RM 3,500
            // advance left Y RM 3,500 too high for the rest of the year).
            else if (li.Kind == PayslipLineKind.DEDUCTION
                     && ((li.SubjectToPcb && PayrollAdjustmentCategories.Find(li.Category) is { ReducesBase: true })
                         || IsLegacyImportedUnpaidLeave(li.Category, li.Label)))
            {
                reducingByPayslip[li.PayslipId] =
                    Get(reducingByPayslip, li.PayslipId) + (li.PcbTaxableAmount ?? li.Amount);
            }

            // Both allowances and TP1 deductions are totalled by category — the
            // annual ceilings apply on either side.
            if (li.Category is not null)
            {
                if (!byCategory.TryGetValue(employeeId, out var perCategory))
                {
                    perCategory = new Dictionary<string, decimal>(StringComparer.Ordinal);
                    byCategory[employeeId] = perCategory;
                }

                // A TP1 row counts what it was GRANTED, not what was claimed: an
                // over-claim clamped to its limit in January must not show as
                // relief in ΣLP all year, nor eat into its group's shared limit.
                // (PcbTaxableAmount carries the granted figure when it differs.)
                var isTp1 = li.Kind == PayslipLineKind.DEDUCTION && Tp1Categories.Contains(li.Category);
                var counted = isTp1 ? li.PcbTaxableAmount ?? li.Amount : li.Amount;

                perCategory[li.Category] = Get(perCategory, li.Category) + counted;

                if (isTp1)
                {
                    tp1[employeeId] = Get(tp1, employeeId) + counted;
                }
            }
        }

        foreach (var group in payslips.GroupBy(p => p.EmployeeProfileId, StringComparer.Ordinal))
        {
            var employeeId = group.Key;

            totals[employeeId] = new PayrollYtdTotals
            {
                // Never below zero: a month's PCB base never was.
                Taxable = Math.Max(0m, group.Sum(p =>
                {
                    var reducing = Get(reducingByPayslip, p.Id);
                    var gross = p.ProratedPay + p.OtPay + Get(taxableAllowancesByPayslip, p.Id);
                    if (reducing <= 0m) return gross;

                    // A month with a wage-reducing deduction: exactly what its
                    // PCB counted, from its own worksheet, when it has one (a
                    // calculated month) — that also covers a bonus the admin
                    // marked "treat as recurring", taxed in the regular base,
                    // which the line items alone can't tell apart.
                    if (CountedByPcb(p.PcbCalculationJson) is { } counted) return counted;

                    // Otherwise (an imported month): the deduction comes off only
                    // as far as the month's regular base held — its PCB wage
                    // stopped at zero, so a larger deduction never took more.
                    return gross - Math.Min(reducing,
                        Math.Max(0m, p.ProratedPay + Get(regularAllowancesByPayslip, p.Id)));
                })),
                Epf = group.Sum(p => p.EpfEmployee),
                Pcb = group.Sum(p => p.Pcb),
                Zakat = group.Sum(p => p.Zakat),
                SocsoEis = group.Sum(p => p.SocsoEmployee + p.SkbbkEmployee + p.EisEmployee),
                AllowableDeductions = Get(tp1, employeeId),
                AllowanceByCategory = byCategory.TryGetValue(employeeId, out var perCategory)
                    ? perCategory
                    : new Dictionary<string, decimal>(StringComparer.Ordinal),
            };
        }

        return totals;
    }

    // NormalTaxable + AdditionalTaxable from a payslip's PCB worksheet: the
    // regular wage and the additional remuneration (with overtime) the month's
    // PCB was worked out on. Null when there's no worksheet.
    private static decimal? CountedByPcb(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            decimal? Read(string name)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                    if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)
                        && prop.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
                        return prop.Value.GetDecimal();
                return null;
            }
            return Read("normalTaxable") is { } normal && Read("additionalTaxable") is { } additional
                ? normal + additional
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // History imported in the previous system and migrated: its YTD importer
    // wrote unpaid leave as an uncategorised DEDUCTION labelled "Unpaid leave"
    // (and flagged not-PCB), though it had come off that month's gross. Read
    // as the unpaid-leave deduction it is.
    private static bool IsLegacyImportedUnpaidLeave(string? category, string? label) =>
        category is null && string.Equals(label?.Trim(), "Unpaid leave", StringComparison.OrdinalIgnoreCase);

    private static decimal Get(IDictionary<string, decimal> map, string key) =>
        map.TryGetValue(key, out var value) ? value : 0m;

    public async Task<IReadOnlyList<PayslipFiledPeriod>> GetFiledPeriodsAsync(string? employeeProfileId = null)
    {
        // Plain comparisons, not a Contains over statuses: against the
        // string-converted enum that form doesn't translate reliably.
        var rows = await (
            from p in _db.Payslips
            join r in _db.PayrollRuns on p.PayrollRunId equals r.Id
            where (r.Status == PayrollRunStatus.SUBMITTED || r.Status == PayrollRunStatus.PENDING_APPROVAL)
                  && (employeeProfileId == null || p.EmployeeProfileId == employeeProfileId)
            select new { p.EmployeeProfileId, r.PeriodYear, r.PeriodMonth, r.Status })
            .ToListAsync();
        return [.. rows.Select(x => new PayslipFiledPeriod(x.EmployeeProfileId, x.PeriodYear, x.PeriodMonth, x.Status))];
    }
}
