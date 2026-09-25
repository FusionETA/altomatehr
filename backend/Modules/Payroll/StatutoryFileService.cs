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

    // The file name carries the day it was generated. UTC, because the previous
    // system dated it off its server clock and that server (Vercel) runs in
    // UTC — so between midnight and 8am Malaysian time it named the file with
    // the previous day, and so does this.
    public async Task<StatutoryFileResult> RenderEpfCsvAsync(string runId) =>
        await RenderAsync(runId, payload =>
            EpfContributionCsv.Render(payload, DateOnly.FromDateTime(DateTime.UtcNow)));

    public async Task<StatutoryFileResult> RenderPerkesoTxtAsync(string runId) =>
        await RenderAsync(runId, payload => PerkesoContributionTxt.Render(payload, PerkesoLayout.SocsoEis));

    public async Task<StatutoryFileResult> RenderPerkesoSkbbkTxtAsync(string runId) =>
        await RenderAsync(runId, payload => PerkesoContributionTxt.Render(payload, PerkesoLayout.SocsoEisSkbbk));

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
    // An IMPORTED run's figures are the uploaded sheet as typed — they never
    // went through the calculator. A payslip is a faithful render of what was
    // stored, so it stays downloadable. Everything else would assert
    // engine-derived numbers to a third party — LHDN, KWSP, PERKESO or the
    // bank — off figures this system never computed, and whose statutory
    // filings and payments the PREVIOUS system already made. Producing them
    // here invites a double submission and a second payment run.
    //
    // Refused at the service, not hidden in the UI: this is the one chokepoint
    // every download route passes through, so no direct URL gets around it.
    private static StatutoryFileResult? RefuseIfImported(Entities.PayrollRun run) =>
        run.Source == Entities.PayrollRunSource.IMPORTED
            ? StatutoryFileResult.Refused(
                "This month was imported from your previous payroll system, so its figures "
                + "were never calculated here. Payslips are available; statutory files, the "
                + "bank file and the run reports are not — the system that produced these "
                + "figures is the one that files and pays them.")
            : null;

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

        return RefuseUnlessApproved(payload.Run)
               ?? RefuseIfImported(payload.Run)
               ?? render(payload);
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
                "Run payroll before downloading the payslips.");
        }

        // The YTD read is one query for the whole run, so it is hoisted out
        // rather than repeated per employee.
        var ytd = await _payslips.GetYtdThroughPeriodAsync(
            model.Run.PeriodYear, model.Run.PeriodMonth, model.Run.Id);
        var lineItems = (await _payslips.GetLineItemsForRunAsync(model.Run.Id))
            .GroupBy(li => li.PayslipId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Entities.PayslipLineItem>)g.ToList(),
                StringComparer.Ordinal);

        // Two employees can sanitise to the same name; the second becomes
        // "…_2.pdf", as the previous system numbered them.
        var used = new HashSet<string>(StringComparer.Ordinal);

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var row in model.Rows)
            {
                var baseName = PayslipFileName(model, row);
                var name = baseName;
                for (var n = 2; !used.Add(name); n++)
                    name = baseName[..^".pdf".Length] + $"_{n}.pdf";

                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);

                await using var stream = entry.Open();
                var pdf = PayslipPdf.Render(BuildPayslipModel(model, row, ytd, lineItems));
                await stream.WriteAsync(pdf);
            }
        }

        var fileName = $"Payslips_{model.Run.PeriodYear}_{model.Run.PeriodMonth:D2}_All.zip";
        return new StatutoryFileResult(true, fileName, buffer.ToArray(), "application/zip", null);
    }

    // What goes in the bundle, in the order an admin works through them: pay
    // the people, then file the returns. The bank file leads because it is the
    // one with a deadline attached.
    private static readonly string[] BundleDocuments =
        ["bank-file", "summary", "payslips", "epf", "socso-eis", "socso-eis-skbbk", "pcb"];

    public async Task<PayrollBundleResult> RenderRunBundleAsync(string runId, DateTime? paymentDate)
    {
        // Checked once, up front. Every renderer below would refuse an
        // unapproved run individually, and a bundle of six identical refusals
        // is a worse answer than one.
        var model = await LoadDocumentAsync(runId);
        if (model is null)
            return new PayrollBundleResult(false, null, null, [], new Dictionary<string, string>(), null);

        if (RefuseUnlessApproved(model.Run) is { Error: { } refusal })
            return new PayrollBundleResult(false, null, null, [], new Dictionary<string, string>(), refusal);

        var included = new List<string>();
        var skipped = new Dictionary<string, string>(StringComparer.Ordinal);

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var document in BundleDocuments)
            {
                // One document failing must not cost the other five. An org
                // with no payor account still needs its statutory files.
                StatutoryFileResult result;
                try
                {
                    result = await RenderBundleDocumentAsync(document, runId, paymentDate);
                }
                catch (Exception ex)
                {
                    skipped[document] = ex.Message;
                    continue;
                }

                if (!result.Ok || result.Content is null)
                {
                    skipped[document] = result.Error ?? "Could not be produced for this run.";
                    continue;
                }

                var entry = archive.CreateEntry(result.FileName!, CompressionLevel.Optimal);
                await using var stream = entry.Open();
                await stream.WriteAsync(result.Content);
                included.Add(document);
            }
        }

        var fileName = $"payroll-{model.Run.PeriodYear}-{model.Run.PeriodMonth:D2}.zip";
        return new PayrollBundleResult(true, fileName, buffer.ToArray(), included, skipped, null);
    }

    private Task<StatutoryFileResult> RenderBundleDocumentAsync(
        string document, string runId, DateTime? paymentDate) => document switch
        {
            "bank-file" => RenderBankFileAsync(runId, paymentDate),
            "summary" => RenderSummaryPdfAsync(runId),
            "payslips" => RenderAllPayslipsZipAsync(runId),
            "epf" => RenderEpfCsvAsync(runId),
            "socso-eis" => RenderPerkesoTxtAsync(runId),
            "socso-eis-skbbk" => RenderPerkesoSkbbkTxtAsync(runId),
            "pcb" => RenderPcbTxtAsync(runId),
            _ => Task.FromResult(NotFound()),
        };

    public async Task<StatutoryFileResult> RenderSummaryPdfAsync(string runId)
    {
        var model = await LoadDocumentAsync(runId);
        if (model is null) return NotFound();
        if (RefuseUnlessApproved(model.Run) is { } refusal) return refusal;
        if (RefuseIfImported(model.Run) is { } imported) return imported;

        if (model.Rows.Count == 0)
        {
            return StatutoryFileResult.Refused(
                "Run payroll before downloading the summary — there are no payslips on this run.");
        }

        // The summary itemises every payslip's lines under the employee's name.
        var lineItems = (await _payslips.GetLineItemsForRunAsync(model.Run.Id))
            .GroupBy(li => li.PayslipId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Entities.PayslipLineItem>)g.ToList(),
                StringComparer.Ordinal);

        var summary = model with
        {
            LineItems = lineItems,
            GeneratedAt = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                DateTime.UtcNow, Attendance.AttendanceTime.DefaultTimeZone),
        };

        var fileName = $"Payroll_Summary_{MonthYear(model.Run)}.pdf";
        return new StatutoryFileResult(
            true, fileName, PayrollSummaryPdf.Render(summary), PayrollSummaryPdf.ContentType, null);
    }

    public async Task<StatutoryFileResult> RenderPaymentSchedulePdfAsync(string runId)
    {
        var model = await LoadDocumentAsync(runId);
        if (model is null) return NotFound();
        if (RefuseUnlessApproved(model.Run) is { } refusal) return refusal;
        if (RefuseIfImported(model.Run) is { } imported) return imported;

        var fileName = $"Payment_Schedule_{MonthYear(model.Run)}.pdf";
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
        if (RefuseIfImported(model.Run) is { } imported) return imported;

        if (model.Rows.Count == 0)
        {
            return StatutoryFileResult.Refused(
                "Run payroll before downloading the PCB calculation details.");
        }

        var details = new PcbDetailsModel
        {
            OrganizationName = model.OrganizationName,
            PeriodLabel = model.PeriodLabel,
            Employees = [.. model.Rows.Select(ToPcbDetailsEmployee)],
        };

        var fileName = $"PCB_Calculation_Details_{MonthYear(model.Run)}.pdf";
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
        VoluntaryPcb = row.Payslip.VoluntaryPcb,
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

    public async Task<StatutoryFileResult> RenderBankFileAsync(
        string runId,
        DateTime? paymentDate,
        string? recipientReference = null,
        HlbChannel? channel = null)
    {
        var model = await LoadDocumentAsync(runId);
        if (model is null) return NotFound();
        if (RefuseUnlessApproved(model.Run) is { } refusal) return refusal;
        if (RefuseIfImported(model.Run) is { } imported) return imported;

        // Default to the last day of the period — the conventional pay date,
        // and never a date in a different month from the run.
        var valueDate = paymentDate?.Date ?? LastDayOfPeriod(model.Run);

        var settings = await _settings.GetAsync();

        // Routed on the company's own bank, because that decides which portal
        // the file goes to and therefore which layout it must be in. Until
        // now this always rendered Public Bank's ECP spreadsheet: a Maybank or
        // CIMB customer got a file their portal rejects, with nothing saying
        // why. Refusing is the smaller failure — it names the setting to fix.
        var format = PayrollDisbursement.FormatFor(settings.PayrollBankName);

        if (format is null)
        {
            var configured = settings.PayrollBankName?.Trim();

            // "Other" is a decision, not an omission: the admin looked at the
            // list and none applied. Say so rather than nagging them to pick.
            if (string.Equals(configured, PayrollDisbursement.OtherBank, StringComparison.OrdinalIgnoreCase))
            {
                return StatutoryFileResult.Refused(
                    "This company's payroll bank is set to \"Other\", so there is no bulk-upload "
                    + "file to generate. Pay the salaries through your bank's own process — the "
                    + "Payment Schedule PDF lists every account and amount.");
            }

            return StatutoryFileResult.Refused(
                string.IsNullOrEmpty(configured)
                    ? "No payroll bank is set. Choose one in Payroll Settings → Company Info — it "
                      + "decides which bank's upload file this generates."
                    : $"No upload file is generated for \"{configured}\". Choose a supported bank "
                      + "in Payroll Settings → Company Info, or pick \"Other\" if yours isn't listed.");
        }

        return format switch
        {
            PayrollFileFormat.PbEcpXlsx =>
                PayrollBankFileXlsx.Render(model, valueDate, settings.EcpPayorAccountNo),

            PayrollFileFormat.MbbM2eTxt =>
                PayrollBankFileMbbTxt.Render(
                    model, valueDate, settings.EcpPayorAccountNo, settings.PayorOrganisationCode),

            PayrollFileFormat.CimbBizChannelTxt =>
                PayrollBankFileCimbTxt.Render(model, valueDate, settings.PayorOrganisationCode),

            // The only bank with two portals. Guessing one would hand over a
            // file the other rejects, so an unspecified channel is refused
            // with both names rather than defaulted.
            PayrollFileFormat.HlbConnect => channel switch
            {
                HlbChannel.ConnectFirst =>
                    PayrollBankFileHlb.RenderConnectFirstTxt(model, recipientReference),
                HlbChannel.ConnectBiz =>
                    PayrollBankFileHlb.RenderConnectBizXlsx(model, recipientReference),
                _ => StatutoryFileResult.Refused(
                    "Hong Leong has two upload channels and they take different files. Choose "
                    + "Connect First or ConnectBiz in the download panel — whichever one your "
                    + "company uses to submit bulk payroll."),
            },

            _ => StatutoryFileResult.Refused(
                $"The upload file for {settings.PayrollBankName} isn't available yet. Use the "
                + "Payment Schedule PDF for now — it lists every account and amount."),
        };
    }

    private static DateTime LastDayOfPeriod(Entities.PayrollRun run) =>
        new DateTime(run.PeriodYear, run.PeriodMonth, 1).AddMonths(1).AddDays(-1);

    private static StatutoryFileResult NotFound() => new(false, null, null, null, null);

    // The previous system's names: "Payroll_Summary_January_2026.pdf".
    private static string MonthYear(Entities.PayrollRun run) =>
        $"{System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(run.PeriodMonth)}_{run.PeriodYear}";

    // {employeeId}_{name}_{MM-YYYY}.pdf, the previous system's pattern, off the
    // payslip's SNAPSHOT identity as it did — so the file an employee was
    // emailed keeps its name after they are renamed.
    private static string PayslipFileName(PayrollDocumentModel model, StatutoryEmployeeRow row)
    {
        var id = SanitiseFileName(row.Payslip.SnapshotEmployeeNumber ?? string.Empty);
        var name = SanitiseFileName(row.Payslip.SnapshotName);
        var period = $"{model.Run.PeriodMonth:D2}-{model.Run.PeriodYear}";

        return $"{(id.Length == 0 ? "Employee" : id)}_{(name.Length == 0 ? "Unnamed" : name)}_{period}.pdf";
    }

    // Drop the characters Windows rejects, turn whitespace runs into "_",
    // collapse repeated "_", trim them off the ends, cap at 80 — exactly the
    // previous system's sanitiser.
    private static string SanitiseFileName(string raw)
    {
        var kept = new string(raw.Where(c => c is not ('<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')).ToArray());
        var underscored = System.Text.RegularExpressions.Regex.Replace(kept, @"\s+", "_");
        var collapsed = System.Text.RegularExpressions.Regex.Replace(underscored, "_+", "_").Trim('_');
        return collapsed.Length > 80 ? collapsed[..80] : collapsed;
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
            // Culture-aware like the previous system's localeCompare, so the
            // rows come out in the order it filed them ("emp2" beside "EMP1").
            // Ties fall back to the snapshot employee number — the order the
            // previous system loaded payslips in before its stable sort.
            .OrderBy(r => r.EmployeeCode, StringComparer.InvariantCulture)
            .ThenBy(r => r.Payslip.SnapshotEmployeeNumber, StringComparer.OrdinalIgnoreCase)
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
            // The live name even when blank, as the previous system took it.
            EmployeeName = name,
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
