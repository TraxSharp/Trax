using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Orders;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.Recovery.Trains.OrderDemo;

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
