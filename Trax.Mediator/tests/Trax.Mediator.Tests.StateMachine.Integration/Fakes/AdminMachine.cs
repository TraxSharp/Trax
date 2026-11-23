using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class AdminMachine : StageMachine<IAdminTrain, AdminInput, JobOutput>
{
    protected override string Id => "admin-stage";

    protected override AdminInput Input(string source) => new(source);
}
