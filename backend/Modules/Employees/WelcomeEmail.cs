using System.Net;
using AltomateHR.Api.Modules.Email;

namespace AltomateHR.Api.Modules.Employees;

// The email a newly-added employee gets: where to sign in, how their password
// is formed, and how to keep the portal on their phone.
//
// This is what makes the email+MMDD convention worth its cost. The password is
// guessable by anyone who knows someone's birthday, which is only a fair trade
// if it buys something — and what it buys is this: the rule can be explained
// without ever printing the credential, so the email is not itself a working
// login for whoever reads it over their shoulder. A random password would have
// to be in the message, or relayed by hand for every hire.
//
// Built on EmailTemplate like every other message the app sends, rather than
// carrying the legacy version's own HTML, so all of them stay one house style.
public static class WelcomeEmail
{
    // Which password sentence the email carries — the three real outcomes of
    // adding someone.
    public enum PasswordMode
    {
        // The house convention: the email can describe the format instead of
        // printing the credential.
        Default,

        // The address already had an account at another company and was LINKED,
        // not created. They keep the password they already use, and any
        // password typed by the admin was ignored.
        Existing,

        // An admin typed one. We don't know it and wouldn't email it, so we say
        // the admin will pass it on.
        Manual,
    }

    public static string BuildHtml(
        string name, string organizationName, string email, string loginUrl, PasswordMode mode)
    {
        var safeEmail = WebUtility.HtmlEncode(email);
        var safeName = WebUtility.HtmlEncode(name);
        var safeOrg = WebUtility.HtmlEncode(organizationName);

        var body =
            EmailTemplate.Paragraph(
                $"Hi {safeName}, your AltomateHR account at {safeOrg} is ready. Sign in to submit "
                + "claims, apply for leave, clock in, and read your payslips.")
            + EmailTemplate.Button("Sign in to AltomateHR", loginUrl)
            + PasswordBlock(mode, safeEmail)
            + EmailTemplate.Paragraph("<strong>Keep it on your phone</strong>")
            + EmailTemplate.Paragraph(
                "There's no app to download — open the link above in your phone's browser, then "
                + "add it to your home screen. On iPhone: <strong>Share → Add to Home Screen</strong>. "
                + "On Android: <strong>menu → Install app</strong>. It then opens like a normal app "
                + "and can send you notifications.")
            + EmailTemplate.Paragraph(
                "Please change your password after your first sign-in. If you didn't expect this "
                + "email, you can ignore it.",
                muted: true);

        return EmailTemplate.Wrap(
            $"Welcome to {safeOrg}",
            body,
            preheader: "Your AltomateHR account is ready — here's how to sign in.");
    }

    private static string PasswordBlock(PasswordMode mode, string safeEmail) => mode switch
    {
        PasswordMode.Existing => EmailTemplate.Paragraph(
            $"You already have an AltomateHR account, so sign in with <strong>{safeEmail}</strong> "
            + "and the password you use today — it hasn't changed."),

        PasswordMode.Manual => EmailTemplate.Paragraph(
            $"Your username is <strong>{safeEmail}</strong>. Your administrator will pass you the "
            + "temporary password separately."),

        // Describe the format, never the credential: this email on its own must
        // not be a working login for anyone who reads it.
        _ => EmailTemplate.Paragraph($"Your username is <strong>{safeEmail}</strong>.")
             + EmailTemplate.Paragraph(
                 "Your temporary password is <strong>your email address followed by your birthday "
                 + "as MMDD</strong> — two digits for the month, then two for the day. If you were "
                 + $"born on 23 November, that's <strong>{safeEmail}1123</strong>."),
    };
}
