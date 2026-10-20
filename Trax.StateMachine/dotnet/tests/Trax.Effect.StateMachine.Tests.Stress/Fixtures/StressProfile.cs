namespace Trax.Effect.StateMachine.Tests.Stress.Fixtures;

/// <summary>
/// Whether the stress suite runs, and the knobs that size it. Off by default (a stress run seeds and drives
/// thousands of operations against a real database), so normal test runs skip it. Turn it on and tune it
/// with environment variables:
/// <code>
/// TRAX_STRESS=1 \
///   TRAX_STRESS_INSTANCES=200 TRAX_STRESS_SENDS=4 \
///   dotnet test tests/Trax.Effect.StateMachine.Tests.Stress
/// </code>
/// </summary>
internal static class StressProfile
{
    public static bool Enabled =>
        Environment.GetEnvironmentVariable("TRAX_STRESS") is "1" or "true";

    public const string SkipReason =
        "Stress suite. Run with: TRAX_STRESS=1 dotnet test tests/Trax.Effect.StateMachine.Tests.Stress";

    // Exactly-once fan-out.
    public static int Instances => Env("TRAX_STRESS_INSTANCES", 200);
    public static int SendsPerInstance => Env("TRAX_STRESS_SENDS", 4);
    public static int HotSends => Env("TRAX_STRESS_HOT", 64);

    // Throughput.
    public static int ThroughputInstances => Env("TRAX_STRESS_THROUGHPUT", 500);

    // Claim contention.
    public static int ClaimKeys => Env("TRAX_STRESS_CLAIM_KEYS", 100);
    public static int ClaimantsPerKey => Env("TRAX_STRESS_CLAIMANTS", 16);

    // Bound on in-flight operations, kept under Postgres's default max_connections.
    public static int MaxConcurrency => Env("TRAX_STRESS_CONCURRENCY", 40);

    private static int Env(string key, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(key), out var v) && v > 0 ? v : fallback;
}
