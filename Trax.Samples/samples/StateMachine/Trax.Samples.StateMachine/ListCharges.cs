using System.Collections.Concurrent;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.StateMachine;

/// <summary>The caller's charges at the payment provider: the effect's side of the story.</summary>
[TraxAuthorize]
[TraxQuery(
    Namespace = "payments",
    Description = "List the charges the payment provider took from the caller."
)]
public class ListCharges : ServiceTrain<ListChargesInput, ListChargesOutput>, IListCharges
{
    protected override Task<Either<Exception, ListChargesOutput>> Junctions() =>
        Chain<ListChargesJunction>().Resolve();
}
