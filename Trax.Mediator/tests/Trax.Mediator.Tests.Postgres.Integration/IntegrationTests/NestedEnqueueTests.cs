using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Tests.Postgres.Integration.Fixtures;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

/// <summary>
/// An enqueue started from inside an <c>OnQueue</c> hook joins the enqueue it runs inside: its
/// entry is written in the outer enqueue's transaction, so it commits with the outer entry or not
/// at all. Before, it committed on a context and connection of its own, and survived an outer
/// enqueue that then failed, so work ran for a mutation the caller was told was refused.
///
/// <para>Enforces <c>docs/adr/0003-a-nested-enqueue-joins-the-enqueue-it-runs-inside.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0003-a-nested-enqueue-joins-the-enqueue-it-runs-inside.md")]
public class NestedEnqueueTests : TestSetup
{
    private const string Adr = "docs/adr/0003-a-nested-enqueue-joins-the-enqueue-it-runs-inside.md";

    private ITrainExecutionService Execution =>
        Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

    private static string Json(object input) =>
        JsonSerializer.Serialize(input, TraxEffectConfiguration.StaticSystemJsonSerializerOptions);

    private async Task<List<WorkQueue>> RowsAsync<TTrain>()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        return await context
            .WorkQueues.AsNoTracking()
            .Where(w => w.TrainName == typeof(TTrain).FullName)
            .ToListAsync();
    }

    [Test]
    public async Task An_outer_hook_that_throws_after_enqueueing_takes_the_nested_entry_with_it()
    {
        var act = async () =>
            await Execution.QueueAsync(
                typeof(IOuterTrain).FullName!,
                Json(new OuterInput { Leaf = typeof(ILeafTrain).FullName!, ThrowAfter = true })
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*outer refused*");

        (await RowsAsync<ILeafTrain>())
            .Should()
            .BeEmpty(
                $"the nested entry was written in the outer enqueue's transaction, which rolled "
                    + $"back ({Adr})"
            );
        (await RowsAsync<IOuterTrain>()).Should().BeEmpty();
    }

    [Test]
    public async Task A_nested_enqueue_commits_with_the_outer_one()
    {
        var outer = await Execution.QueueAsync(
            typeof(IOuterTrain).FullName!,
            Json(new OuterInput { Leaf = typeof(ILeafTrain).FullName! })
        );

        var leaf = (await RowsAsync<ILeafTrain>()).Should().ContainSingle().Subject;
        leaf.Id.Should()
            .Be(OuterTrain.LastNestedId, "the nested enqueue returned the id of the row it wrote");
        leaf.ConfirmedAt.Should().NotBeNull("it is dispatchable once the outer enqueue commits");
        (await RowsAsync<IOuterTrain>())
            .Should()
            .ContainSingle()
            .Which.Id.Should()
            .Be(outer.WorkQueueId);
    }

    [Test]
    public async Task A_nested_deferring_train_joins_too_and_is_not_left_staged()
    {
        var refused = async () =>
            await Execution.QueueAsync(
                typeof(IOuterTrain).FullName!,
                Json(
                    new OuterInput
                    {
                        Leaf = typeof(IDeferringLeafTrain).FullName!,
                        ThrowAfter = true,
                    }
                )
            );

        await refused.Should().ThrowAsync<InvalidOperationException>();
        (await RowsAsync<IDeferringLeafTrain>())
            .Should()
            .BeEmpty(
                $"a deferring train nested in a hook is part of the outer transaction ({Adr})"
            );

        await Execution.QueueAsync(
            typeof(IOuterTrain).FullName!,
            Json(new OuterInput { Leaf = typeof(IDeferringLeafTrain).FullName! })
        );

        (await RowsAsync<IDeferringLeafTrain>())
            .Should()
            .ContainSingle()
            .Which.ConfirmedAt.Should()
            .NotBeNull(
                "the outer commit is what makes it visible, so there is no staged state for the "
                    + "stale-entry sweep to find"
            );
    }

    [Test]
    public async Task A_nested_failure_the_hook_swallows_still_fails_the_outer_enqueue()
    {
        var act = async () =>
            await Execution.QueueAsync(
                typeof(IOuterTrain).FullName!,
                Json(new OuterInput { Leaf = typeof(IThrowingLeafTrain).FullName!, Swallow = true })
            );

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*enqueued inside*OnQueue*failed*")
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*leaf refused*");

        (await RowsAsync<IOuterTrain>())
            .Should()
            .BeEmpty(
                $"a failed nested enqueue may have left writes on the shared context, so the "
                    + $"whole enqueue rolls back ({Adr})"
            );
    }

    [Test]
    public async Task An_enqueue_nested_in_a_deferring_hook_has_nothing_to_join_and_stands_alone()
    {
        var act = async () =>
            await Execution.QueueAsync(
                typeof(IDeferringOuterTrain).FullName!,
                Json(new DeferringOuterInput())
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*outer refused*");

        (await RowsAsync<ILeafTrain>())
            .Should()
            .ContainSingle(
                $"a deferring hook runs after its own entry committed, outside any transaction, "
                    + $"so an enqueue it starts commits on its own ({Adr})"
            );
        (await RowsAsync<IDeferringOuterTrain>())
            .Should()
            .BeEmpty("the deferring train's staged entry is removed when its hook throws");
    }

    [Test]
    public async Task Nested_enqueues_a_hook_runs_at_once_all_commit_with_the_outer_one()
    {
        await Execution.QueueAsync(
            typeof(IOuterTrain).FullName!,
            Json(new OuterInput { Leaf = typeof(ILeafTrain).FullName!, Fanout = 4 })
        );

        (await RowsAsync<ILeafTrain>())
            .Should()
            .HaveCount(4, "the nested enqueues share one context, so their writes are serialized");
    }

    [Test]
    public async Task A_hook_that_returns_before_its_nested_enqueue_finishes_fails_the_enqueue()
    {
        GatedLeafTrain.Reset();

        var act = async () =>
            await Execution.QueueAsync(
                typeof(IUnawaitedOuterTrain).FullName!,
                Json(new UnawaitedOuterInput())
            );

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "*returned while an enqueue it started was still running*"
        );

        GatedLeafTrain.Gate.TrySetResult();
        await DrainAsync(UnawaitedOuterTrain.Started!);

        (await RowsAsync<IUnawaitedOuterTrain>())
            .Should()
            .BeEmpty(
                $"the enqueue could not know whether its nested enqueue would succeed ({Adr})"
            );
        (await RowsAsync<IGatedLeafTrain>()).Should().BeEmpty();
    }

    // ── trains ──────────────────────────────────────────────────────

    public record OuterInput
    {
        public string Leaf { get; init; } = "";
        public bool ThrowAfter { get; init; }
        public bool Swallow { get; init; }
        public int Fanout { get; init; } = 1;
    }

    public interface IOuterTrain : IServiceTrain<OuterInput, Unit>;

    public class OuterTrain(ITrainExecutionService execution)
        : ServiceTrain<OuterInput, Unit>,
            IOuterTrain
    {
        public static long LastNestedId { get; private set; }

        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            var input = metadata.GetInput<OuterInput>()!;

            try
            {
                var results = await Task.WhenAll(
                    Enumerable
                        .Range(0, input.Fanout)
                        .Select(_ => execution.QueueAsync(input.Leaf, "{}", ct: ct))
                );
                LastNestedId = results[^1].WorkQueueId;
            }
            catch when (input.Swallow)
            {
                // The hook decides the nested failure does not matter to it.
            }

            if (input.ThrowAfter)
                throw new InvalidOperationException("outer refused the mutation");
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record DeferringOuterInput
    {
        public string Label { get; init; } = "default";
    }

    public interface IDeferringOuterTrain : IServiceTrain<DeferringOuterInput, Unit>;

    public class DeferringOuterTrain(ITrainExecutionService execution)
        : ServiceTrain<DeferringOuterInput, Unit>,
            IDeferringOuterTrain
    {
        protected override bool DeferQueuePromotion => true;

        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            await execution.QueueAsync(typeof(ILeafTrain).FullName!, "{}", ct: ct);
            throw new InvalidOperationException("outer refused the mutation");
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record UnawaitedOuterInput
    {
        public string Label { get; init; } = "default";
    }

    public interface IUnawaitedOuterTrain : IServiceTrain<UnawaitedOuterInput, Unit>;

    /// <summary>Starts a nested enqueue and returns once its hook is running, without awaiting it.</summary>
    public class UnawaitedOuterTrain(ITrainExecutionService execution)
        : ServiceTrain<UnawaitedOuterInput, Unit>,
            IUnawaitedOuterTrain
    {
        public static Task? Started { get; private set; }

        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            Started = execution.QueueAsync(typeof(IGatedLeafTrain).FullName!, "{}", ct: ct);
            await GatedLeafTrain.Arrived.Task.WaitAsync(SignalTimeout, ct);
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record GatedLeafInput
    {
        public string Label { get; init; } = "default";
    }

    public interface IGatedLeafTrain : IServiceTrain<GatedLeafInput, Unit>;

    public class GatedLeafTrain : ServiceTrain<GatedLeafInput, Unit>, IGatedLeafTrain
    {
        public static TaskCompletionSource Arrived { get; private set; } = new();
        public static TaskCompletionSource Gate { get; private set; } = new();

        public static void Reset()
        {
            Arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            Arrived.TrySetResult();
            await Gate.Task.WaitAsync(SignalTimeout, ct);
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record LeafInput
    {
        public string Label { get; init; } = "default";
    }

    public interface ILeafTrain : IServiceTrain<LeafInput, Unit>;

    public class LeafTrain : ServiceTrain<LeafInput, Unit>, ILeafTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record DeferringLeafInput
    {
        public string Label { get; init; } = "default";
    }

    public interface IDeferringLeafTrain : IServiceTrain<DeferringLeafInput, Unit>;

    public class DeferringLeafTrain : ServiceTrain<DeferringLeafInput, Unit>, IDeferringLeafTrain
    {
        protected override bool DeferQueuePromotion => true;

        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            Task.CompletedTask;

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record ThrowingLeafInput
    {
        public string Label { get; init; } = "default";
    }

    public interface IThrowingLeafTrain : IServiceTrain<ThrowingLeafInput, Unit>;

    public class ThrowingLeafTrain : ServiceTrain<ThrowingLeafInput, Unit>, IThrowingLeafTrain
    {
        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            throw new InvalidOperationException("leaf refused the mutation");

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }
}
