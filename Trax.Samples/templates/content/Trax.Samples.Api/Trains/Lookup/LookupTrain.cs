using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Api.Trains.Lookup.Junctions;

namespace Trax.Samples.Api.Trains.Lookup;

/// <summary>
/// A query train that looks up a record by ID.
/// Exposed as a typed query field under query { discover { lookup(...) } }.
/// Callers need the User role, which the demo key carries in Development (see Program.cs).
/// </summary>
[TraxAuthorize(Roles = "User")]
[TraxQuery(Description = "Looks up a record by ID")]
public class LookupTrain : ServiceTrain<LookupInput, LookupOutput>, ILookupTrain
{
    protected override Task<Either<Exception, LookupOutput>> Junctions() =>
        Chain<FetchDataJunction>().Resolve();
}
