using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class PayslipPasswordService : IPayslipPasswordService
{
    private readonly IEmployeeProfileRepository _profiles;
    private readonly IPayslipRepository _payslips;
    private readonly IUserRepository _users;
    private readonly IPayrollSettingsService _settings;
    private readonly ISecretBox _secrets;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;

    public PayslipPasswordService(
        IEmployeeProfileRepository profiles,
        IPayslipRepository payslips,
        IUserRepository users,
        IPayrollSettingsService settings,
        ISecretBox secrets,
        IAuditService audit,
        ICurrentUser currentUser)
    {
        _profiles = profiles;
        _payslips = payslips;
        _users = users;
        _settings = settings;
        _secrets = secrets;
        _audit = audit;
        _currentUser = currentUser;
    }

    // Can't be made or read here: no secrets key, or a password saved under a
    // key this server no longer has.
    public const string Unreadable =
        "This payslip password can't be read on this server, so payslips can't be locked. Contact support.";

    public async Task<string?> ForLockingAsync(string employeeProfileId)
    {
        var profile = await _profiles.GetByIdAsync(employeeProfileId);
        return profile is null ? null : await StoredOrNewAsync(profile);
    }

    public async Task<PayslipPasswordResult> GetMineAsync()
    {
        var profile = await MyProfileAsync();
        if (profile is null) return PayslipPasswordResult.NotFound;
        return await ShowAsync(profile);
    }

    public async Task<PayslipPasswordResult> ChangeMineAsync(string newPassword)
    {
        var profile = await MyProfileAsync();
        if (profile is null) return PayslipPasswordResult.NotFound;

        if (PayslipLock.Problem(newPassword) is { } problem) return new(false, null, problem);

        // The payslip password travels with every file they forward; their
        // sign-in password must never be the one that does.
        var user = await _users.GetByIdAsync(profile.UserId);
        if (user is not null && PasswordHasher.Verify(newPassword, user.PasswordHash))
        {
            return new(false, null,
                "Choose a different password from the one you sign in with — this one goes with every payslip you send on.");
        }

        if (_secrets.Encrypt(newPassword) is not { } encrypted) return new(false, null, Unreadable);
        await _profiles.SetPayslipPasswordAsync(profile.Id, encrypted);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayslipPasswordChange,
            "Changed their payslip password",
            TargetType: "EmployeeProfile",
            TargetId: profile.Id));

        return new(true, null, null);
    }

    public async Task<PayslipPasswordResult> RevealAsync(string runId, string employeeProfileId)
    {
        var profile = await OnRunAsync(runId, employeeProfileId);
        if (profile is null) return PayslipPasswordResult.NotFound;

        var result = await ShowAsync(profile);
        if (result.Ok)
        {
            var name = await NameOfAsync(profile);
            await _audit.WriteAsync(new AuditEvent(
                AuditActions.PayrollPayslipPasswordReveal,
                $"Showed {name}'s payslip password",
                TargetType: "EmployeeProfile",
                TargetId: profile.Id,
                Metadata: new { EmployeeName = name, PayrollRunId = runId }));
        }
        return result;
    }

    public async Task<PayslipPasswordResult> ResetAsync(string runId, string employeeProfileId)
    {
        var profile = await OnRunAsync(runId, employeeProfileId);
        if (profile is null) return PayslipPasswordResult.NotFound;

        var password = PayslipLock.NewRandomPassword();
        if (_secrets.Encrypt(password) is not { } encrypted) return new(false, null, Unreadable);
        await _profiles.SetPayslipPasswordAsync(profile.Id, encrypted);

        var name = await NameOfAsync(profile);
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.PayrollPayslipPasswordReset,
            $"Reset {name}'s payslip password to a new random one",
            TargetType: "EmployeeProfile",
            TargetId: profile.Id,
            Metadata: new { EmployeeName = name, PayrollRunId = runId }));

        return new(true, password, null);
    }

    // Theirs if they have one. Otherwise made now — but only while payslips
    // are locked: with locking off there is nothing to open, and the page
    // then shows no password at all.
    private async Task<PayslipPasswordResult> ShowAsync(EmployeeProfile profile)
    {
        if (profile.PayslipPasswordEncrypted is null && !(await _settings.GetEffectiveAsync()).LockPayslips)
            return PayslipPasswordResult.NotFound;

        var password = await StoredOrNewAsync(profile);
        return password is null ? new(false, null, Unreadable) : new(true, password, null);
    }

    private async Task<string?> StoredOrNewAsync(EmployeeProfile profile)
    {
        if (profile.PayslipPasswordEncrypted is { } stored) return _secrets.Decrypt(stored);

        if (_secrets.Encrypt(PayslipLock.NewRandomPassword()) is not { } made) return null;
        // Whoever stores first wins; everyone reads back the one that's kept.
        var kept = await _profiles.SetPayslipPasswordIfNoneAsync(profile.Id, made) ?? made;
        profile.PayslipPasswordEncrypted = kept;
        return _secrets.Decrypt(kept);
    }

    // Only someone on that run: the run's page is where an admin reaches this
    // from, and a profile id from elsewhere answers not found.
    private async Task<EmployeeProfile?> OnRunAsync(string runId, string employeeProfileId)
    {
        var onRun = (await _payslips.GetForRunAsync(runId))
            .Any(p => string.Equals(p.EmployeeProfileId, employeeProfileId, StringComparison.Ordinal));
        return onRun ? await _profiles.GetByIdAsync(employeeProfileId) : null;
    }

    private async Task<EmployeeProfile?> MyProfileAsync() =>
        _currentUser.UserId is { } userId ? await _profiles.GetByUserAsync(userId) : null;

    private async Task<string> NameOfAsync(EmployeeProfile profile)
    {
        var user = await _users.GetByIdAsync(profile.UserId);
        return PersonName.Display(user?.Name, user?.Email, "an employee");
    }
}
