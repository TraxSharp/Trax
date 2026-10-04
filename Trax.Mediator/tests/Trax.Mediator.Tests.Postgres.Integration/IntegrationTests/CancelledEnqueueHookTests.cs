using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Tests.Postgres.Integration.Fixtures;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

/// <summary>
/// A caller that cancels while an <c>OnQueue</c> hook runs is told its enqueue failed. A hook
/// that ignores its token keeps running after that, and must not be able to commit work on its
/// own for the mutation the caller was told failed, the same as after the hook time limit.
///
/// <para>Enforces <c>docs/adr/0004-an-onqueue-hook-runs-under-a-time-limit.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0004-an-onqueue-hook-runs-under-a-time-limit.md")]
public class CancelledEnqueueHookTests : TestSetup
{
    private const string Adr = "docs/adr/0004-an-onqueue-hook-runs-under-a-time-limit.md";

    private async Task<int> RowsAsync(string trainNameFragment)
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        return await context.WorkQueues.CountAsync(w => w.TrainName!.Contains(trainNameFragment));
    }

    [Test]
    public async Task An_enqueue_a_hook_starts_after_its_caller_cancelled_does_not_commit_on_its_own()
    {
        OrphanedHookTrain.Reset();
        var execution = Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        OrphanedHookTrain.Execution = execution;

        using var cancel = new CancellationTokenSource();
        var outer = execution.QueueAsync(
            typeof(IOrphanedHookTrain).FullName!,
            "{}",
            ct: cancel.Token
        );

        await OrphanedHookTrain.Started.Task.WaitAsync(SignalTimeout);
        await cancel.CancelAsync();

        var act = async () => await outer.WaitAsync(SignalTimeout);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // The caller has been told the enqueue failed. The hook, which ignores its token, is still
        // running; let it go on to enqueue.
        OrphanedHookTrain.Gate.TrySetResult();

        var nested = async () => await OrphanedHookTrain.Nested.Task.WaitAsync(SignalTimeout);
        await nested
            .Should()
            .ThrowAsync<InvalidOperationException>(
                "the enqueue it ran inside was rolled back, and committing on its own would run "
                    + $"work for a mutation the caller was told failed ({Adr})"
            );
        (await RowsAsync(nameof(IOrphanNestedTrain))).Should().Be(0);
    }

    public record OrphanedHookInput
    {
        public string Label { get; init; } = "default";
    }

    public interface IOrphanedHookTrain : IServiceTrain<OrphanedHookInput, Unit>;

    public class OrphanedHookTrain : ServiceTrain<OrphanedHookInput, Unit>, IOrphanedHookTrain
    {
        public static ITrainExecutionService? Execution { get; set; }

        public static TaskCompletionSource Started { get; private set; } = new();

        public static TaskCompletionSource Gate { get; private set; } = new();

        public static TaskCompletionSource<QueueTrainResult> Nested { get; private set; } = new();

        public static void Reset()
        {
            Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Nested = new TaskCompletionSource<QueueTrainResult>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }

        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            Started.TrySetResult();

            // Deliberately ignores the token.
            await Gate.Task;

            try
            {
                Nested.TrySetResult(
                    await Execution!.QueueAsync(typeof(IOrphanNestedTrain).FullName!, "{}")
                );
            }
            catch (Exception ex)
            {
                Nested.TrySetException(ex);
            }
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }

    public record OrphanNestedInput
    {
        public string Label { get; init; } = "default";
    }

    public interface IOrphanNestedTrain : IServiceTrain<OrphanNestedInput, Unit>;

    public class OrphanNestedTrain : ServiceTrain<OrphanNestedInput, Unit>, IOrphanNestedTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());
    }
}
