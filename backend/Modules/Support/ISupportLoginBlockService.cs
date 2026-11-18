using AltomateHR.Api.Modules.Support.Dtos;

namespace AltomateHR.Api.Modules.Support;

// Sign-in locks and blocks (LoginGuard) for Fusioneta support: who can't sign
// in right now, and lifting one early for someone who really did forget
// their password. Superadmin-only — enforced at the controller.
public interface ISupportLoginBlockService
{
    Task<IReadOnlyList<LoginBlockDto>> ListAsync();

    // False when nothing was in force for that key — it ran out, or someone
    // else lifted it first. Recorded in the activity log and the alert chat.
    Task<bool> UnblockAsync(LoginBlockKind kind, string key);
}
