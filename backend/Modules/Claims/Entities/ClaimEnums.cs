namespace AltomateHR.Api.Modules.Claims.Entities;

// C# enums — mirror the Prisma enums in the real AltomateHR schema.
// Stored as strings in the DB (configured in AppDbContext) and shown as
// strings in JSON (configured in Program.cs).

public enum ClaimStatus { SUBMITTED, PENDING, APPROVED, REVIEWED, REJECTED }

public enum ClaimType { EXPENSE, MILEAGE }

public enum PaymentType { PERSONAL, COMPANY }

public enum ClaimCategory { TRAVEL, TRANSPORT, MEAL, MEDICAL, WELLNESS, HARDWARE, OFFICE, OTHER }
