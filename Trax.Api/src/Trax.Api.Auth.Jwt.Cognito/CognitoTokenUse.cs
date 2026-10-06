namespace Trax.Api.Auth.Jwt.Cognito;

/// <summary>
/// Selects which Cognito token shapes <see cref="CognitoJwtBuilderExtensions.UseCognito"/>
/// configures the validator to accept.
/// </summary>
/// <remarks>
/// Cognito issues both ID tokens (audience in <c>aud</c>) and access tokens
/// (audience in <c>client_id</c>). The default validator wires acceptance
/// of either, since most apps mix both depending on the call site.
/// </remarks>
public enum CognitoTokenUse
{
    /// <summary>Accept Cognito ID tokens (audience claim: <c>aud</c>).</summary>
    Id,

    /// <summary>Accept Cognito access tokens (audience claim: <c>client_id</c>).</summary>
    Access,

    /// <summary>Accept both shapes. The default.</summary>
    IdAndAccess,
}
