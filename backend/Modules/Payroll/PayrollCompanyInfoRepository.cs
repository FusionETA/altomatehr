using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Payroll.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll;

public class PayrollCompanyInfoRepository : IPayrollCompanyInfoRepository
{
    private readonly AppDbContext _db;

    public PayrollCompanyInfoRepository(AppDbContext db) => _db = db;

    public Task<PayrollCompanyInfo?> GetAsync() =>
        _db.PayrollCompanyInfos.FirstOrDefaultAsync();

    public async Task<PayrollCompanyInfo> AddAsync(PayrollCompanyInfo info)
    {
        _db.PayrollCompanyInfos.Add(info);
        await _db.SaveChangesAsync();
        return info;
    }

    public async Task UpdateAsync(PayrollCompanyInfo info)
    {
        _db.PayrollCompanyInfos.Update(info);
        await _db.SaveChangesAsync();
    }
}
