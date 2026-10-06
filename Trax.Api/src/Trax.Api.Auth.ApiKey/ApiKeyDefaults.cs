namespace Trax.Api.Auth.ApiKey;

/// <summary>
/// Constants for the Trax API-key authentication scheme.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public static class ApiKeyDefaults
{
    /// <summary>
    /// Name of the authentication scheme registered by <c>AddTraxApiKeyAuth</c>.
    /// </summary>
    public const string SchemeName = "TraxApiKey";

    /// <summary>
    /// Authorization policy name registered by <c>AddTraxApiKeyAuth</c>. Requires
    /// an authenticated user authenticated via the <see cref="SchemeName"/> scheme.
    /// </summary>
    public const string PolicyName = "ApiKeyPolicy";

    /// <summary>
    /// Default HTTP header that carries the API key. Override via
    /// <c>ApiKeyAuthenticationOptions.HeaderName</c> if your consumer expects
    /// a different header.
    /// </summary>
    public const string HeaderName = "X-Api-Key";
}
