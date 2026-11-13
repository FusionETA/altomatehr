namespace AltomateHR.Api.Modules.DocumentExpiry;

public interface IDocumentExpiryReminderService
{
    // Sends every work permit / passport reminder due on `today` (a Malaysian
    // calendar date) in every company, each one only once.
    Task<DocumentExpiryRunResult> RunAsync(DateTime today);
}

public sealed record DocumentExpiryRunResult(int Companies, int Reminders, int AdminEmails, int StaffEmails);
