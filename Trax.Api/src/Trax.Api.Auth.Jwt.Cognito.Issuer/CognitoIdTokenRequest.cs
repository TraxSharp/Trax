namespace Trax.Api.Auth.Jwt.Cognito.Issuer;

/// <summary>
/// Inputs for <see cref="CognitoTokenIssuer.MintIdToken"/>. Models a Cognito
/// user-pool ID token. The audience is carried in the standard <c>aud</c>
/// claim (= <see cref="ClientId"/>); see <see cref="CognitoTokenIssuer"/>
/// for the full claim layout.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed record CognitoIdTokenRequest
{
    /// <summary>The <c>sub</c> claim. Cognito uses a GUID per user.</summary>
    public required Guid Sub { get; init; }

    /// <summary>
    /// App client id. Written as the <c>aud</c> claim. Cognito ID tokens
    /// (unlike access tokens) follow the OIDC convention here.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>Token lifetime. Typically one hour to match Cognito's default.</summary>
    public required TimeSpan Lifetime { get; init; }

    /// <summary>The <c>email</c> claim. Required by OIDC for ID tokens that carry profile data.</summary>
    public required string Email { get; init; }

    /// <summary>The <c>email_verified</c> claim.</summary>
    public required bool EmailVerified { get; init; }

    /// <summary>Optional OIDC profile claim.</summary>
    public string? GivenName { get; init; }

    /// <summary>Optional OIDC profile claim.</summary>
    public string? FamilyName { get; init; }

    /// <summary>
    /// Cognito-internal username. Written as <c>cognito:username</c>.
    /// Defaults to the string form of <see cref="Sub"/> when not supplied.
    /// </summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Cognito group memberships. Written as repeated <c>cognito:groups</c> claims.</summary>
    public IReadOnlyList<string> Groups { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Linked federated identities. Serialized as the Cognito-shaped
    /// <c>identities</c> claim: a string containing a JSON array of objects
    /// with <c>userId</c>, <c>providerName</c>, <c>providerType</c>,
    /// <c>primary</c>, and <c>dateCreated</c> (epoch-milliseconds string).
    /// </summary>
    public IReadOnlyList<FederatedIdentity> Identities { get; init; } =
        Array.Empty<FederatedIdentity>();

    /// <summary>
    /// When the user authenticated. Cognito refresh-token grants reuse the
    /// original authentication's <c>auth_time</c>. Defaults to the issuer's
    /// clock when null.
    /// </summary>
    public DateTimeOffset? AuthTime { get; init; }

    /// <summary>
    /// Additional string-valued claims. Use for custom attributes
    /// (<c>custom:*</c>) or claims not covered above.
    /// </summary>
    public IReadOnlyDictionary<string, string>? AdditionalClaims { get; init; }
}
