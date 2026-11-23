using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>
/// A-27, shaped like the topic map: <c>Build</c> requires <c>Operator</c> and <c>Rebuild</c> any authenticated
/// caller, and both enter the state invoking an <c>Operator</c> train.
/// </summary>
public sealed class EveryEnteringEventMachine : RuleStageMachine<IOperatorTrain, OperatorInput>
{
    public const string MachineId = "rules-every-entering-event";
    protected override string Id => MachineId;

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.Requires("Operator");

    protected override Func<
        ITransitionBuilder<RuleState, RuleTrigger>,
        ITransitionBuilder<RuleState, RuleTrigger>
    >? RebuildRule => rebuild => rebuild.AnyAuthenticated();

    protected override OperatorInput Input(string source) => new(source);
}
