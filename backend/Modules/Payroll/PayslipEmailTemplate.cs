using System.Net;
using AltomateHR.Api.Modules.Email;

namespace AltomateHR.Api.Modules.Payroll;

// The email an employee gets when an admin sends their payslip. Built on
// EmailTemplate like every other message the app sends, matching
// Modules/Employees/WelcomeEmail.cs's house style.
public static class PayslipEmailTemplate
{
    public static string Subject(string periodLabel) => $"Your payslip — {periodLabel}";

    public static string BuildHtml(string name, string organizationName, string periodLabel)
    {
        var safeName = WebUtility.HtmlEncode(name);
        var safeOrg = WebUtility.HtmlEncode(organizationName);
        var safePeriod = WebUtility.HtmlEncode(periodLabel);

        var body =
            EmailTemplate.Paragraph(
                $"Hi {safeName}, your payslip from {safeOrg} for {safePeriod} is attached as a PDF.")
            + EmailTemplate.Paragraph(
                "If you weren't expecting this email, please contact your administrator.",
                muted: true);

        return EmailTemplate.Wrap(
            $"Payslip — {safePeriod}",
            body,
            preheader: $"Your payslip for {safePeriod} is attached.");
    }
}
