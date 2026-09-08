namespace AltomateHR.Api.Modules.Claims.Entities;

// C# enums — mirror the Prisma enums in the real AltomateHR schema.
// Stored as strings in the DB (configured in AppDbContext) and shown as
// strings in JSON (configured in Program.cs).

// REVIEWED is deliberately absent. The production schema used it as a separate
// settled state after APPROVED; this app leaves APPROVED terminal, so a second
// settled state only ever meant two spellings of the same thing — and every
// read had to remember to accept both. Dropped, with the rows migrated over.
public enum ClaimStatus { SUBMITTED, PENDING, APPROVED, REJECTED }

public enum ClaimType { EXPENSE, MILEAGE }

// How an approved claim gets settled. XERO_BILL pushes it to Xero (a bill for a
// personal-paid claim, a spend-money transaction for a company-paid one);
// PAYROLL reimburses it through the employee's pay instead, so it is excluded
// from Xero and picked up by the payroll reimbursement export.
//
// PAYROLL is only meaningful for PERSONAL claims: a COMPANY-paid claim's money
// already left a company account, so there is nothing to reimburse.
public enum ClaimSettlement { XERO_BILL, PAYROLL }

public enum PaymentType { PERSONAL, COMPANY }

public enum ClaimCategory { TRAVEL, TRANSPORT, MEAL, MEDICAL, WELLNESS, HARDWARE, OFFICE, OTHER }

// Whether an approved claim has been pushed to Xero as a bill. ERROR is kept
// distinct from NOT_SYNCED on purpose: "never tried" and "tried and failed"
// need different actions from an admin, and collapsing them hides the failure.
public enum XeroSyncStatus { NOT_SYNCED, SYNCED, ERROR }
