using AltomateHR.Api.Modules.Auth;

namespace AltomateHR.Api.Modules.Teams;

// Resolves WHO must approve a request right now, and how many steps there are:
// the team chain for the module, or — when the employee has no chain — a single
// supervisor step (the flat fallback), so nothing breaks mid-migration.
public interface IApprovalRouter
{
    bool IsOrgApprover(string? role);

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

    public bool IsOrgApprover(string? role) => _supervision.IsOrgApprover(role);

    public async Task<IReadOnlyList<string>> CurrentApproversAsync(
        ApprovalModule module, string applicantId, int currentStep)
    {
        var chain = await _chain.GetChainAsync(applicantId, module);
        if (chain.Count > 0)
            return currentStep >= 0 && currentStep < chain.Count ? chain[currentStep].ApproverIds : [];

        // No team chain → a single supervisor step (the flat fallback).
        var supervisor = await _supervision.GetSupervisorIdAsync(applicantId);
        return currentStep == 0 && supervisor is not null ? [supervisor] : [];
    }

    public async Task<int> StepCountAsync(ApprovalModule module, string applicantId)
    {
        var chain = await _chain.GetChainAsync(applicantId, module);
        if (chain.Count > 0) return chain.Count;

        var supervisor = await _supervision.GetSupervisorIdAsync(applicantId);
        return supervisor is not null ? 1 : 0;
    }
}
