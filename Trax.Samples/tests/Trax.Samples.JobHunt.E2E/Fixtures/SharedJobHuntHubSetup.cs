using NUnit.Framework;
using Trax.Samples.JobHunt.E2E.Factories;

namespace Trax.Samples.JobHunt.E2E;

/// <summary>
/// Shares a single JobHuntHubFactory across all E2E test fixtures in the assembly.
/// Prevents Postgres connection pool exhaustion from spinning up the host per fixture.
/// </summary>
[SetUpFixture]
public class SharedJobHuntHubSetup
{
    public static JobHuntHubFactory Factory { get; private set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Factory = new JobHuntHubFactory();
        // Accessing Services triggers host startup, which runs JobHunt schema migration
        // and Trax schema migration.
        _ = Factory.Services;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Factory.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }
}
