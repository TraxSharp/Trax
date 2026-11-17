using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>A-31: a start any authenticated caller may make, into the same state.</summary>
public sealed class AnyoneStartMachine : RuleStageMachine<IOperatorTrain, OperatorInput>
{
    public const string MachineId = "rules-start-by-anyone";
    protected override string Id => MachineId;

    protected override Action<IStartBuilder<RuleStartInput>>? StartRule =>
        s => s.AnyAuthenticated();

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.Requires("Operator");

    protected override OperatorInput Input(string source) => new(source);
}
