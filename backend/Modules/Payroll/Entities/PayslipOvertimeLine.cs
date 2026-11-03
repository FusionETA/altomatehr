namespace AltomateHR.Api.Modules.Payroll.Entities;

// One of the company's own overtime types as a payslip paid it — name and
// multiplier snapshotted at generation (Payslip.OtCustomJson). Amount is
// hours × the hourly rate × multiplier, rounded for display; the payslip's
// OtPay rounds the month's overtime once, as a whole.
public sealed record PayslipOvertimeLine(string TypeId, string Name, decimal Hours, decimal Multiplier, decimal Amount);
