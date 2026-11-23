using System.Globalization;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

public class CountY : EffectJunction<MarkerInput, YCount>
{
    public override Task<YCount> Run(MarkerInput input) => Task.FromResult(new YCount(4));
}
