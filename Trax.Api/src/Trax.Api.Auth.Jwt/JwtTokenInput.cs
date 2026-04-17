using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Trax.Api.Auth.Jwt;

/// <summary>
/// Input handed to an <see cref="ITraxPrincipalResolver{JwtTokenInput}"/> after
/// <c>Microsoft.AspNetCore.Authentication.JwtBearer</c> has validated signature,
/// issuer, audience, and lifetime. The resolver never sees an unvalidated token.
/// </summary>
/// <param name="Principal">
/// Claims principal built by JwtBearer from the validated token. Subject, roles,
/// audiences, and any custom claims are already populated.
/// </param>
/// <param name="SecurityToken">
/// The validated <see cref="Microsoft.IdentityModel.Tokens.SecurityToken"/>.
/// Concrete type is typically <c>JsonWebToken</c> or <c>JwtSecurityToken</c>
/// depending on which token handler the options select.
/// </param>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed record JwtTokenInput(ClaimsPrincipal Principal, SecurityToken SecurityToken);
