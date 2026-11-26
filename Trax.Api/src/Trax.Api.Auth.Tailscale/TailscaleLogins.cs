namespace Trax.Api.Auth.Tailscale;

/// <summary>The logins a scheme admits and their roles, as <see cref="TailscaleBuilder"/> built them.</summary>
internal sealed class TailscaleLogins(
    IReadOnlyDictionary<string, string[]> logins,
    string[]? anyLoginRoles
)
{
    /// <summary>The roles <paramref name="login"/> gets, or null when it is not admitted.</summary>
    public IReadOnlyList<string>? RolesOf(string login) =>
        logins.TryGetValue(login, out var roles) ? roles : anyLoginRoles;
}
