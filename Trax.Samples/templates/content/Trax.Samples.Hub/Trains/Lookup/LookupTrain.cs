using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Hub.Trains.Lookup.Junctions;

namespace Trax.Samples.Hub.Trains.Lookup;

/// <summary>
/// A query train that looks up a record by ID.
/// Exposed as a typed query field under query { discover { lookup(...) } }.
/// </summary>
[TraxQuery(Description = "Looks up a record by ID")]
public class LookupTrain : ServiceTrain<LookupInput, LookupOutput>, ILookupTrain
{
    protected override async Task<Either<Exception, LookupOutput>> RunInternal(LookupInput input) =>
        Activate(input).Chain<FetchDataJunction>().Resolve();
}
