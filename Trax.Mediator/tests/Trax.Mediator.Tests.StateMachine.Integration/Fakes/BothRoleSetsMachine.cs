using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>
/// A-30: an event that requires <c>Operator</c> and, separately, <c>Auditor</c>, into a train that needs both.
/// </summary>
public sealed class BothRoleSetsMachine : RuleStageMachine<IAuditedTrain, AuditedInput>
{
    public const string MachineId = "rules-both-role-sets";
    protected override string Id => MachineId;

    protected override ITransitionBuilder<RuleState, RuleTrigger> BuildRule(
        ITransitionBuilder<RuleState, RuleTrigger> build
    ) => build.Requires("Operator").Requires("Auditor");

    protected override AuditedInput Input(string source) => new(source);
}
