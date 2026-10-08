using AltomateHR.Api.Modules.Documents.Entities;

namespace AltomateHR.Api.Modules.Documents;

// Starter letters for "Add sample templates". Not seeded per company in a
// migration: an admin adds them when they want them, and edits from there.
//
// The wording is a PLACEHOLDER, not legal advice — every body opens with a
// bold line saying so, which prints on the letter until the admin deletes it,
// so a sample can't be sent as-is by accident.
public static class SampleTemplates
{
    public sealed record Sample(string Key, string Name, DocumentCategory Category, string Body);

    private const string Notice =
        "**SAMPLE WORDING — replace with your company's own text and have it reviewed before use. Delete this line.**";

    private static string WithNotice(string body) => Notice + "\n\n" + body;

    public static readonly IReadOnlyList<Sample> All =
    [
        new("offer-letter", "Offer Letter", DocumentCategory.OFFER, WithNotice("""
            PRIVATE & CONFIDENTIAL

            {{employee.name}}
            {{employee.address}}

            Dear {{employee.name}},

            # Letter of Offer of Employment

            We are pleased to offer you the position of **{{employee.jobTitle}}** in the {{employee.department}} department of {{company.name}}, on the terms below.

            ## Terms of employment

            - Commencement date: {{employee.joinDate}}
            - Place of work: {{employee.location}}
            - Basic salary: {{employee.monthlySalary}} per month
            - Probation period: {{employee.probationMonths}} months from your commencement date

            During probation either party may end the employment by giving notice as set out in the company's employee handbook. On successful completion of probation you will be confirmed in writing.

            Your other benefits, working hours and leave entitlement are set out in the employee handbook, which forms part of this offer.

            Please sign and return a copy of this letter by **{{input.offerExpiryDate}}** to accept this offer. If we do not hear from you by then, this offer will lapse.

            We look forward to welcoming you to the team.
            """)),

        new("confirmation-letter", "Confirmation of Employment", DocumentCategory.CONFIRMATION, WithNotice("""
            PRIVATE & CONFIDENTIAL

            {{employee.name}}
            Employee ID: {{employee.employeeNumber}}

            Dear {{employee.name}},

            # Confirmation of Employment

            We are pleased to inform you that you have successfully completed your probation period of {{employee.probationMonths}} months as **{{employee.jobTitle}}**.

            Your employment is confirmed with effect from **{{employee.confirmationDate}}**. All other terms and conditions of your employment remain unchanged.

            Thank you for your contribution so far. We look forward to your continued commitment to {{company.name}}.
            """)),

        new("resignation-acceptance", "Resignation Acceptance", DocumentCategory.RESIGNATION, WithNotice("""
            PRIVATE & CONFIDENTIAL

            {{employee.name}}
            IC / Passport No.: {{employee.idNumber}}

            Dear {{employee.name}},

            # Acceptance of Resignation

            We acknowledge receipt of your letter of resignation dated {{input.resignationLetterDate}} and accept your resignation from the position of {{employee.jobTitle}}.

            Your last working day with {{company.name}} will be **{{employee.leaveDate}}**.

            Before your last day, please:

            - return all company property, including access cards, equipment and documents;
            - hand over your work and any outstanding matters to your supervisor;
            - settle any outstanding advances or claims with the HR department.

            Your final salary and any balance due to you will be paid in the usual payroll cycle after your last working day.

            We thank you for your service and wish you every success in the future.
            """)),

        new("warning-letter", "First Written Warning", DocumentCategory.WARNING, WithNotice("""
            PRIVATE & CONFIDENTIAL

            {{employee.name}}
            Employee ID: {{employee.employeeNumber}}
            {{employee.jobTitle}}, {{employee.department}}

            Dear {{employee.name}},

            # First Written Warning

            This letter is a formal written warning about {{input.misconductSummary}}.

            On {{input.incidentDate}}, {{input.incidentDetails}}

            This conduct is contrary to the company's rules and the standard expected of every employee. You were given the opportunity to explain at a meeting on {{input.meetingDate}}, and your explanation was not accepted.

            You are required to improve immediately. Any repetition of this or similar misconduct may lead to further disciplinary action, up to and including dismissal.

            A copy of this letter will be kept on your personnel file. Please sign below to acknowledge receipt.

            Acknowledged by: ______________________        Date: ______________
            """)),
    ];
}
