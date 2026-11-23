using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class SecretMachine : StageMachine<ISecretTrain, SecretInput, SecretOutput>
{
    protected override string Id => "secret-stage";

    protected override SecretInput Input(string source) => new(source);
}
