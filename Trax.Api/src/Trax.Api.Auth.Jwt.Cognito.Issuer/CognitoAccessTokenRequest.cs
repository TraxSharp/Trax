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
    /// The <c>email</c> claim. Real Cognito access tokens carry no profile claims unless a
    /// PreTokenGeneration trigger adds them; set this to mint the token such a trigger produces.
    /// Omitted from the token when null; an empty or whitespace value is refused with an
    /// <see cref="ArgumentException"/>, as it is for an ID token.
    /// </summary>
    public string? Email { get; init; }

    /// <summary>The <c>email_verified</c> claim. Omitted from the token when null.</summary>
    public bool? EmailVerified { get; init; }

    /// <summary>The <c>given_name</c> claim. Omitted from the token when null or empty.</summary>
    public string? GivenName { get; init; }

    /// <summary>The <c>family_name</c> claim. Omitted from the token when null or empty.</summary>
    public string? FamilyName { get; init; }

    /// <summary>
    /// When the user authenticated. Cognito refresh-token grants reuse the
    /// original authentication's <c>auth_time</c>, which can differ from the
    /// new token's <c>iat</c>. Defaults to the issuer's clock when null.
    /// </summary>
    public DateTimeOffset? AuthTime { get; init; }

    /// <summary>
    /// Additional string-valued claims. Use for custom attributes
    /// (<c>custom:*</c>) or scope-style extras not covered above.
    /// A name the issuer sets itself (<c>sub</c>, <c>token_use</c>, <c>cognito:groups</c> and
    /// the rest), or one the principal resolver reads an identity, a name or roles from
    /// (<c>role</c>, <c>roles</c>, <c>name</c>, ...), is refused with an
    /// <see cref="ArgumentException"/>; set it through this request's own property.
    /// </summary>
    public IReadOnlyDictionary<string, string>? AdditionalClaims { get; init; }
}
