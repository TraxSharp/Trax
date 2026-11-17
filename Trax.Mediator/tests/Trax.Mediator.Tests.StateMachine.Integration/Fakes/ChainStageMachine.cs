using System.Text.Json.Nodes;
using Trax.Core.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>
/// Two stages: <c>Fetching</c> and <c>Embedding</c> each invoke <see cref="IGoodTrain"/>. When
/// <see cref="ChainedByOutcome"/>, <c>Fetching</c>'s <c>OnDone</c> enters <c>Embedding</c> directly, so the outcome
/// queues the next run; otherwise it enters <c>Fetched</c>, and the user's <c>Continue</c> enters <c>Embedding</c>.
/// </summary>
public abstract class ChainStageMachine : Machine<ChainState, ChainTrigger>
{
    protected abstract string Id { get; }

    protected virtual bool System => false;

    protected virtual bool ChainedByOutcome => true;

    protected override void Configure(IMachineBuilder<ChainState, ChainTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(
                System ? ChainState.Fetching : ChainState.Idle,
                () => new JsonObject { ["source"] = "repo" }
            );
        if (System)
            m.SystemOwned();

        m.In(ChainState.Idle).On(ChainTrigger.Go).To(ChainState.Fetching);

        m.In(ChainState.Fetching)
            .Invokes<IGoodTrain, GoodInput, JobOutput>(ctx =>
                new(ctx["source"].NotNull().GetValue<string>())
            )
            .OnDone(ChainedByOutcome ? ChainState.Embedding : ChainState.Fetched)
            .OnFailed(ChainState.Failed)
            .OnCancelled(ChainState.Cancelled);

        m.In(ChainState.Fetched).On(ChainTrigger.Continue).To(ChainState.Embedding);

        m.In(ChainState.Embedding)
            .Invokes<IGoodTrain, GoodInput, JobOutput>(ctx =>
                new(ctx["source"].NotNull().GetValue<string>())
            )
            .OnDone(ChainState.Done)
            .OnFailed(ChainState.Failed)
            .OnCancelled(ChainState.Cancelled);

        m.In(ChainState.Failed).On(ChainTrigger.Retry).To(ChainState.Fetching);
    }
}
