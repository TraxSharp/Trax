using System.Collections.Concurrent;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.StateMachine;

/// <summary>A charge the payment provider took.</summary>
public record ProviderCharge
{
    public required string Receipt { get; init; }
    public required long AmountCents { get; init; }
    public required int Items { get; init; }
    public required DateTime ChargedAt { get; init; }
}
