using Trax.Samples.StateMachine.E2E.Factories;

// In the assembly root namespace so this [SetUpFixture] covers every test namespace.
namespace Trax.Samples.StateMachine.E2E;

/// <summary>
/// One Development host for the whole assembly. An unreachable database fails the run: a suite that
/// skipped instead would report green while proving nothing (Samples ADR 0001).
/// </summary>
[SetUpFixture]
public class SharedStateMachineSetup
{
    public static StateMachineApiFactory Factory { get; private set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Factory = new StateMachineApiFactory();
        // Builds and starts the host: the Trax migrations, snapshot_draft and effect_claim included, run here.
        _ = Factory.Services;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Factory.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }
}
