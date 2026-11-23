using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>An output that carries a sensitive value.</summary>
public sealed record SecretOutput
{
    public string Artifact { get; init; } = "";

    [TraxSensitive]
    public string ApiKey { get; init; } = "";
}
