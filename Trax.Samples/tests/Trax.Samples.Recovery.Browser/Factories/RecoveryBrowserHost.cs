using Trax.Samples.Recovery.E2E.Factories;
using Trax.Samples.Recovery.E2E.Fixtures;

namespace Trax.Samples.Recovery.Browser.Factories;

/// <summary>
/// The Recovery host the browser tests drive: <see cref="RecoveryHostProcess"/>, on a database of its own
/// (<c>recovery_browser_tests</c>, which CI provisions, Samples ADR 0001), so a test can kill and restart it.
/// </summary>
public static class RecoveryBrowserHost
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=recovery_browser_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=10;Minimum Pool Size=0";

    /// <summary>How long each junction takes: long enough to catch a build in the middle.</summary>
    public static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(400);

    public static string ConnectionString => TestPostgres.WithPort(DefaultConnectionString);

    /// <summary>
    /// How often the host's reconciler sweeps: a test that makes the review timer due by rewriting the row is not
    /// heard by the reconciler, so the timer fires on its next sweep, which must come well inside
    /// <see cref="Fixtures.BrowserTimeouts.Server"/>.
    /// </summary>
    public static readonly TimeSpan MachineSweepInterval = TimeSpan.FromSeconds(1);

    public static Task<RecoveryHostProcess> StartAsync() =>
        RecoveryHostProcess.StartAsync(
            StepDelay,
            ConnectionString,
            machineSweepInterval: MachineSweepInterval
        );
}
