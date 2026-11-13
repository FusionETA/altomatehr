using System.Globalization;
using System.Net;
using AltomateHR.Api.Modules.DocumentExpiry.Entities;
using AltomateHR.Api.Modules.Email;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Notifications;
using AltomateHR.Api.Modules.Notifications.Entities;
using AltomateHR.Api.Modules.NotificationSettings;
using AltomateHR.Api.Modules.NotificationSettings.Entities;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.DocumentExpiry;

// The daily work permit and passport reminders. For each company:
//
//   · Who: current foreign staff (ExpiringDocuments — the same rule as the
//     dashboard's "Documents expiring" card).
//   · When: 30 days before (or the first day it's within 30 days, for a date
//     entered late), on the day, and once if it had already expired when
//     first seen. Each once per expiry date — a renewed date starts over.
//   · To: the person (in the app + push, and email when Settings →
//     Notifications says so) and the admins chosen there (in the app + push,
//     and one email listing the day's reminders). An admin whose access is
//     limited only hears about the staff it covers.
public class DocumentExpiryReminderService : IDocumentExpiryReminderService
{
    public const int DaysBefore = 30;

    // More than this for one admin in one day becomes one "N documents" note
    // in the bell rather than a push per document (the email lists them all).
    private const int MaxSeparateAdminNotes = 3;

    private readonly IDocumentExpiryRepository _repo;
    private readonly INotificationSettingService _settings;
    private readonly IDirectoryService _directory;
    private readonly IEmployeeScopeResolver _scope;
    private readonly INotificationService _notifications;
    private readonly IEmailSender _email;
    private readonly PortalOptions _portal;
    private readonly ILogger<DocumentExpiryReminderService> _logger;

    public DocumentExpiryReminderService(
        IDocumentExpiryRepository repo,
        INotificationSettingService settings,
        IDirectoryService directory,
        IEmployeeScopeResolver scope,
        INotificationService notifications,
        IEmailSender email,
        IOptions<PortalOptions> portal,
        ILogger<DocumentExpiryReminderService> logger)
    {
        _repo = repo;
        _settings = settings;
        _directory = directory;
        _scope = scope;
        _notifications = notifications;
        _email = email;
        _portal = portal.Value;
        _logger = logger;
    }

    // Which reminder a document is due, given those already sent for its
    // current expiry date. Null = nothing today.
    public static DocumentReminderStage? StageFor(int daysLeft, IReadOnlyCollection<DocumentReminderStage> sent)
    {
        if (daysLeft > DaysBefore) return null;
        if (daysLeft > 0) return sent.Contains(DocumentReminderStage.BEFORE) ? null : DocumentReminderStage.BEFORE;
        if (daysLeft == 0) return sent.Contains(DocumentReminderStage.ON_THE_DAY) ? null : DocumentReminderStage.ON_THE_DAY;
        // Past it: once, unless the on-the-day reminder already said so.
        return sent.Contains(DocumentReminderStage.ON_THE_DAY) || sent.Contains(DocumentReminderStage.EXPIRED)
            ? null
            : DocumentReminderStage.EXPIRED;
    }

    public async Task<DocumentExpiryRunResult> RunAsync(DateTime today)
    {
        var day = today.Date;
        int companies = 0, reminders = 0, adminEmails = 0, staffEmails = 0;

        foreach (var (orgId, orgName) in await _repo.GetCompaniesAsync())
        {
            try
            {
                var sent = await RunCompanyAsync(orgId, orgName, day);
                if (sent.Reminders == 0) continue;
                companies++;
                reminders += sent.Reminders;
                adminEmails += sent.AdminEmails;
                staffEmails += sent.StaffEmails;
            }
            catch (Exception ex)
            {
                // One company's failure must not stop the others.
                _logger.LogError(ex, "Document expiry reminders failed for organization {OrgId}.", orgId);
            }
        }

        return new DocumentExpiryRunResult(companies, reminders, adminEmails, staffEmails);
    }

    private sealed record Due(ExpiringDocument Doc, DocumentReminderStage Stage, string PersonName);

