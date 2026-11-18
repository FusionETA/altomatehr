using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Support.Dtos;

namespace AltomateHR.Api.Modules.Support;

public class SupportLoginBlockService : ISupportLoginBlockService
{
    private readonly ILoginGuard _guard;
    private readonly IUserRepository _users;
    private readonly IDirectoryService _directory;
    private readonly IOrganizationRepository _organizations;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly TeamChat.ITeamChatAlerts? _teamChat;

    public SupportLoginBlockService(
        ILoginGuard guard,
        IUserRepository users,
        IDirectoryService directory,
        IOrganizationRepository organizations,
        IAuditService audit,
        ICurrentUser currentUser,
        TeamChat.ITeamChatAlerts? teamChat = null)
    {
        _guard = guard;
        _users = users;
        _directory = directory;
        _organizations = organizations;
        _audit = audit;
        _currentUser = currentUser;
        _teamChat = teamChat;
    }

    public async Task<IReadOnlyList<LoginBlockDto>> ListAsync()
    {
        var rows = new List<LoginBlockDto>();
        foreach (var block in _guard.ActiveBlocks(DateTime.UtcNow))
            rows.Add(await ToDtoAsync(block));
        return rows;
    }

    public async Task<bool> UnblockAsync(LoginBlockKind kind, string key)
    {
        var forAccount = kind == LoginBlockKind.ACCOUNT;
        var now = DateTime.UtcNow;
        // Described before it's lifted: afterwards the guard no longer has it.
        var block = _guard.ActiveBlocks(now).FirstOrDefault(b => b.ForAccount == forAccount && b.Key == key);
        if (block is null || !_guard.Unblock(forAccount, key, now)) return false;

        var row = await ToDtoAsync(block);
        var who = forAccount ? Who(row) : row.Ip ?? key;
        var organizationId = forAccount && block.UserId is not null ? await OrganizationOfAsync(block.UserId) : null;

        var entry = new AuditEvent(
            AuditActions.SupportLoginUnblock,
            forAccount ? $"Fusioneta support unlocked sign-in for {who}" : $"Fusioneta support unblocked sign-in from IP {who}",
            TargetType: forAccount ? "User" : "IpAddress",
            TargetId: forAccount ? block.UserId : who,
            Metadata: new { Kind = kind.ToString(), Until = block.UntilUtc },
            OrganizationId: organizationId,
            Support: _currentUser.UserId is { } supportUserId
                ? new SupportActor(supportUserId, _currentUser.Email ?? string.Empty)
                : null);

        // A real account's company sees it in its own log, as "System
        // (Support)", with the person recorded internally. An IP, or an email
        // with no account, belongs to no company: internal log only. Never the
        // session's company — a superadmin inside a customer (support mode or
        // a switch) would otherwise write another party's IP into its log.
        if (organizationId is not null) await _audit.WriteAsync(entry);
        else await _audit.WriteInternalAsync(entry);

        if (_teamChat is not null) await _teamChat.LoginUnblockedAsync(forAccount, who, organizationId);
        return true;
    }

    private async Task<LoginBlockDto> ToDtoAsync(ActiveLoginBlock block)
    {
        var row = new LoginBlockDto
        {
            Kind = block.ForAccount ? LoginBlockKind.ACCOUNT : LoginBlockKind.IP,
            Key = block.Key,
            UntilUtc = block.UntilUtc,
            EmailsTried = block.EmailsTried,
        };
        if (!block.ForAccount)
        {
            row.Ip = block.Key;
            return row;
        }

        row.Email = block.Email;
        if (block.UserId is null) return row;

        var user = await _users.GetByIdAsync(block.UserId);
        if (user is null) return row;
        row.Name = user.Name;
        row.Email = user.Email;
        var organizationId = await OrganizationOfAsync(user.Id);
        if (organizationId is not null) row.Company = (await _organizations.GetByIdAsync(organizationId))?.Name;
        return row;
    }

    // The company a failed sign-in is recorded against (AuthService does the same).
    private async Task<string?> OrganizationOfAsync(string userId) =>
        (await _directory.GetMembershipsByUserAsync(userId)).FirstOrDefault()?.OrganizationId;

    private static string Who(LoginBlockDto row)
    {
        var email = row.Email ?? row.Key;
        return string.IsNullOrWhiteSpace(row.Name) || string.Equals(row.Name, email, StringComparison.OrdinalIgnoreCase)
            ? email
            : $"{row.Name} ({email})";
    }
}
