using System.Globalization;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

public class ScoreRight : EffectJunction<MarkerInput, RightScore>
{
    public override Task<RightScore> Run(MarkerInput input) => Task.FromResult(new RightScore(2));
}
