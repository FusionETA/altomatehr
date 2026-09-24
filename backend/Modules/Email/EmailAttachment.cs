namespace AltomateHR.Api.Modules.Email;

// One file attached to an outbound email — e.g. a payslip PDF.
public sealed record EmailAttachment(string FileName, byte[] Content);
