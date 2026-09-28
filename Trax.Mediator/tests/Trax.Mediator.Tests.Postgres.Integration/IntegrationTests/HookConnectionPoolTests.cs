using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Tests.Postgres.Integration.Fixtures;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

/// <summary>
/// An enqueue whose <c>OnQueue</c> hook is running holds one pooled connection, the one its
/// transaction is open on. A nested enqueue used to need a second, so hooks that enqueue could
/// exhaust a small pool between them and time out. Joining the outer transaction
/// (mediator/0003) means the nested enqueue writes on the connection its outer enqueue already
/// holds.
///
/// <para>Enforces <c>docs/adr/0003-a-nested-enqueue-joins-the-enqueue-it-runs-inside.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0003-a-nested-enqueue-joins-the-enqueue-it-runs-inside.md")]
public class HookConnectionPoolTests : TestSetup
{
    private ServiceProvider _smallPool = null!;

    [OneTimeSetUp]
    public void BuildSmallPool()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var connectionString = new NpgsqlConnectionStringBuilder(
            TestPostgres.WithPort(
                configuration.GetRequiredSection("Configuration")["DatabaseConnectionString"]!
            )
        )
        {
            MaxPoolSize = 2,
            Timeout = 5,
        }.ConnectionString;

        _smallPool = new ServiceCollection()
            .AddLogging()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UsePostgres(connectionString))
                    .AddMediator(assemblies: [typeof(AssemblyMarker).Assembly])
            )
            .BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeSmallPool() => await _smallPool.DisposeAsync();

    [Test]
    public async Task Two_hooks_that_enqueue_and_wait_do_not_exhaust_a_pool_of_two()
    {
        GatedProbe.Reset(expected: 2);

        using var scope = _smallPool.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var outers = Task.WhenAll(
            Task.Run(() => execution.QueueAsync(typeof(IGatedOuterTrain).FullName!, "{}")),
            Task.Run(() => execution.QueueAsync(typeof(IGatedOuterTrain).FullName!, "{}"))
        );

        // Both hooks have made their nested enqueue and are holding their transactions open.
        await AwaitSignalAsync(
            GatedProbe.AllArrived,
            outers,
            "both hooks finished their nested enqueue"
        );

        var unrelated = Task.Run(() =>
            execution.QueueAsync(typeof(IPoolLeafTrain).FullName!, "{}")
        );

        GatedProbe.Open();

        var act = async () => await Task.WhenAll(outers, unrelated).WaitAsync(SignalTimeout);
        await act.Should()
            .NotThrowAsync(
                "each outer enqueue holds one connection and its nested enqueue writes on that "
                    + "same one, so the pool has room for the unrelated enqueue as soon as the "
                    + "hooks return (docs/adr/0003-a-nested-enqueue-joins-the-enqueue-it-runs-inside.md)"
            );

        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        (await context.WorkQueues.CountAsync(w => w.TrainName == typeof(IPoolLeafTrain).FullName))
            .Should()
            .Be(3, "two nested entries and the unrelated one");
    }

    /// <summary>Holds each hook after its nested enqueue until the test opens the gate.</summary>
    public static class GatedProbe
    {
        private static int _expected;
        private static int _arrived;
        private static TaskCompletionSource _allArrived = new();
        private static TaskCompletionSource _gate = new();

        public static Task AllArrived => _allArrived.Task;

        public static void Reset(int expected)
        {
            _expected = expected;
            _arrived = 0;
            _allArrived = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static void Open() => _gate.TrySetResult();

        public static Task ArriveAndWaitAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _arrived) == _expected)
                _allArrived.TrySetResult();

            return _gate.Task.WaitAsync(SignalTimeout, ct);
        }
    }

    public record GatedOuterInput
    {
        public string Label { get; init; } = "default";
    }

    public interface IGatedOuterTrain : IServiceTrain<GatedOuterInput, Unit>;

    public class GatedOuterTrain(ITrainExecutionService execution)
        : ServiceTrain<GatedOuterInput, Unit>,
            IGatedOuterTrain
    {
        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            await execution.QueueAsync(typeof(IPoolLeafTrain).FullName!, "{}", ct: ct);
            await GatedProbe.ArriveAndWaitAsync(ct);
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record PoolLeafInput
    {
        public string Label { get; init; } = "default";
    }

    public interface IPoolLeafTrain : IServiceTrain<PoolLeafInput, Unit>;

    public class PoolLeafTrain : ServiceTrain<PoolLeafInput, Unit>, IPoolLeafTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }
}
