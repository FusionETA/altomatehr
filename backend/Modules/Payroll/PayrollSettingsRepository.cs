using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Payroll.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll;

public class PayrollSettingsRepository : IPayrollSettingsRepository
{
    private readonly AppDbContext _db;

    public PayrollSettingsRepository(AppDbContext db) => _db = db;

    // The global query filter narrows this to the current org, so
    // FirstOrDefault over the whole set returns this tenant's row or nothing.
    public Task<PayrollSettings?> GetAsync() =>
        _db.PayrollSettings.FirstOrDefaultAsync();

    public async Task<PayrollSettings> AddAsync(PayrollSettings settings)
    {
        _db.PayrollSettings.Add(settings);
        await _db.SaveChangesAsync();
        return settings;
    }

    public async Task UpdateAsync(PayrollSettings settings)
    {
        _db.PayrollSettings.Update(settings);
        await _db.SaveChangesAsync();
    }
}
