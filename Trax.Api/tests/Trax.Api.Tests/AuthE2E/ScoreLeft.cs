using System.Globalization;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>Fails when the run is asked to, so its branch fails the <c>Parallel</c> step.</summary>
public class ScoreLeft : EffectJunction<MarkerInput, LeftScore>
{
    public override Task<LeftScore> Run(MarkerInput input) =>
        input.Fail
            ? throw new InvalidOperationException("the left branch failed")
            : Task.FromResult(new LeftScore(1));
}
