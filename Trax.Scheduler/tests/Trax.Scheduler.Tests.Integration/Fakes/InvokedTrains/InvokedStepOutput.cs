using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>A pointer, never data, except where a test needs a large one.</summary>
public sealed record InvokedStepOutput
{
    public string Artifact { get; init; } = "";

    public bool Accepted { get; init; }

    public bool Chain { get; init; }
}
