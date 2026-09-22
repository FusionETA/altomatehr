namespace AltomateHR.Api.Modules.Teams;

// Where a person sits in a team relative to the people below them.
//
// A deliberately tiny read surface, separate from ITeamService, because the
// Employees module needs it and ITeamService depends on Employees through
// ISupervisionService — injecting the whole thing back would be a circular
// constructor dependency. This implementation touches the team repositories
// only, so the dependency runs one way.
public interface IApproverPositions
{
    // Teams where this person sits ABOVE the bottom layer, which is what makes
    // them an approver for everyone below in that team. Empty means they
    // approve for nobody.
    Task<IReadOnlyList<ApproverPosition>> ForEmployeeAsync(string employeeId);
}

public readonly record struct ApproverPosition(string TeamId, string TeamName, int Layer);

public class ApproverPositions : IApproverPositions
{
    private readonly ITeamMembershipRepository _memberships;
    private readonly ITeamRepository _teams;

    public ApproverPositions(ITeamMembershipRepository memberships, ITeamRepository teams)
    {
        _memberships = memberships;
        _teams = teams;
    }

    public async Task<IReadOnlyList<ApproverPosition>> ForEmployeeAsync(string employeeId)
    {
        // Layer 0 is the bottom, and ApprovalChainService routes upward from
        // membership.Layer + 1 — so any layer above 0 is an approver position
        // for the people beneath it.
        //
        // Judged by the POSITION, not by whether anyone is standing below right
        // now. A team's bottom layer fills and empties as people come and go,
        // and a rule that flickered with the roster would let an admin place
        // someone at an upper layer today and have it become invalid tomorrow,
        // with no action of theirs to blame it on.
        var above = (await _memberships.GetByEmployeeAsync(employeeId))
            .Where(m => m.Layer > 0)
            .ToList();

        if (above.Count == 0) return [];

        var names = (await _teams.GetAllAsync()).ToDictionary(t => t.Id, t => t.Name);

        return above
            .Select(m => new ApproverPosition(
                m.TeamId, names.GetValueOrDefault(m.TeamId, "a team"), m.Layer))
            .ToList();
    }
}
