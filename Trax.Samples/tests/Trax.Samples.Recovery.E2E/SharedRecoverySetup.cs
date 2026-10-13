using Trax.Samples.Recovery.E2E.Factories;
using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Recovery.E2E.Utilities;

// In the assembly root namespace so this [SetUpFixture] covers every test namespace.
namespace Trax.Samples.Recovery.E2E;

/// <summary>
/// Starts the Recovery host once for the whole assembly. A database that cannot be reached fails
/// the run: a suite that skips and reports green would test nothing (Samples ADR 0001).
/// </summary>
[SetUpFixture]
public class SharedRecoverySetup
{
    public static RecoveryApiFactory Factory { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        Factory = new RecoveryApiFactory();
        // Building the host runs the Trax migrations and starts the scheduler and its workers.
        _ = Factory.Services;
        await TopicMapDrafts.ClearAsync(
            TestPostgres.WithPort(RecoveryApiFactory.DefaultConnectionString)
        );
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Factory.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }
}
