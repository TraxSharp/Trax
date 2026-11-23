using System.Globalization;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

public class CountX : EffectJunction<MarkerInput, XCount>
{
    public override Task<XCount> Run(MarkerInput input) => Task.FromResult(new XCount(3));
}
