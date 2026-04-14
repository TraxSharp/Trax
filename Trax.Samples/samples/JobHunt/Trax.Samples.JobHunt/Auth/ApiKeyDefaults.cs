namespace Trax.Samples.JobHunt.Auth;

public static class ApiKeyDefaults
{
    public const string AuthenticationScheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    // WARNING: FAKE KEYS, never use plaintext keys in production.
    public const string AliceKey = "alice-key";
    public const string BobKey = "bob-key";
    public const string CharlieKey = "charlie-key";
}
