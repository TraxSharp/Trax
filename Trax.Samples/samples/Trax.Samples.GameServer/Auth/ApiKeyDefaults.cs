namespace Trax.Samples.GameServer.Auth;

/// <summary>
/// Constants for the fake API key authentication scheme.
/// These keys are plaintext and intentionally insecure — for demonstration only.
/// </summary>
public static class ApiKeyDefaults
{
    public const string AuthenticationScheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    // WARNING: FAKE KEYS — never use plaintext keys in production.
    public const string AdminKey = "admin-key-do-not-use-in-production";
    public const string PlayerKey = "player-key-do-not-use-in-production";
}
