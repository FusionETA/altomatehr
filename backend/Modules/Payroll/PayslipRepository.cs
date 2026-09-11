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
            .OrderBy(li => li.Kind)
            .ThenBy(li => li.CreatedAt)
            .ToListAsync();

    public Task<List<PayslipLineItem>> GetLineItemsForRunAsync(string payrollRunId)
    {
        var payslipIds = _db.Payslips
            .Where(p => p.PayrollRunId == payrollRunId)
            .Select(p => p.Id);

        return _db.PayslipLineItems
            .Where(li => payslipIds.Contains(li.PayslipId))
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
                    Pcb = g.Sum(p => p.Pcb),
                    Hrdf = g.Sum(p => p.Hrdf),
                },
                StringComparer.Ordinal);
    }

    public async Task<IReadOnlyDictionary<string, PayrollYtdTotals>> GetYtdByEmployeeAsync(
        int year, string? excludeRunId)
    {
        // Only SUBMITTED runs. A draft is not tax withheld, and the run being
        // generated right now must not feed its own YTD baseline.
        var runIds = await _db.PayrollRuns
            .Where(r => r.PeriodYear == year
                        && r.Status == PayrollRunStatus.SUBMITTED
                        && (excludeRunId == null || r.Id != excludeRunId))
            .Select(r => r.Id)
            .ToListAsync();

        if (runIds.Count == 0) return new Dictionary<string, PayrollYtdTotals>();

        var payslips = await _db.Payslips
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
            })
            .ToListAsync();

        if (payslips.Count == 0) return new Dictionary<string, PayrollYtdTotals>();

        var employeeByPayslip = payslips.ToDictionary(
            p => p.Id, p => p.EmployeeProfileId, StringComparer.Ordinal);

        var lineItems = await _db.PayslipLineItems
            .Where(li => employeeByPayslip.Keys.Contains(li.PayslipId))
            .Select(li => new
            {
                li.PayslipId,
                li.Kind,
                li.Category,
                li.Amount,
                li.PcbTaxableAmount,
                li.SubjectToPcb,
            })
            .ToListAsync();

        var totals = new Dictionary<string, PayrollYtdTotals>(StringComparer.Ordinal);
        var taxableAllowances = new Dictionary<string, decimal>(StringComparer.Ordinal);
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

                perCategory[li.Category] = Get(perCategory, li.Category) + li.Amount;

                if (li.Kind == PayslipLineKind.DEDUCTION && Tp1Categories.Contains(li.Category))
                {
                    tp1[employeeId] = Get(tp1, employeeId) + li.Amount;
                }
            }
        }

        foreach (var group in payslips.GroupBy(p => p.EmployeeProfileId, StringComparer.Ordinal))
        {
            var employeeId = group.Key;

            totals[employeeId] = new PayrollYtdTotals
            {
                Taxable = group.Sum(p => p.ProratedPay + p.OtPay)
                          + Get(taxableAllowances, employeeId),
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

    private static decimal Get(IDictionary<string, decimal> map, string key) =>
        map.TryGetValue(key, out var value) ? value : 0m;
}
