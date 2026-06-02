using Trax.Samples.Bookworm.E2E.Factories;

// Intentionally in the assembly root namespace so this [SetUpFixture] runs once for every test
// namespace in the assembly (a SetUpFixture only covers its own namespace and descendants).
namespace Trax.Samples.Bookworm.E2E;

/// <summary>
/// Builds the Bookworm API factory once for the whole assembly so every test shares one host and one
/// database connection pool. If the test database is unreachable, the whole assembly is skipped at
/// runtime (rather than failing) so the suite stays green in environments without the dedicated
/// PostgreSQL instance, while still running for real in CI.
/// </summary>
[SetUpFixture]
public class SharedBookwormSetup
{
    public static BookwormApiFactory Factory { get; private set; } = null!;
    public static bool DatabaseAvailable { get; private set; }

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Factory = new BookwormApiFactory();
        try
        {
            // Forces the host to build, run migrations, and seed. Throws if the DB is unreachable.
            _ = Factory.Services;
            using var client = Factory.CreateClient();
            DatabaseAvailable = true;
        }
        catch (Exception ex)
        {
            DatabaseAvailable = false;

            // In CI the database is provisioned, so an unreachable one is a real failure, not an
            // environment we should quietly skip. Skipping there would let every HTTP test silently
            // drop out and report 0% coverage while the build stays green. Fail loud instead.
            if (Environment.GetEnvironmentVariable("CI") is not null)
                throw;

            TestContext.Progress.WriteLine(
                $"Bookworm E2E database unavailable, HTTP tests will be skipped: {ex.Message}"
            );
        }
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Factory.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }
}
