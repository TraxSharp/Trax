using Trax.Samples.Board.E2E.Factories;

// In the assembly root namespace so this [SetUpFixture] covers every test namespace.
namespace Trax.Samples.Board.E2E;

/// <summary>
/// Starts the Board host once for the whole assembly. A database that cannot be reached fails the run: a suite that
/// skips and reports green would test nothing (Samples ADR 0001).
/// </summary>
[SetUpFixture]
public class SharedBoardSetup
{
    public static BoardApiFactory Factory { get; private set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Factory = new BoardApiFactory();
        // Building the host runs the Trax migrations and starts the scheduler and its workers.
        _ = Factory.Services;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Factory.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }
}
