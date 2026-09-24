using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.EnqueueContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Tests.Postgres.Integration.Fixtures;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

/// <summary>
/// Covers the transactional contract of the queue-time hook.
///
/// <para>
/// <c>OnQueue</c> exists so a consumer can perform a side-effect the moment a mutation is
/// accepted — typically an optimistic shadow write. Before this change the hook committed on
/// its own connection and the work queue row committed afterwards on a different one, so a
/// crash between them left the side-effect persisted with no queued work to consume it.
/// </para>
///
/// <para>
/// These tests pin what the enqueue does and does not guarantee. A hook that writes through
/// the ambient context Trax supplies is atomic with the queue row. A hook that writes through
/// its own <c>DbContext</c> is NOT — EF can only share a transaction between contexts that
/// share a <c>DbConnection</c>, and a separately-pooled context does not. That limitation is
/// pinned deliberately so no consumer assumes a guarantee it does not have.
/// </para>
/// </summary>
[TestFixture]
public class QueueHookTransactionTests : TestSetup
{
    private ITrainExecutionService Execution =>
        Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

    /// <summary>Rows the hook wrote. Distinct from the enqueue's own work queue row.</summary>
    private async Task<long> CountMarkersAsync()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        return await context.WorkQueues.LongCountAsync(w =>
            w.TrainName!.StartsWith("QueueTx.Marker")
        );
    }

    /// <summary>The enqueue's own row for a test train.</summary>
    private async Task<long> CountEnqueuedAsync()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        return await context.WorkQueues.LongCountAsync(w =>
            w.TrainName!.Contains("QueueHookTransactionTests")
        );
    }

    [SetUp]
    public async Task ClearAsync()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        await context
            .WorkQueues.Where(w =>
                w.TrainName!.StartsWith("QueueTx.Marker")
                || w.TrainName!.Contains("QueueHookTransactionTests")
            )
            .ExecuteDeleteAsync();
    }

    // ────────────────────────────────────────────────────────────────
    // The guarantee: a hook writing through the ambient context is atomic
    // with the work queue row.
    // ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Hook_write_through_the_ambient_context_commits_with_the_queue_row()
    {
        await Execution.QueueAsync(
            typeof(IAmbientWritingTrain).FullName!,
            "{\"Value\":\"commit\"}"
        );

        (await CountMarkersAsync())
            .Should()
            .Be(1, "the hook's write went through the ambient context and was committed with it");
        (await CountEnqueuedAsync()).Should().Be(1, "the enqueue wrote its own work queue row");
    }

    [Test]
    public async Task Throwing_hook_rolls_back_a_write_it_had_already_made()
    {
        var act = async () =>
            await Execution.QueueAsync(
                typeof(IAmbientThenThrowTrain).FullName!,
                "{\"Value\":\"boom\"}"
            );

        (await act.Should().ThrowAsync<Exception>()).WithMessage(
            "*hook rejected the mutation*",
            "the hook itself must be what threw — not DI or resolution"
        );

        (await CountMarkersAsync())
            .Should()
            .Be(
                0,
                "a write the hook only tracked is never saved when the hook aborts; the flushed "
                    + "case, which needs the transaction, is the test below"
            );
        (await CountEnqueuedAsync()).Should().Be(0, "a throwing hook aborts the enqueue");
    }

    [Test]
    public async Task Throwing_hook_rolls_back_a_write_it_had_already_flushed()
    {
        var act = async () =>
            await Execution.QueueAsync(
                typeof(IAmbientFlushThenThrowTrain).FullName!,
                "{\"Value\":\"flushed\"}"
            );

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*after flushing*"
        );

        (await CountMarkersAsync())
            .Should()
            .Be(
                0,
                "the hook saved on the enqueue's context before throwing, so only the enqueue "
                    + "transaction's rollback can remove the write"
            );
        (await CountEnqueuedAsync()).Should().Be(0, "the flushed queue row rolls back too");
    }

    // ────────────────────────────────────────────────────────────────
    // The limitation, pinned on purpose.
    // ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Hook_write_through_its_own_context_is_NOT_rolled_back()
    {
        var act = async () =>
            await Execution.QueueAsync(
                typeof(IOwnContextThenThrowTrain).FullName!,
                "{\"Value\":\"boom\"}"
            );

        (await act.Should().ThrowAsync<Exception>()).WithMessage(
            "*hook rejected the mutation*",
            "the hook itself must be what threw — not DI or resolution"
        );

        (await CountMarkersAsync())
            .Should()
            .Be(
                1,
                "a separately-pooled DbContext has its own connection and therefore its own "
                    + "transaction. Trax cannot roll it back. A consumer needing atomicity must "
                    + "write through the ambient context"
            );
    }

    [Test]
    public async Task Ambient_context_is_null_outside_the_hook()
    {
        ObservingProbe.Reset();
        var accessor = Scope.ServiceProvider.GetRequiredService<IEnqueueContextAccessor>();

        await Execution.QueueAsync(typeof(IObservingTrain).FullName!, "{}");

        ObservingProbe
            .DuringHook.Should()
            .ContainSingle()
            .Which.Should()
            .BeTrue("the enqueue did enter the context, so its absence elsewhere means something");
        ObservingProbe
            .DuringKey.Should()
            .ContainSingle()
            .Which.Should()
            .BeFalse("the subject key is computed before the enqueue has a context to offer");
        accessor
            .Current.Should()
            .BeNull("the context is handed back when the enqueue returns, not left on the caller");

        await Execution.RunAsync(typeof(IObservingTrain).FullName!, "{}");

        ObservingProbe
            .DuringRun.Should()
            .ContainSingle()
            .Which.Should()
            .BeFalse("a run is not an enqueue, even when the same train was just queued");
    }

    // ────────────────────────────────────────────────────────────────
    // Probes
    // ────────────────────────────────────────────────────────────────

    public record AmbientInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public record AmbientThrowInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public record AmbientFlushInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public record OwnContextInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public record ObservingInput
    {
        public string Value { get; init; } = string.Empty;
    }

    /// <summary>Whether <see cref="IEnqueueContextAccessor.Current"/> held a context at each point.</summary>
    public static class ObservingProbe
    {
        public static List<bool> DuringHook { get; } = [];
        public static List<bool> DuringKey { get; } = [];
        public static List<bool> DuringRun { get; } = [];

        public static void Reset()
        {
            DuringHook.Clear();
            DuringKey.Clear();
            DuringRun.Clear();
        }
    }

    public interface IObservingTrain : IServiceTrain<ObservingInput, Unit>;

    public class ObservingTrain(IEnqueueContextAccessor accessor)
        : ServiceTrain<ObservingInput, Unit>,
            IObservingTrain
    {
        protected override string? QueueSubjectKey(Metadata metadata)
        {
            ObservingProbe.DuringKey.Add(accessor.Current is not null);
            return null;
        }

        protected override Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            ObservingProbe.DuringHook.Add(accessor.Current is not null);
            return Task.CompletedTask;
        }

        protected override Task<Either<Exception, Unit>> Junctions()
        {
            ObservingProbe.DuringRun.Add(accessor.Current is not null);
            return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
        }
    }

    private static WorkQueue Marker(string suffix) =>
        WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = $"QueueTx.Marker.{suffix}",
                Input = "{}",
                InputTypeName = typeof(AmbientInput).FullName,
                Priority = 0,
            }
        );

    public interface IAmbientWritingTrain : IServiceTrain<AmbientInput, Unit>;

    public class AmbientWritingTrain(IEnqueueContextAccessor accessor)
        : ServiceTrain<AmbientInput, Unit>,
            IAmbientWritingTrain
    {
        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            var context = accessor.Current!;
            await context.Track(Marker("ambient"));
        }

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface IAmbientThenThrowTrain : IServiceTrain<AmbientThrowInput, Unit>;

    public class AmbientThenThrowTrain(IEnqueueContextAccessor accessor)
        : ServiceTrain<AmbientThrowInput, Unit>,
            IAmbientThenThrowTrain
    {
        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            await accessor.Current!.Track(Marker("ambient-throw"));
            throw new InvalidOperationException("hook rejected the mutation after writing");
        }

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface IOwnContextThenThrowTrain : IServiceTrain<OwnContextInput, Unit>;

    public class OwnContextThenThrowTrain(IDataContextProviderFactory factory)
        : ServiceTrain<OwnContextInput, Unit>,
            IOwnContextThenThrowTrain
    {
        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            using var own = await factory.CreateDbContextAsync(ct);
            await own.Track(Marker("own-context"));
            await own.SaveChanges(ct);
            throw new InvalidOperationException("hook rejected the mutation after writing");
        }

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface IAmbientFlushThenThrowTrain : IServiceTrain<AmbientFlushInput, Unit>;

    public class AmbientFlushThenThrowTrain(IEnqueueContextAccessor accessor)
        : ServiceTrain<AmbientFlushInput, Unit>,
            IAmbientFlushThenThrowTrain
    {
        // Against the documented contract on purpose: a hook that saves on the enqueue's
        // context flushes the row and its own write before the enqueue commits.
        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            await accessor.Current!.Track(Marker("ambient-flush"));
            await accessor.Current!.SaveChanges(ct);
            throw new InvalidOperationException("hook rejected the mutation after flushing");
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }
}
