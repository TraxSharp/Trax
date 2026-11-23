using System.Globalization;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

public class JoinScores : EffectJunction<(LeftScore, RightScore), string>
{
    public override Task<string> Run((LeftScore, RightScore) input) =>
        Task.FromResult(
            string.Create(CultureInfo.InvariantCulture, $"{input.Item1.Value}+{input.Item2.Value}")
        );
}
