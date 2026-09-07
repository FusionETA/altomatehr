using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Auth;

namespace AltomateHR.Api.Modules.Teams;

// Resolves WHO must approve a request right now, and how many steps there are:
// the team chain for the module, or — when the employee has no chain — a single
// supervisor step (the flat fallback), so nothing breaks mid-migration.
//
// Approval is purely by hierarchy seat, and administrative roles are not in the
// hierarchy: an Admin/Owner is never returned as an approver, from either
// source. So "no one above me" is a real, reachable state — StepCountAsync
// returns 0 for the person at the top, and callers must treat that as "nobody
// to ask" rather than creating a request no one can ever decide. See OrgRoles.
public interface IApprovalRouter
{
    // Approver ids for the request at `currentStep`. Empty when there's no one.
    Task<IReadOnlyList<string>> CurrentApproversAsync(ApprovalModule module, string applicantId, int currentStep);

    // Number of approval steps: the chain length, or 1 for the supervisor
    // fallback, or 0 when the applicant has neither a chain nor a supervisor.
    Task<int> StepCountAsync(ApprovalModule module, string applicantId);
}

public class ApprovalRouter : IApprovalRouter
{
    private readonly IApprovalChainService _chain;
    private readonly ISupervisionService _supervision;

    public ApprovalRouter(IApprovalChainService chain, ISupervisionService supervision)
    {
        _chain = chain;
        _supervision = supervision;
    }

    public async Task<IReadOnlyList<string>> CurrentApproversAsync(
        ApprovalModule module, string applicantId, int currentStep)
    {
        var chain = await _chain.GetChainAsync(applicantId, module);
        if (chain.Count > 0)
            return currentStep >= 0 && currentStep < chain.Count ? chain[currentStep].ApproverIds : [];

        // No team chain → a single supervisor step (the flat fallback).
        var supervisor = await ApprovingSupervisorAsync(applicantId);
        return currentStep == 0 && supervisor is not null ? [supervisor] : [];
    }

    public async Task<int> StepCountAsync(ApprovalModule module, string applicantId)
    {
        var chain = await _chain.GetChainAsync(applicantId, module);
        if (chain.Count > 0) return chain.Count;

        var supervisor = await ApprovingSupervisorAsync(applicantId);
        return supervisor is not null ? 1 : 0;
    }

    // The assigned supervisor, unless they hold an administrative seat — an
    // admin listed as someone's supervisor is there for oversight and doesn't
    // become their approver. Null then means genuinely nobody above.
    private async Task<string?> ApprovingSupervisorAsync(string applicantId)
    {
        var supervisorId = await _supervision.GetSupervisorIdAsync(applicantId);
        if (supervisorId is null) return null;

        var administrative = await _supervision.GetAdministrativeUserIdsAsync();
        return administrative.Contains(supervisorId) ? null : supervisorId;
    }
}
