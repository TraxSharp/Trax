using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.CheckIn.Api.Auth;

namespace Trax.Samples.CheckIn.Trains;

/// <summary>What the booking's <c>Issuing</c> state hands the train: the passport number, from the booking's context.</summary>
public sealed record BoardingPassInput
{
    public required string Passport { get; init; }
}
