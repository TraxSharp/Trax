using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>
/// One stage: <c>Idle</c> --Go--> <c>Running</c>, which invokes <typeparamref name="TTrain"/> with the context's
/// <c>source</c>; <c>Stop</c> leaves it, and its outcome goes to <c>Done</c>, <c>Failed</c> or <c>Cancelled</c>,
/// from which <c>Retry</c> enters <c>Running</c> again. A system-owned stage starts in <c>Running</c>.
/// </summary>
public abstract class StageMachine<TTrain, TInput, TOutput> : Machine<StepState, StepTrigger>
    where TTrain : IServiceTrain<TInput, TOutput>
{
    protected abstract string Id { get; }

    protected virtual bool System => false;

    protected virtual int? Limit => null;

    protected abstract TInput Input(string source);

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(
                System ? StepState.Running : StepState.Idle,
                () => new JsonObject { ["source"] = "repo" }
            );
        if (System)
            m.SystemOwned();
        if (Limit is { } limit)
            m.InvokedRunLimit(limit);

        m.In(StepState.Idle).On(StepTrigger.Go).To(StepState.Running);

        m.In(StepState.Running)
            .Invokes<TTrain, TInput, TOutput>(ctx => Input(ctx["source"]!.GetValue<string>()))
            .OnDone(StepState.Done)
            .OnFailed(StepState.Failed)
            .OnCancelled(StepState.Cancelled)
            .On(StepTrigger.Stop)
            .To(StepState.Idle);

        m.In(StepState.Failed).On(StepTrigger.Retry).To(StepState.Running);
        m.In(StepState.Cancelled).On(StepTrigger.Retry).To(StepState.Running);
    }

    /// <summary>A snapshot of the machine named <paramref name="id"/> as a client sends it.</summary>
    public static string Json(string id, string state, string source = "repo") =>
        new JsonObject
        {
            ["machine"] = id,
            ["version"] = 1,
            ["state"] = state,
            ["context"] = new JsonObject { ["source"] = source },
        }.ToJsonString();
}
