using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Payroll.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll;

public class EmployeeLoanRepository : IEmployeeLoanRepository
{
    private readonly AppDbContext _db;

    public EmployeeLoanRepository(AppDbContext db) => _db = db;

    public Task<List<EmployeeLoan>> GetAllAsync(string? employeeProfileId = null) =>
        _db.EmployeeLoans
            .Where(l => employeeProfileId == null || l.EmployeeProfileId == employeeProfileId)
            // Newest first: the loan an admin just created is the one they
            // are looking for.
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();

    public Task<EmployeeLoan?> GetByIdAsync(string id) =>
        _db.EmployeeLoans.FirstOrDefaultAsync(l => l.Id == id);

    public Task<List<EmployeeLoan>> GetActiveAsync() =>
        _db.EmployeeLoans
            .Where(l => l.Status == LoanStatus.ACTIVE)
            .ToListAsync();

    public async Task<EmployeeLoan> AddAsync(EmployeeLoan loan)
    {
        _db.EmployeeLoans.Add(loan);
        await _db.SaveChangesAsync();
        return loan;
    }

    public async Task UpdateAsync(EmployeeLoan loan)
    {
        _db.EmployeeLoans.Update(loan);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(EmployeeLoan loan)
    {
        _db.EmployeeLoans.Remove(loan);
        await _db.SaveChangesAsync();
    }
}
