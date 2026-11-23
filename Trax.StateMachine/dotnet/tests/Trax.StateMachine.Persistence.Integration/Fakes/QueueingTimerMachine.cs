using System.Text.Json.Nodes;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>A system machine whose timer enters an invoking state, so firing it queues a run.</summary>
public sealed class QueueingTimerMachine : Machine<QueueingTimerState, QueueingTimerTrigger>
{
    public const string Id = "queueing-timer";

    protected override void Configure(IMachineBuilder<QueueingTimerState, QueueingTimerTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(QueueingTimerState.Idle, () => new JsonObject { ["source"] = "repo" });
        m.In(QueueingTimerState.Idle)
            .Context<BuildMachine.SourceContext>()
            .On(QueueingTimerTrigger.Open)
            .To(QueueingTimerState.Waiting);
        m.In(QueueingTimerState.Waiting)
            .Context<BuildMachine.SourceContext>()
            .After(TimeSpan.FromMinutes(1))
            .GoTo(QueueingTimerState.Building);
        m.In(QueueingTimerState.Building)
            .Context<BuildMachine.SourceContext>()
            .Invokes<IBuildTrain, BuildInput, BuildOutput>(ctx => new BuildInput(
                ctx["source"].NotNull().GetValue<string>()
            ))
            .OnDone(QueueingTimerState.Built)
            .OnFailed(QueueingTimerState.Failed)
            .OnCancelled(QueueingTimerState.Cancelled);
        m.In(QueueingTimerState.Built).Context<BuildMachine.SourceContext>();
        m.In(QueueingTimerState.Failed).Context<BuildMachine.SourceContext>();
        m.In(QueueingTimerState.Cancelled).Context<BuildMachine.SourceContext>();
        m.SystemOwned();
    }
}
