using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>A user's machine that chains its stages through the user's <c>Continue</c>.</summary>
public sealed class UserContinueMachine : ChainStageMachine
{
    public const string MachineId = "user-continue-stage";
    protected override string Id => MachineId;
    protected override bool ChainedByOutcome => false;
}
