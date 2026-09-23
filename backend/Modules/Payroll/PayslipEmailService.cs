using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Email;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class PayslipEmailService : IPayslipEmailService
{
    private const string NotSubmittedMessage =
        "This run has not been approved yet, so its figures can still change. "
        + "Submit and approve it before emailing payslips.";

    private readonly IPayslipRepository _payslips;
    private readonly IPayrollRunRepository _runs;
    private readonly IStatutoryFileService _statutory;
    private readonly IEmployeeRowResolver _employees;
    private readonly IOrganizationRepository _organizations;
    private readonly ICurrentUser _currentUser;
    private readonly IEmailSender _email;
    private readonly IAuditService _audit;

    public PayslipEmailService(
        IPayslipRepository payslips,
        IPayrollRunRepository runs,
        IStatutoryFileService statutory,
        IEmployeeRowResolver employees,
        IOrganizationRepository organizations,
        ICurrentUser currentUser,
        IEmailSender email,
        IAuditService audit)
    {
        _payslips = payslips;
        _runs = runs;
        _statutory = statutory;
        _employees = employees;
        _organizations = organizations;
        _currentUser = currentUser;
        _email = email;
        _audit = audit;
    }

    public async Task<PayslipEmailResult> EmailPayslipAsync(string runId, string employeeProfileId)
    {
        var run = await _runs.GetByIdAsync(runId);
        if (run is null) return new PayslipEmailResult(false, "Run not found.");
        if (run.Status != PayrollRunStatus.SUBMITTED)
            return new PayslipEmailResult(false, NotSubmittedMessage);

        var payslip = (await _payslips.GetForRunAsync(runId))
            .FirstOrDefault(p => p.EmployeeProfileId == employeeProfileId);
        if (payslip is null)
            return new PayslipEmailResult(false, "That employee has no payslip on this run.");

        var directory = await _employees.GetSnapshotAsync();
        var (ok, reason) = await SendOneAsync(payslip, run, directory);
        return new PayslipEmailResult(ok, reason);
    }

    public async Task<PayslipEmailBulkResult> EmailPayslipsForRunAsync(string runId)
    {
        var run = await _runs.GetByIdAsync(runId);
        if (run is null) return new PayslipEmailBulkResult(0, [], "Run not found.");
        if (run.Status != PayrollRunStatus.SUBMITTED)
            return new PayslipEmailBulkResult(0, [], NotSubmittedMessage);

        var payslips = await _payslips.GetForRunAsync(runId);
        var directory = await _employees.GetSnapshotAsync();
        var sent = 0;
        var failed = new List<PayslipEmailFailure>();

        // Sequential, not Task.WhenAll — one employee's send failing must not
        // stop or skip the rest, and a burst of concurrent calls to the mail
        // provider is worth avoiding regardless.
        foreach (var payslip in payslips)
        {
            var name = directory.NameOf(payslip.UserId) is { Length: > 0 } n ? n : payslip.SnapshotName;
            var (ok, reason) = await SendOneAsync(payslip, run, directory);
            if (ok) sent++;
            else failed.Add(new PayslipEmailFailure(name, reason ?? "Send failed."));
        }

        return new PayslipEmailBulkResult(sent, failed, null);
    }

    // Never throws — an email failure must not become a 500 for an action
    // that otherwise fully succeeded (same reasoning as
    // EmployeeService.TrySendWelcomeAsync).
    private async Task<(bool Ok, string? Reason)> SendOneAsync(
        Payslip payslip, PayrollRun run, EmployeeRowIndex directory)
    {
        var toEmail = directory.EmailOf(payslip.UserId);
        if (string.IsNullOrWhiteSpace(toEmail))
            return (false, "No email address on file for this employee.");

        try
        {
            var file = await _statutory.RenderPayslipPdfAsync(run.Id, payslip.EmployeeProfileId);
            if (!file.Ok || file.Content is null)
                return (false, file.Error ?? "Could not generate the payslip PDF.");

            var org = await _organizations.GetByIdAsync(_currentUser.OrganizationId ?? "");
            var periodLabel = PayrollPeriodLabel.For(run.PeriodYear, run.PeriodMonth);
            var name = directory.NameOf(payslip.UserId) is { Length: > 0 } n ? n : payslip.SnapshotName;
            var html = PayslipEmailTemplate.BuildHtml(name, org?.Name ?? "AltomateHR", periodLabel);

            var delivered = await _email.SendAsync(
                toEmail,
                PayslipEmailTemplate.Subject(periodLabel),
                html,
                attachments: [new EmailAttachment(file.FileName!, file.Content)]);

            if (!delivered) return (false, "The mail provider rejected the message.");

            await _audit.WriteAsync(new AuditEvent(
                AuditActions.PayrollRunPayslipEmail,
                $"{name} — {periodLabel}",
                TargetType: "PayrollRun",
                TargetId: run.Id,
                Metadata: new { EmployeeName = name, PeriodLabel = periodLabel }));

            return (true, null);
        }
        catch
        {
            return (false, "Send failed unexpectedly.");
        }
    }
}
