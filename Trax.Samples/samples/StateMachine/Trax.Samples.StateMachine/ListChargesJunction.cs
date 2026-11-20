using System.Collections.Concurrent;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.StateMachine;

// Keyed by the same user key the charge records under, which the host maps from its own auth.
public class ListChargesJunction(SimulatedPaymentProvider provider, ISnapshotPrincipal principal)
    : Junction<ListChargesInput, ListChargesOutput>
{
    public override Task<ListChargesOutput> Run(ListChargesInput input) =>
        Task.FromResult(
            new ListChargesOutput
            {
                Charges = principal.CurrentUserKey is { } customer
                    ? provider.ChargesFor(customer)
                    : [],
            }
        );
}
