using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.JobHunt.Trains.UpdateProfile.Junctions;

namespace Trax.Samples.JobHunt.Trains.UpdateProfile;

[TraxMutation(Description = "Updates a single facet of the user's profile")]
public class UpdateProfileTrain
    : ServiceTrain<UpdateProfileInput, UpdateProfileOutput>,
        IUpdateProfileTrain
{
    protected override UpdateProfileOutput Junctions() =>
        Chain<ValidateProfileJsonJunction>().Chain<PersistProfileJunction>();
}
