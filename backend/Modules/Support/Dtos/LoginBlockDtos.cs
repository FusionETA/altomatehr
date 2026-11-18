using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Support.Dtos;

public enum LoginBlockKind
{
    // One account, after 5 wrong passwords for it.
    ACCOUNT,
    // One network address, after 10 wrong passwords from it for anyone.
    IP,
}

// A sign-in lock or block in force right now, on the support page.
public class LoginBlockDto
{
    public LoginBlockKind Kind { get; set; }

    // What /support/login-blocks/unblock takes back. Opaque to the client.
    public string Key { get; set; } = string.Empty;

    // ACCOUNT: the address typed, and the person when it's a real account
    // (null name: no AltomateHR account has that email). IP: the address.
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Company { get; set; }
    public string? Ip { get; set; }

    public DateTime UntilUtc { get; set; }

    // IP: the addresses its wrong passwords were for.
    public IReadOnlyList<string> EmailsTried { get; set; } = [];
}

public class UnblockLoginDto
{
    [Required]
    public LoginBlockKind? Kind { get; set; }

    [Required, MaxLength(400)]
    public string Key { get; set; } = string.Empty;
}
