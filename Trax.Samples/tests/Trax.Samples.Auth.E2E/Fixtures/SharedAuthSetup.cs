using Trax.Samples.Auth.E2E.Factories;

// In the assembly root namespace so this [SetUpFixture] covers every test namespace.
namespace Trax.Samples.Auth.E2E;

/// <summary>
/// One Development host for the whole assembly. An unreachable database fails the run: a suite
/// that skipped instead would report green while proving nothing (Samples ADR 0001).
/// </summary>
[SetUpFixture]
public class SharedAuthSetup
{
    public static AuthApiFactory Factory { get; private set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Factory = new AuthApiFactory();
        // Builds and starts the host: migrations, the newsroom schema and the seed all run here.
        _ = Factory.Services;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Factory.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }
}
