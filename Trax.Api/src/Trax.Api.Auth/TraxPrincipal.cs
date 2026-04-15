namespace Trax.Api.Auth;

/// <summary>
/// Framework-agnostic identity record produced by an <see cref="ITraxPrincipalResolver{TInput}"/>.
/// Converts to an ASP.NET Core <see cref="System.Security.Claims.ClaimsPrincipal"/> via
/// <see cref="TraxPrincipalExtensions.ToClaimsPrincipal(TraxPrincipal, string)"/>.
/// </summary>
/// <param name="Id">
/// Stable principal identifier. Lands in the <see cref="TraxAuthClaimTypes.PrincipalId"/> claim.
/// Must be a non-empty, non-whitespace string; empty identifiers would surface as real claims to
/// audit sinks and downstream authorization handlers.
/// </param>
/// <param name="DisplayName">
/// Human-readable name. Lands in <see cref="System.Security.Claims.ClaimTypes.Name"/>.
/// Must be a non-empty, non-whitespace string.
/// </param>
/// <param name="Roles">Role claims emitted as <see cref="System.Security.Claims.ClaimTypes.Role"/> entries, one per role.</param>
/// <param name="Claims">
/// Optional bag of additional claims. Entries whose key is a Trax-reserved claim type
/// (see <see cref="TraxPrincipalExtensions"/>) are dropped during projection; use
/// <paramref name="Id"/>, <paramref name="DisplayName"/>, <paramref name="Roles"/>, and
/// <paramref name="PrincipalType"/> for those.
/// </param>
/// <param name="PrincipalType">
/// Optional scheme discriminator (e.g. <c>apikey</c>, <c>jwt</c>). When present, emitted as the
/// <see cref="TraxAuthClaimTypes.PrincipalType"/> claim so audit sinks can attribute the principal source.
/// </param>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public record TraxPrincipal(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyDictionary<string, string>? Claims = null,
    string? PrincipalType = null
)
{
    private readonly string _id = RequireNonEmpty(Id, nameof(Id));

    /// <inheritdoc cref="TraxPrincipal" path="/param[@name='Id']" />
    public string Id
    {
        get => _id;
        init => _id = RequireNonEmpty(value, nameof(Id));
    }

    private readonly string _displayName = RequireNonEmpty(DisplayName, nameof(DisplayName));

    /// <inheritdoc cref="TraxPrincipal" path="/param[@name='DisplayName']" />
    public string DisplayName
    {
        get => _displayName;
        init => _displayName = RequireNonEmpty(value, nameof(DisplayName));
    }

    private static string RequireNonEmpty(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value;
    }
}
