using System.Collections.Concurrent;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

/// <summary>
/// <c>TrainInput</c> inside <c>QueueSubjectKey</c> and <c>OnQueue</c>, which run on an instance
/// that has not run: the enqueue hands the instance its input through
/// <c>ServiceTrain.EnterQueueHooks</c> around both hooks, on every enqueue path.
///
/// <para>Enforces Trax.Docs/adr/0021-a-queue-hook-reads-its-input-through-traininput.md.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0021-a-queue-hook-reads-its-input-through-traininput.md")]
[TestFixture]
public class QueueHookTrainInputTests
{
    private ServiceProvider _serviceProvider = null!;

    [SetUp]
    public void Setup()
    {
        TrainBus.ClearMethodCache();
        Seen.Clear();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(assemblies: [typeof(QueueHookTrainInputTests).Assembly])
        );

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        NestingOrderTrain.Execution = null;
        _serviceProvider.Dispose();
        TrainBus.ClearMethodCache();
    }

    private async Task<string?> SubjectKeyOfAsync(long workQueueId)
    {
        var factory = _serviceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        var entry = await context.WorkQueues.AsNoTracking().FirstAsync(w => w.Id == workQueueId);
        return entry.SubjectKey;
    }

    [Test]
    public async Task A_key_built_from_TrainInput_differs_per_input()
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var first = await execution.QueueAsync(typeof(IKeyedOrderTrain).FullName!, "{\"id\":1}");
        var second = await execution.QueueAsync(typeof(IKeyedOrderTrain).FullName!, "{\"id\":2}");

        (await SubjectKeyOfAsync(first.WorkQueueId)).Should().Be("order:1");
        (await SubjectKeyOfAsync(second.WorkQueueId))
            .Should()
            .Be(
                "order:2",
                "a key read from TrainInput was order:0 for every enqueue, which serialized every "
                    + "order behind every other"
            );
    }

    [Test]
    public async Task OnQueue_reads_TrainInput()
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var result = await execution.QueueAsync(typeof(IKeyedOrderTrain).FullName!, "{\"id\":7}");

        Seen.Should().ContainKey(result.ExternalId).WhoseValue.Should().Be(7);
    }

    [Test]
    public async Task A_deferring_train_reads_TrainInput_in_OnQueue()
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var result = await execution.QueueAsync(
            typeof(IDeferringOrderTrain).FullName!,
            "{\"id\":11}"
        );

        Seen.Should().ContainKey(result.ExternalId).WhoseValue.Should().Be(11);
    }

    [Test]
    public async Task A_train_enqueued_from_another_hook_reads_its_own_TrainInput()
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        NestingOrderTrain.Execution = execution;

        var outer = await execution.QueueAsync(typeof(INestingOrderTrain).FullName!, "{\"id\":3}");

        Seen.Should().ContainKey(outer.ExternalId).WhoseValue.Should().Be(3);
        Seen.Values.Should()
            .Contain(
                1003,
                "the nested train's hooks see the nested enqueue's input, not the outer one's"
            );
        NestingOrderTrain
            .InputAfterNestedEnqueue.Should()
            .Be(3, "the outer hook still reads its own input once the nested enqueue returns");
    }

    // ── probes ──────────────────────────────────────────────────────

    private static readonly ConcurrentDictionary<string, int> Seen = new();

    public record OrderInput
    {
        public int Id { get; init; }
    }

    public record DeferredOrderInput
    {
        public int Id { get; init; }
    }

    public record NestingOrderInput
    {
        public int Id { get; init; }
    }

    public interface IKeyedOrderTrain : IServiceTrain<OrderInput, Unit>;

    public class KeyedOrderTrain : ServiceTrain<OrderInput, Unit>, IKeyedOrderTrain
    {
        protected override string? QueueSubjectKey(Metadata metadata) => $"order:{TrainInput.Id}";

        protected override Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            Seen[metadata.ExternalId] = TrainInput.Id;
            return Task.CompletedTask;
        }

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface IDeferringOrderTrain : IServiceTrain<DeferredOrderInput, Unit>;

    public class DeferringOrderTrain : ServiceTrain<DeferredOrderInput, Unit>, IDeferringOrderTrain
    {
        protected override bool DeferQueuePromotion => true;

        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            // Past an await, so the input has to flow with the hook, not just be set on entry.
            await Task.Yield();
            Seen[metadata.ExternalId] = TrainInput.Id;
        }

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface INestingOrderTrain : IServiceTrain<NestingOrderInput, Unit>;

    public class NestingOrderTrain : ServiceTrain<NestingOrderInput, Unit>, INestingOrderTrain
    {
        public static ITrainExecutionService? Execution { get; set; }

        public static int InputAfterNestedEnqueue { get; private set; }

        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            Seen[metadata.ExternalId] = TrainInput.Id;

            await Execution!.QueueAsync(
                typeof(IKeyedOrderTrain).FullName!,
                $"{{\"id\":{1000 + TrainInput.Id}}}",
                ct: ct
            );

            InputAfterNestedEnqueue = TrainInput.Id;
        }

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}
