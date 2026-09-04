namespace AltomateHR.Api.Modules.Claims.Entities;

// C# enums — mirror the Prisma enums in the real AltomateHR schema.
// Stored as strings in the DB (configured in AppDbContext) and shown as
// strings in JSON (configured in Program.cs).

public enum ClaimStatus { SUBMITTED, PENDING, APPROVED, REVIEWED, REJECTED }

public enum ClaimType { EXPENSE, MILEAGE }

public enum PaymentType { PERSONAL, COMPANY }

public enum ClaimCategory { TRAVEL, TRANSPORT, MEAL, MEDICAL, WELLNESS, HARDWARE, OFFICE, OTHER }

// Whether an approved claim has been pushed to Xero as a bill. ERROR is kept
// distinct from NOT_SYNCED on purpose: "never tried" and "tried and failed"
// need different actions from an admin, and collapsing them hides the failure.
public enum XeroSyncStatus { NOT_SYNCED, SYNCED, ERROR }
