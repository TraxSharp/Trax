using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>A-25: an <c>Operator</c> event entering a state that invokes an <c>Operator</c> train.</summary>
public sealed class OperatorInvokesOperatorMachine : RuleStageMachine<IOperatorTrain, OperatorInput>
{
    public const string MachineId = "rules-operator-into-operator";
    protected override string Id => MachineId;

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.Requires("Operator");

    protected override OperatorInput Input(string source) => new(source);
}
