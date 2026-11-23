using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.EnqueueContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Tests.Postgres.Integration.Fixtures;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

/// <summary>
/// An <c>OnQueue</c> hook runs while its enqueue holds a pooled connection with a transaction
/// open, so a hook that never returns would pin that connection for as long as it runs. The
/// enqueue gives the hook <c>MaxQueueHookDuration</c>: past it the enqueue fails, rolls back what
/// the hook had written, and releases the connection, whether or not the hook honours its token.
///
/// <para>Enforces <c>docs/adr/0004-an-onqueue-hook-runs-under-a-time-limit.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0004-an-onqueue-hook-runs-under-a-time-limit.md")]
public class QueueHookTimeLimitTests : TestSetup
{
    private const string Adr = "docs/adr/0004-an-onqueue-hook-runs-under-a-time-limit.md";

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);

    private ServiceProvider? _limited;

    [OneTimeSetUp]
    public void BuildLimitedHost()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        // A pool of two, so two hooks that never return would take every connection.
        var connectionString = new NpgsqlConnectionStringBuilder(
            TestPostgres.WithPort(
                configuration
                    .GetRequiredSection("Configuration")["DatabaseConnectionString"]
                    .NotNull()
            )
        )
        {
            MaxPoolSize = 2,
            Timeout = 5,
        }.ConnectionString;

        _limited = new ServiceCollection()
            .AddLogging()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UsePostgres(connectionString))
                    .AddMediator(mediator =>
                        mediator
                            .ScanAssemblies(typeof(AssemblyMarker).Assembly)
                            .WithMaxQueueHookDuration(Limit)
                    )
            )
            .BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeLimitedHost()
    {
        if (_limited is not null)
            await _limited.DisposeAsync();
    }

    [SetUp]
    public async Task ResetAsync()
    {
        HangingProbe.Reset();

        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = await factory.CreateDbContextAsync(CancellationToken.None);
        await using var contextDisposal = context.ConfigureAwait(false);
        await context
            .WorkQueues.Where(w =>
                w.TrainName != null && w.TrainName.Contains("QueueHookTimeLimitTests")
                || w.TrainName != null && w.TrainName.StartsWith("QueueLimit.Marker")
            )
            .ExecuteDeleteAsync();
    }

    // Lets any hook a test left hanging finish, so it does not outlive the fixture.
    [TearDown]
    public void ReleaseHangingHooks() => HangingProbe.Release();

    private async Task<int> RowsAsync(string trainNameFragment)
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var context = await factory
            .CreateDbContextAsync(CancellationToken.None)
            .ConfigureAwait(false);
        await using var contextDisposal = context.ConfigureAwait(false);
        return await context
            .WorkQueues.CountAsync(w =>
                w.TrainName != null && w.TrainName.Contains(trainNameFragment)
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task Hooks_that_never_return_fail_their_enqueues_and_free_the_pool()
    {
        var scope = _limited.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var hung = Task.WhenAll(
            Task.Run(() => execution.QueueAsync(typeof(IHangingTrain).FullName.NotNull(), "{}")),
            Task.Run(() => execution.QueueAsync(typeof(IHangingTrain).FullName.NotNull(), "{}"))
        );

        var act = async () =>
            await hung.WaitAsync(SignalTimeout, TimeProvider.System).ConfigureAwait(false);
        await act.Should()
            .ThrowAsync<QueueHookTimeoutException>(
                $"a hook past the limit fails its enqueue even when it ignores its token ({Adr})"
            );
        hung.Exception.NotNull()
            .InnerExceptions.Should()
            .HaveCount(2)
            .And.AllBeOfType<QueueHookTimeoutException>();

        // Both hooks are still running, but their connections are back in the pool of two.
        var unrelated = await execution
            .QueueAsync(typeof(IQuickTrain).FullName.NotNull(), "{}")
            .WaitAsync(SignalTimeout, TimeProvider.System);

        unrelated.WorkQueueId.Should().BePositive();
        (await RowsAsync(nameof(IHangingTrain))).Should().Be(0, "a failed enqueue leaves no entry");
    }

    [Test]
    public async Task A_write_the_hook_flushed_before_the_limit_is_rolled_back()
    {
        var scope = _limited.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var act = async () =>
            await execution
                .QueueAsync(typeof(IFlushThenHangTrain).FullName.NotNull(), "{}")
                .WaitAsync(SignalTimeout, TimeProvider.System)
                .ConfigureAwait(false);

        await act.Should().ThrowAsync<QueueHookTimeoutException>();

        (await RowsAsync("QueueLimit.Marker"))
            .Should()
            .Be(
                0,
                "the hook saved on the enqueue's context before hanging, and the limit fails the "
                    + $"enqueue by rolling that transaction back, so the flush is undone ({Adr})"
            );
        (await RowsAsync(nameof(IFlushThenHangTrain))).Should().Be(0);
    }

    [Test]
    public async Task The_hook_token_is_cancelled_at_the_limit()
    {
        var scope = _limited.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var act = async () =>
            await execution
                .QueueAsync(typeof(ICooperativeTrain).FullName.NotNull(), "{}")
                .WaitAsync(SignalTimeout, TimeProvider.System)
                .ConfigureAwait(false);

        (await act.Should().ThrowAsync<QueueHookTimeoutException>()).Which.Limit.Should().Be(Limit);
        (await HangingProbe.TokenCancelled.WaitAsync(SignalTimeout, TimeProvider.System))
            .Should()
            .BeTrue("a hook that honours its token stops at the limit");
    }

    [Test]
    public async Task An_enqueue_a_hook_starts_after_the_limit_is_refused()
    {
        var scope = _limited.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        LateEnqueueTrain.Execution = execution;

        var act = async () =>
            await execution
                .QueueAsync(typeof(ILateEnqueueTrain).FullName.NotNull(), "{}")
                .WaitAsync(SignalTimeout, TimeProvider.System)
                .ConfigureAwait(false);
        await act.Should().ThrowAsync<QueueHookTimeoutException>();

        // The hook is still running. Let it go on to enqueue.
        HangingProbe.Release();

        var late = async () =>
            await LateEnqueueTrain
                .Nested.Task.WaitAsync(SignalTimeout, TimeProvider.System)
                .ConfigureAwait(false);
        await late.Should()
            .ThrowAsync<InvalidOperationException>(
                "the enqueue it would have joined was rolled back, and committing on its own "
                    + $"would run work for a mutation the caller was told failed ({Adr})"
            );
        (await RowsAsync(nameof(IQuickTrain))).Should().Be(0);
    }

    [Test]
    public async Task A_hook_within_the_limit_is_unaffected()
    {
        var scope = _limited.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var result = await execution.QueueAsync(typeof(IQuickTrain).FullName.NotNull(), "{}");

        result.WorkQueueId.Should().BePositive();
    }

    // ── probes ──────────────────────────────────────────────────────

    public static class HangingProbe
    {
        private static TaskCompletionSource _gate = new();

        public static TaskCompletionSource<bool> TokenCancelledSource { get; private set; } = new();

        public static Task<bool> TokenCancelled => TokenCancelledSource.Task;

        public static void Reset()
        {
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            TokenCancelledSource = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }

        public static void Release() => _gate.TrySetResult();

        /// <summary>Waits for the test to release it, deliberately ignoring the hook's token.</summary>
        public static Task WaitIgnoringTokenAsync() => _gate.Task;
    }

    public record HangingInput
    {
        public string Label { get; init; } = "default";
    }

    public interface IHangingTrain : IServiceTrain<HangingInput, Unit>;

    public class HangingTrain : ServiceTrain<HangingInput, Unit>, IHangingTrain
    {
        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            HangingProbe.WaitIgnoringTokenAsync();

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record FlushThenHangInput
    {
        public string Label { get; init; } = "default";
    }

    public interface IFlushThenHangTrain : IServiceTrain<FlushThenHangInput, Unit>;

    public class FlushThenHangTrain(IEnqueueContextAccessor accessor)
        : ServiceTrain<FlushThenHangInput, Unit>,
            IFlushThenHangTrain
    {
        // Against the documented contract on purpose: saving on the enqueue's context flushes the
        // write before the enqueue commits, so only the transaction can take it back.
        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            await accessor
                .Current.NotNull()
                .Track(
                    WorkQueue.Create(
                        new CreateWorkQueue
                        {
                            TrainName = "QueueLimit.Marker",
                            Input = "{}",
                            InputTypeName = "QueueLimit.Marker",
                        }
                    )
                )
                .ConfigureAwait(false);
            await accessor.Current.NotNull().SaveChanges(ct).ConfigureAwait(false);
            await HangingProbe.WaitIgnoringTokenAsync().ConfigureAwait(false);
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record CooperativeInput
    {
        public string Label { get; init; } = "default";
    }

    public interface ICooperativeTrain : IServiceTrain<CooperativeInput, Unit>;

    public class CooperativeTrain : ServiceTrain<CooperativeInput, Unit>, ICooperativeTrain
    {
        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            try
            {
                await new TaskCompletionSource().Task.WaitAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                HangingProbe.TokenCancelledSource.TrySetResult(ct.IsCancellationRequested);
            }
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record LateEnqueueInput
    {
        public string Label { get; init; } = "default";
    }

    public interface ILateEnqueueTrain : IServiceTrain<LateEnqueueInput, Unit>;

    public class LateEnqueueTrain : ServiceTrain<LateEnqueueInput, Unit>, ILateEnqueueTrain
    {
        public static ITrainExecutionService? Execution { get; set; }

        public static TaskCompletionSource<QueueTrainResult> Nested { get; private set; } = new();

        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            Nested = new TaskCompletionSource<QueueTrainResult>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            await HangingProbe.WaitIgnoringTokenAsync().ConfigureAwait(false);

            try
            {
                Nested.TrySetResult(
                    await Execution
                        .NotNull()
                        .QueueAsync(
                            typeof(IQuickTrain).FullName.NotNull(),
                            "{}",
                            ct: CancellationToken.None
                        )
                        .ConfigureAwait(false)
                );
            }
            catch (Exception ex)
            {
                Nested.TrySetException(ex);
            }
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record QuickInput
    {
        public string Label { get; init; } = "default";
    }

    public interface IQuickTrain : IServiceTrain<QuickInput, Unit>;

    public class QuickTrain : ServiceTrain<QuickInput, Unit>, IQuickTrain
    {
        protected override Task OnQueue(Metadata metadata, CancellationToken ct) =>
            Task.CompletedTask;

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }
}
