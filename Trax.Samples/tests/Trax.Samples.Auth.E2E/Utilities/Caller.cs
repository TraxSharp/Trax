using System.Net.Http.Headers;

namespace Trax.Samples.Auth.E2E.Utilities;

/// <summary>Who a test request comes from: nobody, an API key, or a bearer token.</summary>
public sealed record Caller(string Name, string? ApiKey = null, string? BearerToken = null)
{
    public static readonly Caller Anonymous = new("anonymous");

    public static Caller WithApiKey(string name, string key) => new(name, ApiKey: key);

    public static Caller WithToken(string name, string token) => new(name, BearerToken: token);

    public void Apply(HttpRequestMessage request)
    {
        if (ApiKey is not null)
            request.Headers.Add("X-Api-Key", ApiKey);
        if (BearerToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);
    }

    public override string ToString() => Name;
}
