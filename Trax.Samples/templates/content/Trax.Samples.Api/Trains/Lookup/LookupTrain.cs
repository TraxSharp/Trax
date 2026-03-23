using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Api.Trains.Lookup.Junctions;

namespace Trax.Samples.Api.Trains.Lookup;

/// <summary>
/// A query train that looks up a record by ID.
/// Exposed as a typed query field under query { discover { lookup(...) } }.
/// </summary>
[TraxQuery(Description = "Looks up a record by ID")]
public class LookupTrain : ServiceTrain<LookupInput, LookupOutput>, ILookupTrain
{
    protected override LookupOutput Junctions() => Chain<FetchDataJunction>();
}
