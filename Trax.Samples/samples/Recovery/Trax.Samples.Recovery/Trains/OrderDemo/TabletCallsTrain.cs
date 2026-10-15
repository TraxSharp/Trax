using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.StateMachine.Persistence;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Orders;

namespace Trax.Samples.Recovery.Trains.OrderDemo;

public interface ITabletCallsTrain : IServiceTrain<TabletCallsInput, TabletCallsOutput>;

/// <summary>
/// The calls the restaurant's tablet received for the caller's own orders: the inside of the order's
/// <c>SendToRestaurant</c> step, so a client can show the attempt that timed out and the one that was answered.
/// </summary>
[TraxAuthorize(Roles = RecoveryRoles.User)]
[TraxQuery(Namespace = "orders", Description = "Lists the calls the restaurant's tablet received.")]
public class TabletCallsTrain : ServiceTrain<TabletCallsInput, TabletCallsOutput>, ITabletCallsTrain
{
    protected override Task<Either<Exception, TabletCallsOutput>> Junctions() =>
        Chain<ReadTabletCalls>().Resolve();
}

public class ReadTabletCalls(Restaurant restaurant, ISnapshotPrincipal caller)
    : Junction<TabletCallsInput, TabletCallsOutput>
{
    public override Task<TabletCallsOutput> Run(TabletCallsInput input) =>
        Task.FromResult(
            new TabletCallsOutput
            {
                Restaurant = Restaurant.Name,
                Calls = restaurant.CallsOf(caller.CurrentUserKey),
            }
        );
}
