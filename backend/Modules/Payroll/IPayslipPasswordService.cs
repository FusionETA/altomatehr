namespace AltomateHR.Api.Modules.Payroll;

// A person's payslip password: ONE per person per company
// (EmployeeProfile.PayslipPasswordEncrypted). It starts random, made the first
// time it's needed; the employee can change it on their Payslips page; a
// payroll admin can see it or reset it to a new random one. Every copy of
// every payslip of theirs is locked with the current one.
public interface IPayslipPasswordService
{
    // The password to lock this person's payslip with — theirs, made random
    // now if they have none. Null when it can't be made or read on this
    // server (no secrets key, or one saved under a key it no longer has).
    Task<string?> ForLockingAsync(string employeeProfileId);

    // The caller's own. Not found when they have no profile in this company,
    // or have no password yet and payslips aren't locked (nothing to open).
    Task<PayslipPasswordResult> GetMineAsync();

    // The caller sets their own. Error says why it was refused.
    Task<PayslipPasswordResult> ChangeMineAsync(string newPassword);

    // A payroll admin, from a run's payslips: someone on that run. Audited.
    Task<PayslipPasswordResult> RevealAsync(string runId, string employeeProfileId);

    // A payroll admin replaces it with a new random one, and is shown it so
    // they can pass it on. Audited.
    Task<PayslipPasswordResult> ResetAsync(string runId, string employeeProfileId);
}
