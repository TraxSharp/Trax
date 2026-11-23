using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>A-30: an <c>Operator</c> event into a train that needs <c>Operator</c> and <c>Auditor</c>.</summary>
public sealed class OneRoleSetMachine : RuleStageMachine<IAuditedTrain, AuditedInput>
{
    public const string MachineId = "rules-one-role-set";
    protected override string Id => MachineId;

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.Requires("Operator");

    protected override AuditedInput Input(string source) => new(source);
}
