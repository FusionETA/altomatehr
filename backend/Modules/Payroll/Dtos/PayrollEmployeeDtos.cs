using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Payroll.Dtos;

// One employee as payroll sees them.
//
// Deliberately NOT the full profile: this is the roster an admin scans
// before a run — what someone is paid, which statutory numbers are on file,
// and where the money goes. Editing any of it is Employees' job, so nothing
// here is writable.
//
// Keyed by EmployeeProfileId because that is what payroll's own records
// (payslips, loans, salary changes) reference. `/employees` returns the USER
// id, which is not the same key and cannot be used to attach a loan.
public class PayrollEmployeeRowDto
{
    public string EmployeeProfileId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? EmployeeNumber { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }

    public SalaryType SalaryType { get; set; }
    public decimal? MonthlySalary { get; set; }
    public decimal? HourlyRate { get; set; }

    public bool ContributeToEpf { get; set; }
    public string? EpfNumber { get; set; }
    public decimal EpfEmployeeRate { get; set; }
    public string? SocsoNumber { get; set; }
    public bool ContributeToEis { get; set; }
    public string? IncomeTaxNumber { get; set; }

    public PaymentMethod PaymentMethod { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }

    public string? IdNumber { get; set; }
    public IdType? IdType { get; set; }
    public string? Nationality { get; set; }
    public bool HasPr { get; set; }
    public bool IsResident { get; set; }

    public DateTime? JoinDate { get; set; }
    public DateTime? LeaveDate { get; set; }
    public bool IsArchived { get; set; }

    // Why this person would be left out of a run: no salary on file, or an
    // archive that happened before the period. Null when they are payable.
    public string? NotPayableReason { get; set; }

    // The same statutory gaps the run readiness check reports, computed off
    // the live profile so they can be fixed BEFORE a run is created rather
    // than after it refuses to submit.
    public IReadOnlyList<string> Missing { get; set; } = [];
}
