using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.GetProfile.Junctions;

namespace Trax.Samples.JobHunt.Trains.GetProfile;

[TraxQuery(Description = "Returns the user's profile or a default empty profile")]
public class GetProfileTrain : ServiceTrain<GetProfileInput, GetProfileOutput>, IGetProfileTrain
{
    protected override GetProfileOutput Junctions() => Chain<LoadProfileJunction>();
}
