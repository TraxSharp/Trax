namespace Trax.Samples.StateMachine.E2E.Utilities;

/// <summary>Who a test request comes from: nobody, or the holder of an API key.</summary>
public sealed record Caller(string Name, string? ApiKey = null)
{
    public static readonly Caller Anonymous = new("anonymous");

    /// <summary>The sample's Development-only demo key for user <c>alice</c>.</summary>
    public static readonly Caller Alice = new("alice", "alice-key-do-not-use-in-production");

    /// <summary>The sample's Development-only demo key for user <c>bob</c>.</summary>
    public static readonly Caller Bob = new("bob", "bob-key-do-not-use-in-production");

    public void Apply(HttpRequestMessage request)
    {
        if (ApiKey is not null)
            request.Headers.Add("X-Api-Key", ApiKey);
    }

    public override string ToString() => Name;
}
