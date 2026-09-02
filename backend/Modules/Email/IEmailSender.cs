namespace AltomateHR.Api.Modules.Email;

public interface IEmailSender
{
    // Returns whether the provider actually ACCEPTED the message — not merely
    // whether the HTTP call returned 200. Callers should not treat a false as
    // fatal for user-facing flows (see AuthService.ForgotPasswordAsync: a failed
    // send must not reveal whether the account exists).
    Task<bool> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default);
}
