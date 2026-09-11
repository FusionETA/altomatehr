using System.IO.Compression;
using System.Text.Json;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Payroll.Pdf;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public class StatutoryFileService : IStatutoryFileService
{
    private readonly IPayrollRunRepository _runs;
    private readonly IPayslipRepository _payslips;
    private readonly IPayrollCompanyInfoRepository _companyInfo;
    private readonly IDirectoryService _directory;
    private readonly IOrganizationService _organizations;
    private readonly Common.ICurrentUser _currentUser;

    // Only for the bank file: PB's payor account lives on payroll settings,
    // not on Company Info.
    private readonly IPayrollSettingsService _settings;

    public StatutoryFileService(
        IPayrollRunRepository runs,
        IPayslipRepository payslips,
        IPayrollCompanyInfoRepository companyInfo,
        IDirectoryService directory,
        IOrganizationService organizations,
        Common.ICurrentUser currentUser,
        IPayrollSettingsService settings)
    {
        _runs = runs;
        _payslips = payslips;
        _companyInfo = companyInfo;
        _directory = directory;
        _organizations = organizations;
        _currentUser = currentUser;
        _settings = settings;
    }

    public async Task<StatutoryFileResult> RenderEpfCsvAsync(string runId) =>
        await RenderAsync(runId, EpfContributionCsv.Render);

    public async Task<StatutoryFileResult> RenderPerkesoTxtAsync(string runId) =>
        await RenderAsync(runId, PerkesoContributionTxt.Render);

    public async Task<StatutoryFileResult> RenderPcbTxtAsync(string runId) =>
        await RenderAsync(runId, PcbCp39Txt.Render);

    public async Task<PayrollRunReadiness.Result?> GetReadinessAsync(string runId)
    {
        var payload = await LoadAsync(runId);
        return payload is null ? null : PayrollRunReadiness.Check(payload);
    }

    // Every file this service produces is either filed with a regulator or
    // used to move money. A DRAFT's figures can still change — regenerating
    // rebuilds every payslip from scratch — so a file cut from one is a
    // submission the org cannot stand behind. Approval is the point the
    // figures become final, and it is the gate.
    //
    // Readiness deliberately does NOT go through this: its whole job is to
    // be asked BEFORE a run is approved.
    private static StatutoryFileResult? RefuseUnlessApproved(Entities.PayrollRun run) =>
        run.Status == Entities.PayrollRunStatus.SUBMITTED
            ? null
            : StatutoryFileResult.Refused(
                "This run has not been approved yet, so its figures can still change. "
                + "Submit and approve it before producing files.");

    // Null Content with a null Error is "no such run" — the controller's 404.
    private async Task<StatutoryFileResult> RenderAsync(
        string runId, Func<StatutoryRunPayload, StatutoryFileResult> render)
    {
        var payload = await LoadAsync(runId);
        if (payload is null) return new StatutoryFileResult(false, null, null, null, null);

        return RefuseUnlessApproved(payload.Run) ?? render(payload);
    }

    // ─── Documents ──────────────────────────────────────────────────────

    public async Task<StatutoryFileResult> RenderPayslipPdfAsync(
        string runId, string employeeProfileId)
    {
        var model = await LoadDocumentAsync(runId);
        if (model is null) return NotFound();
        if (RefuseUnlessApproved(model.Run) is { } refusal) return refusal;

        var row = model.Rows.FirstOrDefault(r =>
            string.Equals(r.Payslip.EmployeeProfileId, employeeProfileId, StringComparison.Ordinal));

        if (row is null)
        {
            return StatutoryFileResult.Refused("That employee has no payslip on this run.");
        }

        var (payslip, _) = await BuildPayslipAsync(model, row);

        return new StatutoryFileResult(
            true, PayslipFileName(model, row), PayslipPdf.Render(payslip),
            PayslipPdf.ContentType, null);
    }

    public async Task<StatutoryFileResult> RenderAllPayslipsZipAsync(string runId)
    {
        var model = await LoadDocumentAsync(runId);
        if (model is null) return NotFound();
        if (RefuseUnlessApproved(model.Run) is { } refusal) return refusal;

        if (model.Rows.Count == 0)
        {
            return StatutoryFileResult.Refused(
                "Generate this run's payslips before downloading them.");
        }

        // The YTD read is one query for the whole run, so it is hoisted out
        // rather than repeated per employee.
        var ytd = await _payslips.GetYtdThroughPeriodAsync(
            model.Run.PeriodYear, model.Run.PeriodMonth, model.Run.Id);
        var lineItems = (await _payslips.GetLineItemsForRunAsync(model.Run.Id))
            .GroupBy(li => li.PayslipId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Entities.PayslipLineItem>)g.ToList(),
                StringComparer.Ordinal);

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var row in model.Rows)
            {
                var entry = archive.CreateEntry(
                    PayslipFileName(model, row), CompressionLevel.Optimal);

                await using var stream = entry.Open();
                var pdf = PayslipPdf.Render(BuildPayslipModel(model, row, ytd, lineItems));
                await stream.WriteAsync(pdf);
            }
        }

        var fileName = $"payslips-{model.Run.PeriodYear}-{model.Run.PeriodMonth:D2}.zip";
        return new StatutoryFileResult(true, fileName, buffer.ToArray(), "application/zip", null);
    }

    public async Task<StatutoryFileResult> RenderSummaryPdfAsync(string runId)
    {
        var model = await LoadDocumentAsync(runId);
        if (model is null) return NotFound();
        if (RefuseUnlessApproved(model.Run) is { } refusal) return refusal;

        var fileName = $"payroll-summary-{model.Run.PeriodYear}-{model.Run.PeriodMonth:D2}.pdf";
        return new StatutoryFileResult(
            true, fileName, PayrollSummaryPdf.Render(model), PayrollSummaryPdf.ContentType, null);
    }

    public async Task<StatutoryFileResult> RenderPaymentSchedulePdfAsync(string runId)
    {
        var model = await LoadDocumentAsync(runId);
        if (model is null) return NotFound();
        if (RefuseUnlessApproved(model.Run) is { } refusal) return refusal;

        var fileName = $"payment-schedule-{model.Run.PeriodYear}-{model.Run.PeriodMonth:D2}.pdf";
        return new StatutoryFileResult(
            true, fileName, PaymentSchedulePdf.Render(model), PaymentSchedulePdf.ContentType, null);
    }

    // The LHDN MTD §E worksheet, one page per employee.
    //
    // Every figure is DESERIALISED from the payslip's stored breakdown. The
    // engine is deliberately not re-run: the snapshot is the month's law, and
    // recomputing it today would let a filed figure move.
    public async Task<StatutoryFileResult> RenderPcbDetailsPdfAsync(string runId)
    {
        var model = await LoadDocumentAsync(runId);
        if (model is null) return NotFound();
        if (RefuseUnlessApproved(model.Run) is { } refusal) return refusal;

        if (model.Rows.Count == 0)
        {
            return StatutoryFileResult.Refused(
                "Generate this run's payslips before downloading the PCB calculation details.");
        }

        var details = new PcbDetailsModel
        {
            OrganizationName = model.OrganizationName,
            PeriodLabel = model.PeriodLabel,
            Employees = [.. model.Rows.Select(ToPcbDetailsEmployee)],
        };

        var fileName = $"pcb-calculation-details-{model.Run.PeriodYear}-{model.Run.PeriodMonth:D2}.pdf";
        return new StatutoryFileResult(
            true, fileName, PcbCalculationDetailsPdf.Render(details),
            PcbCalculationDetailsPdf.ContentType, null);
    }

    private static PcbDetailsEmployee ToPcbDetailsEmployee(StatutoryEmployeeRow row) => new()
    {
        Name = row.EmployeeName,
        Position = row.Payslip.SnapshotPosition,
        EmployeeCode = row.EmployeeCode,
        Breakdown = DeserialiseBreakdown(row.Payslip.PcbCalculationJson),
    };

    // A snapshot that will not parse is a missing working, not a crash. The
    // page says so; the rest of the run still renders.
    private static PcbBreakdown? DeserialiseBreakdown(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<PcbBreakdown>(json, PayrollSnapshotJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<StatutoryFileResult> RenderBankFileAsync(string runId, DateTime? paymentDate)
    {
        var model = await LoadDocumentAsync(runId);
        if (model is null) return NotFound();
        if (RefuseUnlessApproved(model.Run) is { } refusal) return refusal;

        // Default to the last day of the period — the conventional pay date,
        // and never a date in a different month from the run.
        var valueDate = paymentDate?.Date ?? LastDayOfPeriod(model.Run);

        var settings = await _settings.GetAsync();

        return PayrollBankFileXlsx.Render(model, valueDate, settings.EcpPayorAccountNo);
    }

    private static DateTime LastDayOfPeriod(Entities.PayrollRun run) =>
        new DateTime(run.PeriodYear, run.PeriodMonth, 1).AddMonths(1).AddDays(-1);

    private static StatutoryFileResult NotFound() => new(false, null, null, null, null);

    // Sortable and recognisable inside a ZIP, and safe on both Windows and
    // macOS — an employee name can legally contain characters neither will
    // accept in a filename.
    private static string PayslipFileName(PayrollDocumentModel model, StatutoryEmployeeRow row)
    {
        var code = Sanitise(row.EmployeeCode);
        var name = Sanitise(row.EmployeeName);
        var period = $"{model.Run.PeriodMonth:D2}-{model.Run.PeriodYear}";

        var stem = string.IsNullOrEmpty(code) ? name : $"{code}_{name}";
        return $"{(string.IsNullOrEmpty(stem) ? "payslip" : stem)}_{period}.pdf";
    }

    private static string Sanitise(string value)
    {
        var cleaned = new string(value
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : ' ')
            .ToArray());

        return string.Join('_', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private async Task<(PayslipPdfModel Model, PayslipYtdSummary Ytd)> BuildPayslipAsync(
        PayrollDocumentModel model, StatutoryEmployeeRow row)
    {
        var ytd = await _payslips.GetYtdThroughPeriodAsync(
            model.Run.PeriodYear, model.Run.PeriodMonth, model.Run.Id);

        var lineItems = (await _payslips.GetLineItemsForRunAsync(model.Run.Id))
            .GroupBy(li => li.PayslipId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Entities.PayslipLineItem>)g.ToList(),
                StringComparer.Ordinal);

        var built = BuildPayslipModel(model, row, ytd, lineItems);
        return (built, built.Ytd);
    }

    private static PayslipPdfModel BuildPayslipModel(
        PayrollDocumentModel model,
        StatutoryEmployeeRow row,
        IReadOnlyDictionary<string, PayslipYtdSummary> ytd,
        IReadOnlyDictionary<string, IReadOnlyList<Entities.PayslipLineItem>> lineItems) => new()
        {
            Payslip = row.Payslip,
            OrganizationName = model.OrganizationName,
            PeriodLabel = model.PeriodLabel,
            IssueDate = model.IssueDate,
            Ytd = ytd.GetValueOrDefault(row.Payslip.EmployeeProfileId) ?? new PayslipYtdSummary(),
            LineItems = lineItems.GetValueOrDefault(row.Payslip.Id) ?? [],
            IdNumber = row.IdNumber,
            EpfNumber = row.EpfNumber,
            SocsoNumber = row.SocsoNumber,
            IncomeTaxNumber = row.IncomeTaxNumber,
            BankName = row.BankName,
            BankAccountNumber = row.BankAccountNumber,
            JoinDate = row.JoinDate,
        };

    public async Task<PayrollDocumentModel?> LoadDocumentAsync(string runId)
    {
        var payload = await LoadAsync(runId);
        if (payload is null) return null;

        var org = await _organizations.GetByIdAsync(_currentUser.OrganizationId ?? string.Empty);

        return new PayrollDocumentModel
        {
            Run = payload.Run,
            // The employer name on Company Info is the one that belongs on a
            // statutory document; the org's own name is the fallback.
            OrganizationName = FirstNonBlank(
                payload.CompanyInfo?.EmployerName, org?.Name) ?? string.Empty,
            PeriodLabel = PayrollPeriodLabel.For(payload.Run.PeriodYear, payload.Run.PeriodMonth),
            StatusLabel = StatusLabel(payload.Run.Status),
            IssueDate = LastDayOfPeriod(payload.Run),
            Rows = payload.Rows,
        };
    }

    private static string StatusLabel(Entities.PayrollRunStatus status) => status switch
    {
        Entities.PayrollRunStatus.DRAFT => "Draft — not yet approved",
        Entities.PayrollRunStatus.PENDING_APPROVAL => "Awaiting approval",
        _ => "Submitted",
    };

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

    // ─── Loading ────────────────────────────────────────────────────────

    public async Task<StatutoryRunPayload?> LoadAsync(string runId)
    {
        var run = await _runs.GetByIdAsync(runId);
        if (run is null) return null;

        var payslips = await _payslips.GetForRunAsync(run.Id);
        var companyInfo = await _companyInfo.GetAsync();

        // Identity is read LIVE rather than off the payslip snapshot. The
        // money is the month's filed figure and must not move; an EPF number
        // is a current fact about a person, and correcting a typo should reach
        // the next file rather than being frozen into a bad submission.
        var profiles = (await _directory.GetProfilesForCurrentOrgAsync())
            .ToDictionary(p => p.Id, StringComparer.Ordinal);
        var users = (await _directory.GetUsersAsync())
            .ToDictionary(u => u.Id, StringComparer.Ordinal);
        var memberships = (await _directory.GetMembershipsForCurrentOrgAsync())
            .ToDictionary(m => m.UserId, StringComparer.Ordinal);

        var rows = payslips
            .Select(p => ToRow(p, profiles.GetValueOrDefault(p.EmployeeProfileId), users, memberships))
            // Stable order so regenerating a file produces the same bytes.
            .OrderBy(r => r.EmployeeCode, StringComparer.Ordinal)
            .ThenBy(r => r.EmployeeName, StringComparer.Ordinal)
            .ToList();

        return new StatutoryRunPayload
        {
            Run = run,
            CompanyInfo = companyInfo,
            Rows = rows,
        };
    }

    private static StatutoryEmployeeRow ToRow(
        Payslip payslip,
        Employees.Entities.EmployeeProfile? profile,
        IReadOnlyDictionary<string, Auth.Entities.User> users,
        IReadOnlyDictionary<string, Employees.Entities.OrganizationMembership> memberships)
    {
        // A profile archived or deleted since the run was generated leaves the
        // payslip's own snapshot as the only identity there is. Better a file
        // naming them from the snapshot than a row silently dropped.
        var membership = profile is not null
            ? memberships.GetValueOrDefault(profile.UserId)
            : null;

        var name = profile is not null && users.TryGetValue(profile.UserId, out var user)
            ? user.Name
            : payslip.SnapshotName;

        return new StatutoryEmployeeRow
        {
            Payslip = payslip,
            EmployeeName = string.IsNullOrWhiteSpace(name) ? payslip.SnapshotName : name,
            EmployeeCode = membership?.EmployeeNumber
                           ?? payslip.SnapshotEmployeeNumber
                           ?? string.Empty,
            IdNumber = profile?.IdNumber,
            IdType = profile?.IdType,
            EpfNumber = profile?.EpfNumber,
            SocsoNumber = profile?.SocsoNumber,
            SsfwNumber = profile?.SsfwNumber,
            IncomeTaxNumber = profile?.IncomeTaxNumber,
            Nationality = profile?.Nationality ?? payslip.SnapshotNationality,
            HasPr = profile?.HasPr ?? false,
            Gender = profile?.Gender,
            MaritalStatus = profile?.MaritalStatus,

            PaymentMethod = profile?.PaymentMethod ?? Employees.Entities.PaymentMethod.BANK_TRANSFER,
            BankName = profile?.BankName,
            BankAccountNumber = profile?.BankAccountNumber,
            BankAccountHolderName = profile?.BankAccountHolderName,
            JoinDate = profile?.JoinDate,
        };
    }
}
