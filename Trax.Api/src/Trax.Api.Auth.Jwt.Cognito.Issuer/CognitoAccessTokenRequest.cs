namespace Trax.Api.Auth.Jwt.Cognito.Issuer;

/// <summary>
/// Inputs for <see cref="CognitoTokenIssuer.MintAccessToken"/>. Models a
/// Cognito user-pool access token. The audience is carried in
/// <see cref="ClientId"/> (the <c>client_id</c> claim) rather than <c>aud</c>;
/// see <see cref="CognitoTokenIssuer"/> for the claim layout.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed record CognitoAccessTokenRequest
{
    /// <summary>The <c>sub</c> claim. Cognito uses a GUID per user.</summary>
    public required Guid Sub { get; init; }

    /// <summary>
    /// App client id. Written as the <c>client_id</c> claim. The
    /// <c>CognitoJwtPrincipalResolver</c>'s audience validator accepts
    /// this in place of <c>aud</c> for access tokens.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// Token lifetime. Typically one hour to match Cognito's user-pool
    /// default. The issuer derives <c>exp</c> from this plus its
    /// <c>TimeProvider</c>.
    /// </summary>
    public required TimeSpan Lifetime { get; init; }

    /// <summary>
    /// Cognito-internal username. Written as <c>username</c>. Defaults to the
    /// string form of <see cref="Sub"/> when not supplied; real Cognito
    /// access tokens always carry this claim.
    /// </summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>OAuth scopes joined into the <c>scope</c> claim (space-delimited).</summary>
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>Cognito group memberships. Written as repeated <c>cognito:groups</c> claims.</summary>
    public IReadOnlyList<string> Groups { get; init; } = Array.Empty<string>();

    /// <summary>
    /// When the user authenticated. Cognito refresh-token grants reuse the
    /// original authentication's <c>auth_time</c>, which can differ from the
    /// new token's <c>iat</c>. Defaults to the issuer's clock when null.
    /// </summary>
    public DateTimeOffset? AuthTime { get; init; }

    /// <summary>
    /// Additional string-valued claims. Use for custom attributes
    /// (<c>custom:*</c>) or scope-style extras not covered above.
    /// </summary>
    public IReadOnlyDictionary<string, string>? AdditionalClaims { get; init; }
}
