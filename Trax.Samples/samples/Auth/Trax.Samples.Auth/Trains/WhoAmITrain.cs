using Trax.Api.Auth;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Auth.Trains;

/// <summary>
/// Any signed-in caller, whatever the scheme or role. A bare <c>[TraxAuthorize]</c> requires an
/// authenticated caller and nothing more.
/// </summary>
[TraxQuery]
[TraxAuthorize]
public class WhoAmITrain : ServiceTrain<WhoAmIInput, WhoAmIOutput>, IWhoAmITrain
{
    protected override Task<Either<Exception, WhoAmIOutput>> Junctions() =>
        Chain<DescribeCallerJunction>().Resolve();
}
