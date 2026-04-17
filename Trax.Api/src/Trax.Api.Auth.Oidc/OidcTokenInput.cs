using System.Security.Claims;

namespace Trax.Api.Auth.Oidc;

/// <summary>
/// Input handed to an <see cref="ITraxPrincipalResolver{OidcTokenInput}"/>
/// after the OIDC handler has validated the id-token (signature, issuer,
/// audience, nonce, lifetime). The resolver never sees an unvalidated token.
/// </summary>
/// <param name="Principal">
/// Claims principal built from the validated id-token. Subject, name, and
/// any custom claims the IdP includes are already populated.
/// </param>
/// <param name="IdToken">Raw id-token string, if the handler retained it.</param>
/// <param name="AccessToken">Access token, if one was issued. May be <c>null</c>.</param>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed record OidcTokenInput(
    ClaimsPrincipal Principal,
    string? IdToken,
    string? AccessToken
);