    private async Task<(int Reminders, int AdminEmails, int StaffEmails)> RunCompanyAsync(
        string orgId, string orgName, DateTime today)
    {
        var setting = await _settings.ForOrgAsync(orgId, NotificationKind.DOCUMENT_EXPIRY);
        if (!setting.Enabled) return (0, 0, 0);

        var docs = ExpiringDocuments.Due(await _repo.GetDocumentsAsync(orgId), today, DaysBefore);
        if (docs.Count == 0) return (0, 0, 0);

        var sentBefore = (await _repo.GetSentAsync(orgId))
            .ToLookup(r => (r.UserId, r.Document, r.ExpiryDate.Date), r => r.Stage);

        var users = new Dictionary<string, Auth.Entities.User?>(StringComparer.Ordinal);
        async Task<Auth.Entities.User?> UserAsync(string id)
        {
            if (!users.TryGetValue(id, out var user)) users[id] = user = await _directory.GetUserAsync(id);
            return user;
        }

        var due = new List<Due>();
        foreach (var doc in docs)
        {
            var stage = StageFor(doc.DaysLeft, [.. sentBefore[(doc.UserId, doc.Document, doc.Expiry.Date)]]);
            if (stage is null) continue;
            var name = (await UserAsync(doc.UserId))?.Name;
            due.Add(new Due(doc, stage.Value, string.IsNullOrWhiteSpace(name) ? "A staff member" : name.Trim()));
        }
        if (due.Count == 0) return (0, 0, 0);

        // Recorded before sending, so a crash part-way can't send any of
        // these twice. A reminder that then fails to send is not retried.
        var now = DateTime.UtcNow;
        await _repo.AddSentAsync([.. due.Select(d => new DocumentExpiryReminder
        {
            OrganizationId = orgId,
            UserId = d.Doc.UserId,
            Document = d.Doc.Document,
            ExpiryDate = d.Doc.Expiry.Date,
            Stage = d.Stage,
            SentAt = now,
        })]);

        // The person.
        var staffEmails = 0;
        foreach (var d in due)
        {
            var title = StaffTitle(d);
            await _notifications.NotifyAsync(orgId, d.Doc.UserId, NotificationType.DOCUMENT_EXPIRY,
                title, StaffBody(d), url: null);

            if (setting.EmailStaff && (await UserAsync(d.Doc.UserId))?.Email is { Length: > 0 } to
                && await _email.SendAsync(to, title, StaffEmail(d, orgName)))
                staffEmails++;
        }

        // The admins: each chosen one gets the staff their access covers.
        // Anyone none of them covers (every chosen admin limited to other
        // staff) goes to the owner, so no reminder reaches no admin at all.
        var recipients = new List<(string UserId, List<Due> Items)>();
        var told = new HashSet<Due>();
        foreach (var admin in setting.Admins)
        {
            var covered = admin.PolicyScope is null
                ? null
                : (await _scope.ResolveAsync(orgId, admin.UserId, admin.PolicyScope)).UserIds
                    .ToHashSet(StringComparer.Ordinal);
            var theirs = due.Where(d => covered is null || covered.Contains(d.Doc.UserId)).ToList();
            told.UnionWith(theirs);
            if (theirs.Count > 0) recipients.Add((admin.UserId, theirs));
        }
        var nobodys = due.Where(d => !told.Contains(d)).ToList();
        if (nobodys.Count > 0)
        {
            foreach (var owner in setting.Owners)
            {
                var i = recipients.FindIndex(r => r.UserId == owner);
                if (i >= 0) recipients[i] = (owner, [.. recipients[i].Items, .. nobodys]);
                else recipients.Add((owner, nobodys));
            }
        }

        var adminEmails = 0;
        foreach (var (adminId, theirs) in recipients)
        {
            if (theirs.Count <= MaxSeparateAdminNotes)
            {
                foreach (var d in theirs)
                    await _notifications.NotifyAsync(orgId, adminId, NotificationType.DOCUMENT_EXPIRY,
                        AdminTitle(d), AdminBody(d), AdminUrl(d));
            }
            else
            {
                await _notifications.NotifyAsync(orgId, adminId, NotificationType.DOCUMENT_EXPIRY,
                    $"{theirs.Count} documents need renewing",
                    "Work permits and passports expiring or expired — see the email or the dashboard's Documents expiring card.",
                    "/overview");
            }

            if ((await UserAsync(adminId)) is { Email.Length: > 0 } adminUser
                && await _email.SendAsync(adminUser.Email, AdminSubject(theirs.Count, orgName),
                    AdminEmail(adminUser.Name, theirs, orgName)))
                adminEmails++;
        }

        return (due.Count, adminEmails, staffEmails);
    }

    // ---- Wording ----

    private static string Label(ExpiringDocument d) => ExpiringDocuments.Label(d.Document);

