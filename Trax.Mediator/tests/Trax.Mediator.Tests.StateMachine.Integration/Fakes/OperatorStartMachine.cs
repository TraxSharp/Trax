using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>A-31: a start an <c>Operator</c> may make, into an initial state invoking an <c>Operator</c> train.</summary>
public sealed class OperatorStartMachine : RuleStageMachine<IOperatorTrain, OperatorInput>
{
    public const string MachineId = "rules-start-by-operator";
    protected override string Id => MachineId;

    protected override Action<IStartBuilder<RuleStartInput>>? StartRule =>
        s => s.Requires("Operator");

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.Requires("Operator");

    protected override OperatorInput Input(string source) => new(source);
}
