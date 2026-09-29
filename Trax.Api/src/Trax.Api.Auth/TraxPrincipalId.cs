namespace Trax.Api.Auth;

/// <summary>
/// The shape of the <see cref="TraxAuthClaimTypes.PrincipalId"/> claim: the id a resolver
/// returned, qualified by the authentication scheme that authenticated it,
/// <c>{scheme}:{id}</c>.
/// </summary>
/// <remarks>
/// A resolver's <see cref="TraxPrincipal.Id"/> is only unique within its scheme: two issuers can
/// both mint <c>sub = "abc"</c>, and an issuer that lets its subjects be chosen can mint any
/// <c>sub</c> at all. Qualifying the id by the scheme keeps the two apart wherever the id is used
/// as a key: owner-scope filters, audit records, per-principal limits.
/// <para>
/// Use <see cref="Qualify"/> to compute the id a principal will carry, for example to seed or
/// migrate rows keyed on it. See
/// <c>docs/adr/0023-a-principal-id-is-qualified-by-its-scheme.md</c>.
/// </para>
/// <para>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for
/// securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </para>
/// </remarks>
public static class TraxPrincipalId
{
    /// <summary>Separates the scheme from the resolver's id.</summary>
    public const char Separator = ':';

    /// <summary>
    /// The qualified id <paramref name="id"/> carries when <paramref name="scheme"/>
    /// authenticated it: <c>{scheme}:{id}</c>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="scheme"/> is empty or contains <see cref="Separator"/>, which would let two
    /// different scheme and id pairs read as one id; or <paramref name="id"/> is empty or
    /// whitespace.
    /// </exception>
    public static string Qualify(string scheme, string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (scheme.Contains(Separator))
            throw new ArgumentException(
                $"An authentication scheme name used for a Trax principal cannot contain '{Separator}': "
                    + $"'{scheme}' would make qualified principal ids ambiguous.",
                nameof(scheme)
            );

        return scheme + Separator + id;
    }
}
