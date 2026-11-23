using System.Globalization;
using System.Text.RegularExpressions;
using Npgsql;

namespace Trax.Api.Tests.Stress.Utils;

/// <summary>
/// Row counts for a stress run. Defaults target genuine millions; override any value
/// with the matching <c>TRAX_STRESS_*</c> environment variable for smaller/larger runs.
/// </summary>
/// <remarks>
/// FK-bearing columns are wired by modular arithmetic against these counts (e.g. a
/// metadata row's <c>manifest_id</c> is <c>1 + (g % Manifests)</c>), which is valid
/// because every table is truncated with <c>RESTART IDENTITY</c> before seeding, so
/// identity/serial ids run 1..N with no gaps.
/// </remarks>
public sealed record StressProfile(
    long Metadata,
    long Log,
    long WorkQueue,
    long DeadLetter,
    int Manifests,
    int Groups,
    int TrainNames,
    int PersistedOperations,
    long Decisions
)
{
    public static StressProfile FromEnvironment() =>
        new(
            Metadata: EnvLong("TRAX_STRESS_METADATA", 3_000_000),
            Log: EnvLong("TRAX_STRESS_LOG", 3_000_000),
            WorkQueue: EnvLong("TRAX_STRESS_WORKQUEUE", 1_500_000),
            DeadLetter: EnvLong("TRAX_STRESS_DEADLETTER", 1_000_000),
            Manifests: (int)EnvLong("TRAX_STRESS_MANIFEST", 5_000),
            Groups: (int)EnvLong("TRAX_STRESS_GROUP", 200),
            TrainNames: (int)EnvLong("TRAX_STRESS_NAMES", 50),
            // Persisted operations grow with releases, not with traffic, so they get a large
            // catalog rather than millions: many tenants' documents, most of them retired.
            PersistedOperations: (int)EnvLong("TRAX_STRESS_PERSISTED_OPS", 100_000),
            // Only runs whose trains ask deciders record decisions, a few each, so the table is a
            // fraction of the run table's size.
            Decisions: EnvLong("TRAX_STRESS_DECISIONS", 1_000_000)
        )
        {
            SnapshotDrafts = EnvLong("TRAX_STRESS_SNAPSHOT_DRAFTS", 2_000_000),
        };

    /// <summary>
    /// State-machine instances (<c>trax.snapshot_draft</c> rows) to seed. A profile built by hand
    /// seeds none unless it says so.
    /// </summary>
    public long SnapshotDrafts { get; init; }

    private static long EnvLong(string name, long fallback) =>
        long.TryParse(
            Environment.GetEnvironmentVariable(name),
            System.Globalization.CultureInfo.InvariantCulture,
            out var v
        )
        && v > 0
            ? v
            : fallback;
}
