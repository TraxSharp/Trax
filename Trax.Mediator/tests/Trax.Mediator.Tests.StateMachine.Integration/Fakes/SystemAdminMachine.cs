using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>A system-owned machine invoking a train that needs a role.</summary>
public sealed class SystemAdminMachine : StageMachine<IAdminTrain, AdminInput, JobOutput>
{
    protected override string Id => "system-admin-stage";
    protected override bool System => true;

    protected override AdminInput Input(string source) => new(source);
}
