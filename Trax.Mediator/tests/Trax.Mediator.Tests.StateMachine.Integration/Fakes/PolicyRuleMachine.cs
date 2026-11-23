using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>A-29: an event gated by a policy, into a train gated by a role.</summary>
public sealed class PolicyRuleMachine : RuleStageMachine<IOperatorTrain, OperatorInput>
{
    public const string MachineId = "rules-policy-into-role";
    public const string Policy = "can-build";
    protected override string Id => MachineId;

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.RequiresPolicy(Policy);

    protected override OperatorInput Input(string source) => new(source);
}
