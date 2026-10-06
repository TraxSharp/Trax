using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trax.Samples.Scheduling.E2E.Factories;
using Trax.Samples.Scheduling.E2E.Utilities;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Scheduling.E2E;

/// <summary>
/// Sits in the suite's root namespace so NUnit runs it once around every test below it.
/// One Development host for the whole suite, started on an empty <c>trax</c> schema so every run
/// sees the sample's first start: the manifests seeded, the one-off announcement still due and
/// the supplier feed down.
/// </summary>
[SetUpFixture]
public class SharedSchedulingSetup
{
    public static SchedulingHostFactory Factory { get; private set; } = null!;

    public static Db Db { get; private set; } = null!;

    /// <summary>When the host started, by the test's clock.</summary>
    public static DateTime StartedAt { get; private set; }

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await DropTraxSchema();

        StartedAt = DateTime.UtcNow;
        Factory = new SchedulingHostFactory();

        // Resolving Services starts the host: migrations run and the manifests are seeded
        // before the polling services start.
        Db = new Db(Factory.Services);

        var seeded = await Polling.WaitUntilAsync(
            async () => await Db.Query(dc => dc.Manifests.CountAsync()) == 6,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(200)
        );
        seeded.Should().BeTrue("the host seeds the sample's six manifests at startup");
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }

    private static async Task DropTraxSchema()
    {
        await using var connection = new NpgsqlConnection(
            SchedulingHostFactory.TestConnectionString
        );
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DROP SCHEMA IF EXISTS trax CASCADE;",
            connection
        );
        await command.ExecuteNonQueryAsync();
    }
}
