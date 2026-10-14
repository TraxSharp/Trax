using Microsoft.AspNetCore.Authentication;

namespace Trax.Api.Auth.ApiKey;

/// <summary>
/// Configurable options for the Trax API-key authentication scheme.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/api-auth/api-key-authentication-options">SDK reference</seealso>
public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// HTTP request header that carries the API key. Defaults to
    /// <see cref="ApiKeyDefaults.HeaderName"/> (<c>X-Api-Key</c>).
    /// </summary>
    public string HeaderName { get; set; } = ApiKeyDefaults.HeaderName;
}
