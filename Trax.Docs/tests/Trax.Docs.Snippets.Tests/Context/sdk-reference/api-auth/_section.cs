// What the examples under sdk-reference/api-auth take as given: the auth usings, and a user
// store a resolver looks callers up in. The host comes from _shared.cs.
global using System.Security.Claims;
global using System.Security.Cryptography;
global using System.Text;
global using Microsoft.AspNetCore.Authentication;
global using Microsoft.IdentityModel.Tokens;
global using Trax.Api.Auth;
global using Trax.Api.Auth.ApiKey;
global using Trax.Api.Auth.Jwt;
global using Trax.Api.Auth.Jwt.Cognito;
global using Trax.Api.Auth.Jwt.Cognito.Issuer;
global using Trax.Api.Auth.Jwt.Testing;
global using Trax.Api.Auth.Oidc;
global using Trax.Api.GraphQL.Extensions;

public sealed record UserRecord(
    string Id,
    string DisplayName,
    string[] Roles,
    string TenantId,
    bool IsRevoked
);

public interface IUserDirectory
{
    Task<UserRecord?> FindByIdAsync(string id, CancellationToken ct);

    Task<UserRecord?> FindByApiKeyHashAsync(byte[] sha256, CancellationToken ct);
}