    private static string Capital(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    private static string Date(DateTime d) => d.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    private static string When(Due d) => d.Stage switch
    {
        DocumentReminderStage.ON_THE_DAY => "expires today",
        DocumentReminderStage.EXPIRED => "has expired",
        _ => $"expires in {d.Doc.DaysLeft} day{(d.Doc.DaysLeft == 1 ? "" : "s")}",
    };

    private static string Numbered(ExpiringDocument d) =>
        d.Number is null ? Capital(Label(d)) : $"{Capital(Label(d))} {d.Number}";

    private static string Dated(ExpiringDocument d) =>
        d.DaysLeft < 0 ? $"expired on {Date(d.Expiry)}" : $"expires on {Date(d.Expiry)}";

    private static string StaffTitle(Due d) => $"Your {Label(d.Doc)} {When(d)}";

    private static string StaffBody(Due d) =>
        $"{Numbered(d.Doc)} {Dated(d.Doc)}. Please talk to HR about renewing it.";

    private static string AdminTitle(Due d) => $"{d.PersonName}'s {Label(d.Doc)} {When(d)}";

    private static string AdminBody(Due d) =>
        $"{Numbered(d.Doc)} {Dated(d.Doc)}. Once it's renewed, update the date on their record.";

    // Opens that person's record at the date to update (see the frontend's
    // employee profile link).
    private static string AdminUrl(Due d) =>
        $"/employees?employee={Uri.EscapeDataString(d.Doc.UserId)}&section=personal&field="
        + Uri.EscapeDataString(d.Doc.Document == IdentityDocument.PASSPORT ? "Passport expiry" : "Work permit expiry");

    private static string AdminSubject(int count, string orgName) =>
        $"{count} document{(count == 1 ? "" : "s")} need{(count == 1 ? "s" : "")} renewing at {orgName}";

    private static string H(string s) => WebUtility.HtmlEncode(s);

    private string StaffEmail(Due d, string orgName) =>
        EmailTemplate.Wrap(
            StaffTitle(d),
            EmailTemplate.Paragraph($"Your {H(Label(d.Doc))} with <strong>{H(orgName)}</strong> {H(When(d))}.")
            + EmailTemplate.Paragraph($"<strong>{H(Numbered(d.Doc))}</strong> · {H(Dated(d.Doc))}")
            + EmailTemplate.Paragraph(
                "Please talk to HR about renewing it and give them anything they ask for. "
                + "Once it's renewed, HR updates the new date and these reminders stop.")
            + EmailTemplate.Button("Open AltomateHR", _portal.LoginUrl)
            + EmailTemplate.Paragraph(
                $"You get this because {H(orgName)} sends document reminders by email.", muted: true),
            preheader: $"{Numbered(d.Doc)} {Dated(d.Doc)}");

    private string AdminEmail(string? adminName, IReadOnlyList<Due> items, string orgName)
    {
        var rows = string.Join("", items.Select(d =>
            $"""
            <tr>
              <td style="padding:10px 0;border-top:1px solid #E6DEF1;font-size:14px;line-height:1.5;color:#190D26;">
                <strong>{H(d.PersonName)}</strong> · {H(d.Stage switch
                {
                    DocumentReminderStage.ON_THE_DAY => "EXPIRES TODAY",
                    DocumentReminderStage.EXPIRED => "ALREADY EXPIRED",
                    _ => $"IN {d.Doc.DaysLeft} DAY{(d.Doc.DaysLeft == 1 ? "" : "S")}",
                })}<br>
                <span style="color:#4C4258;">{H(Numbered(d.Doc))} · {H(Dated(d.Doc))}</span>
              </td>
            </tr>
            """));

        var hello = string.IsNullOrWhiteSpace(adminName) ? "Hi," : $"Hi {H(adminName.Trim())},";
        return EmailTemplate.Wrap(
            AdminSubject(items.Count, orgName),
            EmailTemplate.Paragraph(hello)
            + EmailTemplate.Paragraph(
                $"These staff at <strong>{H(orgName)}</strong> have a work permit or passport expiring. "
                + "One email a day lists every reminder due that day.")
            + $"""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 16px;">{rows}</table>"""
            + EmailTemplate.Button("Open AltomateHR", _portal.BaseUrl)
            + EmailTemplate.Paragraph(
                "Once a permit or passport is renewed, update its expiry date on the employee's record. "
                + "The reminders then start over for the new date.")
            + EmailTemplate.Paragraph(
                $"You get this because you're chosen for document expiry reminders in Settings → Notifications at {H(orgName)}. "
                + "If your access is limited, you only hear about the staff it covers.", muted: true),
            preheader: string.Join(", ", items.Take(3).Select(d => $"{d.PersonName}: {Label(d.Doc)}")));
    }
}
