namespace AltomateHR.Api.Modules.Organizations;

// A tenant's subscription. Mirrors the monolith's OrgPlan / OrgPlanTier.
// DIY = self-service customer (manages their own account).
// EXPERT = managed customer (we run their account); same module surface as DIY Paid.
public enum OrgPlan
{
    DIY,
    EXPERT,
}

// Only meaningful when Plan = DIY. FREE = base HR only (no Claims / Attendance);
// PAID = Claims / Attendance unlocked via addons. Null for EXPERT.
public enum OrgPlanTier
{
    FREE,
    PAID,
}
