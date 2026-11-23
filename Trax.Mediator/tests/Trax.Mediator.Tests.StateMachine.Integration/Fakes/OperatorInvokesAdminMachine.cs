using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>A-26: an <c>Operator</c> event entering a state that invokes an <c>admin</c> train.</summary>
public sealed class OperatorInvokesAdminMachine : RuleStageMachine<IAdminTrain, AdminInput>
{
    public const string MachineId = "rules-operator-into-admin";
    protected override string Id => MachineId;

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.Requires("Operator");

    protected override AdminInput Input(string source) => new(source);
}
