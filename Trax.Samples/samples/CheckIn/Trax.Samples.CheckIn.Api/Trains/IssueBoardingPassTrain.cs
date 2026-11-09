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

/// <summary>What the train answers: whether the passport was accepted. The booking's outcomes read <c>accepted</c>.</summary>
public sealed record BoardingPassOutput(bool Accepted);

public interface IIssueBoardingPassTrain : IServiceTrain<BoardingPassInput, BoardingPassOutput>;

/// <summary>
/// Issues a boarding pass, a stand-in for the passport office: a passport number ending in 0 has expired and is turned
/// down, any other is accepted. The booking's <c>Issuing</c> state invokes it for the passenger who checked in, so it
/// asks for the passenger role, as <c>CheckIn</c> does: the host compares the two at startup.
/// </summary>
[TraxAuthorize(Roles = CheckInRoles.Passenger)]
public class IssueBoardingPassTrain
    : ServiceTrain<BoardingPassInput, BoardingPassOutput>,
        IIssueBoardingPassTrain
{
    protected override Task<Either<Exception, BoardingPassOutput>> Junctions() =>
        Chain<CheckPassport>().Resolve();
}

/// <summary>Accepts the passport, or turns it down when its number ends in 0, which the demo reads as expired.</summary>
public class CheckPassport : EffectJunction<BoardingPassInput, BoardingPassOutput>
{
    public static bool Expired(string passport) => passport.EndsWith('0');

    public override Task<BoardingPassOutput> Run(BoardingPassInput input) =>
        Task.FromResult(new BoardingPassOutput(!Expired(input.Passport)));
}
