using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
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
    public async Task OneTimeSetUp()
    {
        Factory = new StateMachineApiFactory();
        // Builds and starts the host: the Trax migrations, snapshot_draft and effect_claim included, run here.
        _ = Factory.Services;

        // The database outlives a run, and the demo callers hold at most the host's per-user draft count, so each run
        // starts from no drafts and no claims rather than inheriting every earlier run's.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDataContext>();
        await db.SnapshotDrafts.ExecuteDeleteAsync();
        await db.EffectClaims.ExecuteDeleteAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Factory.DisposeAsync();
        Npgsql.NpgsqlConnection.ClearAllPools();
    }
}
