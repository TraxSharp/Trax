using Trax.Samples.GameServer.E2E.Factories;

namespace Trax.Samples.GameServer.E2E.ApiTests;

/// <summary>
/// Shares a single GameServerApiFactory across ALL API test fixtures.
/// Prevents connection pool exhaustion from multiple WebApplicationFactory instances.
/// </summary>
[SetUpFixture]
public class SharedApiSetup
{
    public static GameServerApiFactory Factory { get; private set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Factory = new GameServerApiFactory();
        _ = Factory.Services;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Factory.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }
}
