using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.CheckIn.Api.Auth;

namespace Trax.Samples.CheckIn.Trains;

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
