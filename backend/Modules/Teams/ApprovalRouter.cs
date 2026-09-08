namespace AltomateHR.Api.Modules.Teams;

// Resolves WHO must approve a request right now, and how many steps there
// are — purely the employee's team chain for the module. `projectId`
// disambiguates which team when the employee is on more than one; see
// ApprovalChainService.GetChainAsync.
//
// Approval is purely by hierarchy seat, and administrative roles are not in the
// hierarchy: an Admin/Owner is never returned as an approver. So "no one above
// me" is a real, reachable state — StepCountAsync returns 0 for the person at
// the top (or anyone on no team at all), and callers must treat that as
// "nobody to ask" rather than creating a request no one can ever decide.
// See OrgRoles.
public interface IApprovalRouter
{
    // Approver ids for the request at `currentStep`. Empty when there's no one.
    Task<IReadOnlyList<string>> CurrentApproversAsync(
        ApprovalModule module, string applicantId, int currentStep, string? projectId = null);

    // Number of approval steps in the applicant's chain — 0 when they're on no
    // team, or sit at/above the top approving layer.
    Task<int> StepCountAsync(ApprovalModule module, string applicantId, string? projectId = null);
}

public class ApprovalRouter : IApprovalRouter
{
    private readonly IApprovalChainService _chain;

    public ApprovalRouter(IApprovalChainService chain)
    {
        _chain = chain;
    }

    public async Task<IReadOnlyList<string>> CurrentApproversAsync(
        ApprovalModule module, string applicantId, int currentStep, string? projectId = null)
    {
        var chain = await _chain.GetChainAsync(applicantId, module, projectId);
        return currentStep >= 0 && currentStep < chain.Count ? chain[currentStep].ApproverIds : [];
    }

    public async Task<int> StepCountAsync(ApprovalModule module, string applicantId, string? projectId = null)
    {
        var chain = await _chain.GetChainAsync(applicantId, module, projectId);
        return chain.Count;
    }
}
