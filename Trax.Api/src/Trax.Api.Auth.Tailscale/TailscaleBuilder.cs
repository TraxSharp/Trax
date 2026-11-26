namespace Trax.Api.Auth.Tailscale;

/// <summary>
/// Which tailnet logins the scheme admits, and the roles each gets. Logins are compared ignoring case. A login the
/// builder does not name is refused, unless <see cref="AnyLogin"/> admits every login.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// Roles come from logins only. Tailscale sends no identity for a request from a tagged device, so a tag cannot be
/// mapped to a role, and such a request is anonymous.
/// </para>
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/api-auth/add-trax-tailscale-auth">SDK reference</seealso>
public sealed class TailscaleBuilder
{
    private readonly Dictionary<string, string[]> _logins = new(StringComparer.OrdinalIgnoreCase);

    private string[]? _anyLoginRoles;

    /// <summary>Admits <paramref name="login"/> (<c>alice@example.com</c>) with <paramref name="roles"/>.</summary>
    /// <param name="login">The login name as Tailscale reports it in <c>Tailscale-User-Login</c>.</param>
    /// <param name="roles">The roles the caller gets. None is allowed.</param>
    public TailscaleBuilder Login(string login, params string[] roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        ArgumentNullException.ThrowIfNull(roles);
        if (login.Contains(',', StringComparison.Ordinal) || login.Trim().Length != login.Length)
            throw new ArgumentException(
                $"'{login}' is not a login: it has a comma or surrounding space.",
                nameof(login)
            );
        if (!_logins.TryAdd(login, (string[])roles.Clone()))
            throw new ArgumentException($"Login '{login}' is mapped twice.", nameof(login));
        return this;
    }

    /// <summary>
    /// Admits every login on the tailnet, and anyone a device was shared with, with <paramref name="roles"/>. A login
    /// named with <see cref="Login"/> gets its own roles instead.
    /// </summary>
    /// <param name="roles">The roles an unnamed login gets. None is allowed.</param>
    public TailscaleBuilder AnyLogin(params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        _anyLoginRoles = (string[])roles.Clone();
        return this;
    }

    internal TailscaleLogins Build()
    {
        if (_logins.Count == 0 && _anyLoginRoles is null)
            throw new InvalidOperationException(
                "AddTraxTailscaleAuth(tailnet => ...) admits nobody. Name each login with "
                    + "tailnet.Login(login, roles), or admit every login with tailnet.AnyLogin(roles)."
            );
        return new TailscaleLogins(
            new Dictionary<string, string[]>(_logins, StringComparer.OrdinalIgnoreCase),
            _anyLoginRoles
        );
    }
}
