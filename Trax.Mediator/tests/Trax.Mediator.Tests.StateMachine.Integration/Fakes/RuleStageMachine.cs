using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>
/// A user's machine: <c>Idle</c> --Build--> <c>Running</c>, which invokes <typeparamref name="TTrain"/>; with
/// <see cref="RebuildRule"/>, <c>Rebuild</c> enters <c>Running</c> too. With <see cref="StartRule"/>, the machine
/// starts in <c>Running</c> through its declared <c>start</c>, so the start enters the invoking state.
/// </summary>
public abstract class RuleStageMachine<TTrain, TInput> : Machine<RuleState, RuleTrigger>
    where TTrain : IServiceTrain<TInput, JobOutput>
{
    protected abstract string Id { get; }

    /// <summary>The rule of the <c>Build</c> event.</summary>
    protected abstract ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    );

    /// <summary>The rule of the <c>Rebuild</c> event, or <c>null</c> when the machine has none.</summary>
    protected virtual Func<
        ITransitionBuilder<RuleState, RuleTrigger>,
        ITransitionBuilder<RuleState, RuleTrigger>
    >? RebuildRule => null;

    /// <summary>The rule of the machine's <c>start</c>, or <c>null</c> when it declares none.</summary>
    protected virtual Action<IStartBuilder<RuleStartInput>>? StartRule => null;

    protected abstract TInput Input(string source);

    protected override void Configure(IMachineBuilder<RuleState, RuleTrigger> m)
    {
        var start = StartRule;
        m.Id(Id)
            .Version(1)
            .StartsAt(
                start is null ? RuleState.Idle : RuleState.Running,
                () => new JsonObject { ["source"] = "repo" }
            );
        if (start is not null)
            m.Start(start);

        var idle = m.In(RuleState.Idle);
        BuildRule(idle.On(RuleTrigger.Build)).To(RuleState.Running);
        if (RebuildRule is { } rebuild)
            rebuild(idle.On(RuleTrigger.Rebuild)).To(RuleState.Running);

        m.In(RuleState.Running)
            .Invokes<TTrain, TInput, JobOutput>(ctx => Input(ctx["source"]!.GetValue<string>()))
            .OnDone(RuleState.Done)
            .OnFailed(RuleState.Failed)
            .OnCancelled(RuleState.Cancelled)
            .On(RuleTrigger.Stop)
            .AnyAuthenticated()
            .To(RuleState.Idle);
    }
}
