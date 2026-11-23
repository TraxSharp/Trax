using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.CheckIn.Api.Auth;

namespace Trax.Samples.CheckIn.Trains;

/// <summary>Accepts the passport, or turns it down when its number ends in 0, which the demo reads as expired.</summary>
public class CheckPassport : EffectJunction<BoardingPassInput, BoardingPassOutput>
{
    public static bool Expired(string passport) => passport.EndsWith('0');

    public override Task<BoardingPassOutput> Run(BoardingPassInput input) =>
        Task.FromResult(new BoardingPassOutput(!Expired(input.Passport)));
}
