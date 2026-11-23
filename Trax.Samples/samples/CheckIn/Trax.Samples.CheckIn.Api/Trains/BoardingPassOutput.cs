using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.CheckIn.Api.Auth;

namespace Trax.Samples.CheckIn.Trains;

/// <summary>What the train answers: whether the passport was accepted. The booking's outcomes read <c>accepted</c>.</summary>
public sealed record BoardingPassOutput(bool Accepted);
