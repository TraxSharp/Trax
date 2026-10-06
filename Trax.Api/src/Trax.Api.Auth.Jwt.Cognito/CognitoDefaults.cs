namespace Trax.Api.Auth.Jwt.Cognito;

/// <summary>
/// Constants and well-known claim names emitted by Amazon Cognito user pool
/// tokens.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public static class CognitoDefaults
{
    /// <summary>
    /// Discriminator written to <see cref="TraxAuthClaimTypes.PrincipalType"/>
    /// by <see cref="CognitoJwtPrincipalResolver"/>.
    /// </summary>
    public const string PrincipalType = "cognito";

    /// <summary>Cognito ID-token <c>token_use</c> claim value.</summary>
    public const string TokenUseId = "id";

    /// <summary>Cognito access-token <c>token_use</c> claim value.</summary>
    public const string TokenUseAccess = "access";

    /// <summary>Claim name carrying the Cognito-internal username.</summary>
    public const string CognitoUsername = "cognito:username";

    /// <summary>Claim name carrying Cognito group memberships (repeats per group).</summary>
    public const string CognitoGroups = "cognito:groups";

    /// <summary>Claim name carrying the JSON-encoded federated identities array.</summary>
    public const string Identities = "identities";

    /// <summary>
    /// Synthetic claim emitted by <see cref="CognitoJwtPrincipalResolver"/>
    /// that names the primary federated provider (<c>Google</c>,
    /// <c>SignInWithApple</c>, etc.) or <c>cognito</c> for native users.
    /// </summary>
    public const string IdentityProvider = "identity_provider";

    /// <summary>Claim name carrying <c>access</c> or <c>id</c>.</summary>
    public const string TokenUse = "token_use";

    /// <summary>Claim name on Cognito access tokens carrying the app client id.</summary>
    public const string ClientId = "client_id";

    /// <summary>Claim name carrying email-verified state.</summary>
    public const string EmailVerified = "email_verified";

    /// <summary>Claim name carrying the user's email.</summary>
    public const string Email = "email";
}
