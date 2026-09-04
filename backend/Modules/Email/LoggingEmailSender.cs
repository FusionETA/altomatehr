namespace AltomateHR.Api.Modules.Email;

// Stand-in used when EngineMailer isn't configured, so dev works with no
// credentials. Mirrors the Redis → in-memory cache fallback in Program.cs.
//
// It logs the body, which for the OTP flow means the code is readable from the
// server log — fine (and useful) in development, but this must never be the
// registered sender in production. Program.cs picks it only when the API key is
// blank, and logs a warning at startup when it does.
public class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger) => _logger = logger;

    public Task<bool> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "[email not sent — EngineMailer unconfigured] To: {To} | Subject: {Subject}\n{Body}",
            toEmail, subject, htmlBody);

        return Task.FromResult(true);
    }
}
