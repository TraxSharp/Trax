using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.EnqueueContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.WorkQueuePromotion;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Tests.Postgres.Integration.Fixtures;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

/// <summary>
/// Covers two-phase enqueue: a train may hold its work queue entry unconfirmed until its
/// <c>OnQueue</c> hook has committed, so that a crash between the two leaves a recoverable entry
/// rather than a side-effect nothing will consume.
///
/// <para>
/// Immediate promotion is the default. Deferral is opt-in, because it costs an extra round trip and
/// only earns anything when the hook writes somewhere Trax's own transaction cannot reach.
/// </para>
///
/// <para>Enforces Trax.Docs/adr/0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md"
)]
[TestFixture]
public class DeferredPromotionTests : TestSetup
{
    private ITrainExecutionService Execution =>
        Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

    private IWorkQueuePromotion Promotion =>
        Scope.ServiceProvider.GetRequiredService<IWorkQueuePromotion>();

    private async Task<WorkQueue?> EntryAsync(long id)
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        return await context.WorkQueues.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
    }

    private async Task<long> InsertUnconfirmedAsync(DateTime createdAt)
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "DeferTx.Manual",
                Input = "{}",
                InputTypeName = typeof(DeferInput).FullName,
                DeferPromotion = true,
            }
        );
        entry.CreatedAt = createdAt;
        await context.Track(entry);
        await context.SaveChanges(CancellationToken.None);
        return entry.Id;
    }

    [SetUp]
    public async Task ResetAsync()
    {
        Observed.Clear();
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        await context
            .WorkQueues.Where(w =>
                w.TrainName!.Contains("DeferTx") || w.TrainName!.Contains("DeferredPromotionTests")
            )
            .ExecuteDeleteAsync();
    }

    // ── default: immediate promotion ────────────────────────────────

    [Test]
    public async Task A_train_without_a_hook_is_confirmed_immediately()
    {
        var result = await Execution.QueueAsync(typeof(IPlainTrain).FullName!, "{\"Value\":\"x\"}");

        (await EntryAsync(result.WorkQueueId))!
            .ConfirmedAt.Should()
            .NotBeNull("immediate promotion is the default — nothing is staged");
    }

    [Test]
    public async Task A_hook_that_does_not_defer_is_confirmed_immediately()
    {
        var result = await Execution.QueueAsync(
            typeof(IImmediateHookTrain).FullName!,
            "{\"Value\":\"x\"}"
        );

        (await EntryAsync(result.WorkQueueId))!
            .ConfirmedAt.Should()
            .NotBeNull(
                "overriding OnQueue alone must not change when the entry becomes dispatchable"
            );
    }

    [Test]
    public async Task Deferring_without_a_hook_is_confirmed_immediately()
    {
        var result = await Execution.QueueAsync(
            typeof(IDeferWithoutHookTrain).FullName!,
            "{\"Value\":\"x\"}"
        );

        var entry = (await EntryAsync(result.WorkQueueId))!;

        entry
            .ConfirmedAt.Should()
            .NotBeNull(
                "DeferQueuePromotion only has something to wait for when OnQueue is overridden; "
                    + "staging without a hook would leave an entry nothing ever promotes"
            );
    }

    // ── opt-in: deferred promotion ──────────────────────────────────

    [Test]
    public async Task A_deferring_train_stages_its_entry_unconfirmed_while_the_hook_runs()
    {
        var result = await Execution.QueueAsync(
            typeof(IDeferringTrain).FullName!,
            "{\"Value\":\"x\"}"
        );

        Observed
            .ConfirmedAtDuringHook.Should()
            .ContainSingle()
            .Which.Should()
            .BeNull(
                "the entry is committed unconfirmed BEFORE the hook runs — that is what makes a "
                    + "crash during the hook recoverable rather than invisible"
            );

        (await EntryAsync(result.WorkQueueId))!
            .ConfirmedAt.Should()
            .NotBeNull("a successful hook is followed by promotion");
    }

    [Test]
    public async Task A_deferring_train_whose_hook_throws_leaves_no_entry()
    {
        var act = async () =>
            await Execution.QueueAsync(typeof(IDeferringThrowTrain).FullName!, "{\"Value\":\"x\"}");

        (await act.Should().ThrowAsync<Exception>()).WithMessage("*hook rejected*");

        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        (await context.WorkQueues.CountAsync(w => w.TrainName!.Contains("DeferringThrowTrain")))
            .Should()
            .Be(0, "a rejected mutation leaves no trace — the staged entry is removed");
    }

    // ── promotion service ───────────────────────────────────────────

    [Test]
    public async Task PromoteAsync_confirms_an_unconfirmed_entry()
    {
        var id = await InsertUnconfirmedAsync(DateTime.UtcNow);

        (await Promotion.PromoteAsync(id, CancellationToken.None)).Should().BeTrue();
        (await EntryAsync(id))!.ConfirmedAt.Should().NotBeNull();
    }

    [Test]
    public async Task PromoteAsync_returns_false_when_the_entry_is_already_confirmed()
    {
        var id = await InsertUnconfirmedAsync(DateTime.UtcNow);
        await Promotion.PromoteAsync(id, CancellationToken.None);

        (await Promotion.PromoteAsync(id, CancellationToken.None))
            .Should()
            .BeFalse("callers distinguish a recovery from a no-op");
    }

    [Test]
    public async Task PromoteAsync_returns_false_for_an_entry_that_does_not_exist()
    {
        (await Promotion.PromoteAsync(-1, CancellationToken.None)).Should().BeFalse();
    }

    [Test]
    public async Task PromoteStaleAsync_promotes_only_entries_older_than_the_window()
    {
        var stale = await InsertUnconfirmedAsync(DateTime.UtcNow.AddMinutes(-30));
        var fresh = await InsertUnconfirmedAsync(DateTime.UtcNow);

        var promoted = await Promotion.PromoteStaleAsync(
            TimeSpan.FromMinutes(10),
            CancellationToken.None
        );

        promoted.Should().Be(1);
        (await EntryAsync(stale))!.ConfirmedAt.Should().NotBeNull();
        (await EntryAsync(fresh))!
            .ConfirmedAt.Should()
            .BeNull("an entry still inside the window may simply have a slow hook in flight");
    }

    [Test]
    public async Task PromoteStaleAsync_ignores_entries_that_are_already_confirmed()
    {
        await InsertUnconfirmedAsync(DateTime.UtcNow.AddMinutes(-30));
        await Promotion.PromoteStaleAsync(TimeSpan.FromMinutes(10), CancellationToken.None);

        (await Promotion.PromoteStaleAsync(TimeSpan.FromMinutes(10), CancellationToken.None))
            .Should()
            .Be(0);
    }

    [Test]
    public async Task CancelStaleAsync_cancels_only_entries_older_than_the_window()
    {
        var stale = await InsertUnconfirmedAsync(DateTime.UtcNow.AddMinutes(-30));
        var fresh = await InsertUnconfirmedAsync(DateTime.UtcNow);

        var cancelled = await Promotion.CancelStaleAsync(
            TimeSpan.FromMinutes(10),
            CancellationToken.None
        );

        cancelled.Should().Be(1);
        (await EntryAsync(stale))!.Status.Should().Be(WorkQueueStatus.Cancelled);
        (await EntryAsync(fresh))!.Status.Should().Be(WorkQueueStatus.Queued);
    }

    [Test]
    public async Task PromoteAsync_leaves_an_entry_cancelled_while_its_hook_ran_cancelled()
    {
        var id = await InsertUnconfirmedAsync(DateTime.UtcNow);

        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using (var context = await factory.CreateDbContextAsync(CancellationToken.None))
            await context
                .WorkQueues.Where(w => w.Id == id)
                .ExecuteUpdateAsync(u => u.SetProperty(w => w.Status, WorkQueueStatus.Cancelled));

        (await Promotion.PromoteAsync(id, CancellationToken.None))
            .Should()
            .BeFalse("confirming it would revive work an operator cancelled");
        (await EntryAsync(id))!.ConfirmedAt.Should().BeNull();
    }

    [Test]
    public async Task A_caller_that_gives_up_after_the_hook_returned_still_gets_its_entry_confirmed()
    {
        using var caller = new CancellationTokenSource();
        Observed.CancelCallerAfterHook = caller;

        var result = await Execution.QueueAsync(
            typeof(IDeferringCancelAfterTrain).FullName!,
            "{\"Value\":\"x\"}",
            ct: caller.Token
        );

        (await EntryAsync(result.WorkQueueId))!
            .ConfirmedAt.Should()
            .NotBeNull(
                "once the hook has returned its side-effect may have landed, so the entry that "
                    + "consumes it must not be stranded because the caller stopped listening"
            );
    }

    [Test]
    public async Task A_caller_that_gives_up_during_the_hook_leaves_no_entry()
    {
        using var caller = new CancellationTokenSource();
        Observed.CancelCallerDuringHook = caller;

        var act = async () =>
            await Execution.QueueAsync(
                typeof(IDeferringCancelDuringTrain).FullName!,
                "{\"Value\":\"x\"}",
                ct: caller.Token
            );

        await act.Should().ThrowAsync<OperationCanceledException>();

        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        (await context.WorkQueues.CountAsync(w => w.TrainName!.Contains("CancelDuringTrain")))
            .Should()
            .Be(
                0,
                "removing the staged entry must not depend on the token that was just cancelled"
            );
    }

    [Test]
    public async Task A_deferring_hook_has_no_enqueue_context()
    {
        await Execution.QueueAsync(typeof(IDeferringTrain).FullName!, "{\"Value\":\"x\"}");

        Observed
            .ContextDuringHook.Should()
            .ContainSingle()
            .Which.Should()
            .BeFalse("the entry is already committed, so there is no transaction to join");
    }

    [Test]
    public async Task An_entry_cancelled_while_its_hook_ran_makes_the_enqueue_fail()
    {
        var act = async () =>
            await Execution.QueueAsync(
                typeof(IDeferringSelfCancelTrain).FullName!,
                "{\"Value\":\"x\"}"
            );

        var cancelled = (
            await act.Should()
                .ThrowAsync<QueuedWorkCancelledException>(
                    "reporting success for work that will not run would be false, and a caller "
                        + "compensating for the hook's side-effect needs a type to catch"
                )
        ).Which;
        cancelled.WorkQueueId.Should().BePositive();
        cancelled.TrainName.Should().Be(typeof(IDeferringSelfCancelTrain).FullName);
        cancelled.Message.Should().Contain("cancelled before its OnQueue hook returned");
    }

    [Test]
    public async Task An_entry_promoted_elsewhere_while_its_hook_ran_is_a_successful_enqueue()
    {
        var result = await Execution.QueueAsync(
            typeof(IDeferringPromotedElsewhereTrain).FullName!,
            "{\"Value\":\"x\"}"
        );

        (await EntryAsync(result.WorkQueueId))!
            .ConfirmedAt.Should()
            .NotBeNull(
                "a promoting sweep confirmed it, so it will run and the enqueue did not fail"
            );
    }

    [Test]
    public async Task A_hook_that_throws_after_its_entry_was_promoted_leaves_the_entry_alone()
    {
        var act = async () =>
            await Execution.QueueAsync(
                typeof(IDeferringPromotedThenThrowsTrain).FullName!,
                "{\"Value\":\"x\"}"
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*after promotion*");

        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        (await context.WorkQueues.CountAsync(w => w.TrainName!.Contains("PromotedThenThrowsTrain")))
            .Should()
            .Be(
                1,
                "an entry that is no longer staged may already be running; deleting it would "
                    + "orphan the run and free its subject"
            );
    }

    // ── probes ──────────────────────────────────────────────────────

    /// <summary>What a hook saw about its own entry while it was running.</summary>
    public static class Observed
    {
        public static readonly List<DateTime?> ConfirmedAtDuringHook = [];
        public static readonly List<bool> ContextDuringHook = [];
        public static CancellationTokenSource? CancelCallerAfterHook;
        public static CancellationTokenSource? CancelCallerDuringHook;

        public static void Clear()
        {
            ConfirmedAtDuringHook.Clear();
            ContextDuringHook.Clear();
            CancelCallerAfterHook = null;
            CancelCallerDuringHook = null;
        }
    }

    public record DeferInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public record PlainInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public record ImmediateInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public record DeferThrowInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public interface IPlainTrain : IServiceTrain<PlainInput, Unit>;

    public class PlainTrain : ServiceTrain<PlainInput, Unit>, IPlainTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface IImmediateHookTrain : IServiceTrain<ImmediateInput, Unit>;

    public class ImmediateHookTrain : ServiceTrain<ImmediateInput, Unit>, IImmediateHookTrain
    {
        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            Task.CompletedTask;

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public record DeferWithoutHookInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public interface IDeferWithoutHookTrain : IServiceTrain<DeferWithoutHookInput, Unit>;

    public class DeferWithoutHookTrain
        : ServiceTrain<DeferWithoutHookInput, Unit>,
            IDeferWithoutHookTrain
    {
        protected override bool DeferQueuePromotion => true;

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public interface IDeferringTrain : IServiceTrain<DeferInput, Unit>;

    public class DeferringTrain(
        IDataContextProviderFactory factory,
        IEnqueueContextAccessor enqueueContext
    ) : ServiceTrain<DeferInput, Unit>, IDeferringTrain
    {
        protected override bool DeferQueuePromotion => true;

        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            Observed.ContextDuringHook.Add(enqueueContext.Current is not null);

            // Read the entry back on a fresh connection: the staging commit has landed, so the
            // row exists and must still be unconfirmed at this point.
            using var context = await factory.CreateDbContextAsync(ct);
            var row = await context
                .WorkQueues.AsNoTracking()
                .FirstOrDefaultAsync(w => w.ExternalId == metadata.ExternalId, ct);

            Observed.ConfirmedAtDuringHook.Add(row?.ConfirmedAt);
        }

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public interface IDeferringThrowTrain : IServiceTrain<DeferThrowInput, Unit>;

    public class DeferringThrowTrain : ServiceTrain<DeferThrowInput, Unit>, IDeferringThrowTrain
    {
        protected override bool DeferQueuePromotion => true;

        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            throw new InvalidOperationException("hook rejected the mutation");

        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public record CancelAfterInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public record CancelDuringInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public interface IDeferringCancelAfterTrain : IServiceTrain<CancelAfterInput, Unit>;

    public class DeferringCancelAfterTrain
        : ServiceTrain<CancelAfterInput, Unit>,
            IDeferringCancelAfterTrain
    {
        protected override bool DeferQueuePromotion => true;

        protected override Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            // The hook succeeds, and the caller gives up before promotion runs.
            Observed.CancelCallerAfterHook?.Cancel();
            return Task.CompletedTask;
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public interface IDeferringCancelDuringTrain : IServiceTrain<CancelDuringInput, Unit>;

    public class DeferringCancelDuringTrain
        : ServiceTrain<CancelDuringInput, Unit>,
            IDeferringCancelDuringTrain
    {
        protected override bool DeferQueuePromotion => true;

        protected override Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            Observed.CancelCallerDuringHook?.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record SelfCancelInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public record PromotedThenThrowsInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public interface IDeferringSelfCancelTrain : IServiceTrain<SelfCancelInput, Unit>;

    public class DeferringSelfCancelTrain(IDataContextProviderFactory factory)
        : ServiceTrain<SelfCancelInput, Unit>,
            IDeferringSelfCancelTrain
    {
        protected override bool DeferQueuePromotion => true;

        // What an operator, or the stale-entry sweep, does while a slow hook is still running.
        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            using var context = await factory.CreateDbContextAsync(ct);
            await context
                .WorkQueues.Where(w => w.ExternalId == metadata.ExternalId)
                .ExecuteUpdateAsync(
                    u => u.SetProperty(w => w.Status, WorkQueueStatus.Cancelled),
                    ct
                );
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public interface IDeferringPromotedThenThrowsTrain
        : IServiceTrain<PromotedThenThrowsInput, Unit>;

    public class DeferringPromotedThenThrowsTrain(IDataContextProviderFactory factory)
        : ServiceTrain<PromotedThenThrowsInput, Unit>,
            IDeferringPromotedThenThrowsTrain
    {
        protected override bool DeferQueuePromotion => true;

        // What an opted-in promoting sweep does to a slow hook's entry, before the hook fails.
        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            using var context = await factory.CreateDbContextAsync(ct);
            await context
                .WorkQueues.Where(w => w.ExternalId == metadata.ExternalId)
                .ExecuteUpdateAsync(u => u.SetProperty(w => w.ConfirmedAt, DateTime.UtcNow), ct);

            throw new InvalidOperationException("hook failed after promotion");
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record PromotedElsewhereInput
    {
        public string Value { get; init; } = string.Empty;
    }

    public interface IDeferringPromotedElsewhereTrain : IServiceTrain<PromotedElsewhereInput, Unit>;

    public class DeferringPromotedElsewhereTrain(IDataContextProviderFactory factory)
        : ServiceTrain<PromotedElsewhereInput, Unit>,
            IDeferringPromotedElsewhereTrain
    {
        protected override bool DeferQueuePromotion => true;

        // What an opted-in promoting sweep does to a slow hook's entry; the hook then succeeds.
        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            using var context = await factory.CreateDbContextAsync(ct);
            await context
                .WorkQueues.Where(w => w.ExternalId == metadata.ExternalId)
                .ExecuteUpdateAsync(u => u.SetProperty(w => w.ConfirmedAt, DateTime.UtcNow), ct);
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }
}
