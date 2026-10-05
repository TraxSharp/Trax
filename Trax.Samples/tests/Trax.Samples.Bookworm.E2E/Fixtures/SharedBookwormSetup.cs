using Npgsql;
using Trax.Samples.Bookworm.E2E.Factories;

// Intentionally in the assembly root namespace so this [SetUpFixture] runs once for every test
// namespace in the assembly (a SetUpFixture only covers its own namespace and descendants).
namespace Trax.Samples.Bookworm.E2E;

/// <summary>
/// Builds the Bookworm API factory once for the whole assembly so every test shares one host and one
/// database connection pool. The catalog and lending schemas are dropped first, so every run starts
/// from the host's own bootstrap and seed rather than from what an earlier run left behind.
/// </summary>
/// <remarks>
/// An unreachable database fails the run. Skipping instead would report green while testing
/// nothing, the trap <c>docs/adr/0001-a-sample-e2e-database-must-be-one-ci-provisions.md</c> exists
/// to prevent.
/// </remarks>
[SetUpFixture]
public class SharedBookwormSetup
{
    public static BookwormApiFactory Factory { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await using (var connection = new NpgsqlConnection(BookwormApiFactory.TestConnectionString))
        {
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand(
                "DROP SCHEMA IF EXISTS lending CASCADE; DROP SCHEMA IF EXISTS catalog CASCADE;",
                connection
            );
            await drop.ExecuteNonQueryAsync();
        }

        Factory = new BookwormApiFactory();
        // Forces the host to build, create both schemas, and seed.
        _ = Factory.Services;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }
}
