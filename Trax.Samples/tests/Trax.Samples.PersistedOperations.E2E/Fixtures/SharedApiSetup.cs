using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Samples.PersistedOperations.E2E.Fixtures;

namespace Trax.Samples.PersistedOperations.E2E;

/// <summary>
/// One-time setup: builds the API factory, ensures the persisted-operation
/// store is reachable, and seeds a clean state.
/// </summary>
[SetUpFixture]
public class SharedApiSetup
{
    public static PersistedOperationsApiFactory Factory { get; private set; } = null!;

    /// <summary>
    /// Starts the host. An unreachable database fails the whole suite rather than skipping it: a
    /// suite that reports green while testing nothing is the trap
    /// <c>docs/adr/0001-a-sample-e2e-database-must-be-one-ci-provisions.md</c> exists to prevent.
    /// </summary>
    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        Factory = new PersistedOperationsApiFactory();
        _ = Factory.Services;
        await ClearAsync(Factory.Services);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        try
        {
            await ClearAsync(Factory.Services);
        }
        catch
        {
            // Best-effort.
        }
        await Factory.DisposeAsync();
    }

    /// <summary>
    /// Hard-deletes every row in <c>trax.persisted_operation</c> and
    /// <c>trax.persisted_operation_history</c> so each test starts clean.
    /// Tests in this suite reuse a small set of ids (e.g. <c>greet_v1</c>)
    /// across cases and rely on the shape-diff guardrail being evaluated
    /// against a fresh insert, not a stale row from a previous test.
    /// </summary>
    public static async Task ClearAsync(IServiceProvider services)
    {
        var factory = services.GetRequiredService<IDataContextProviderFactory>();
        var ctx = await factory.CreateDbContextAsync(CancellationToken.None);
        await ctx.PersistedOperationHistories.ExecuteDeleteAsync();
        await ctx.PersistedOperations.ExecuteDeleteAsync();
        if (ctx is IAsyncDisposable async)
            await async.DisposeAsync();
    }
}
