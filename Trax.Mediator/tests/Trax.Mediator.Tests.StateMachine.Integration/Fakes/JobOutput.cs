using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

// Each train has an input type of its own: the mediator routes a run by its input's type.

/// <summary>What every invoked train here returns: a pointer, never data.</summary>
public sealed record JobOutput
{
    public string Artifact { get; init; } = "";
}
