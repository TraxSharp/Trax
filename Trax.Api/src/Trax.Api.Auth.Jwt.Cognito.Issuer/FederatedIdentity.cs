namespace Trax.Api.Auth.Jwt.Cognito.Issuer;

/// <summary>
/// A single entry in the Cognito <c>identities</c> claim. Cognito emits one
/// per linked external provider (Google, Apple, Facebook, SAML/OIDC). Native
/// users (signed up directly into the user pool) have no <c>identities</c>
/// claim at all.
/// </summary>
/// <param name="UserId">
/// Provider-issued subject. For Google this is the numeric user id; for
/// Apple, the opaque <c>sub</c>; for SAML, the assertion's NameID.
/// </param>
/// <param name="ProviderName">
/// Cognito-side provider name, e.g. <c>Google</c>, <c>SignInWithApple</c>,
/// <c>Facebook</c>, or a custom OIDC/SAML provider name configured on the
/// user pool.
/// </param>
/// <param name="ProviderType">
/// Provider type discriminator. For OIDC federations this matches
/// <paramref name="ProviderName"/>; for SAML it is <c>SAML</c>.
/// </param>
/// <param name="Primary">
/// Whether this identity is flagged primary. Cognito's
/// <see cref="CognitoJwtPrincipalResolver"/> prefers the primary entry when
/// resolving <c>identity_provider</c>.
/// </param>
/// <param name="DateCreated">
/// When the identity was linked. Cognito serializes this as a string of
/// epoch-milliseconds; the issuer handles the conversion.
/// </param>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed record FederatedIdentity(
    string UserId,
    string ProviderName,
    string ProviderType,
    bool Primary,
    DateTimeOffset DateCreated
);
