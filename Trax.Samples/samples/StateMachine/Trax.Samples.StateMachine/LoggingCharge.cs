using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.StateMachine;

/// <summary>
/// The charge: it takes the payment from the <see cref="SimulatedPaymentProvider"/> for
/// <see cref="CheckoutMachine.AmountCents"/> of the snapshot it is handed, which is the server's stored copy,
/// whose total the machine has already checked against the items. A real host swaps in a payment gateway.
/// The machine runs it on <c>Pay</c>, exactly once; nothing else calls it.
/// </summary>
public sealed class LoggingCharge(
    SimulatedPaymentProvider provider,
    ISnapshotPrincipal principal,
    ILogger<LoggingCharge> logger
) : ICharge
{
    public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default)
    {
        // A send is refused before its effect runs when the request carries no user, so there is one here.
        var customer =
            principal.CurrentUserKey
            ?? throw new InvalidOperationException("A charge needs the customer it is for.");
        var items = snapshot.Context["items"] is JsonArray a ? a.Count : 0;
        var charge = provider.Charge(customer, CheckoutMachine.AmountCents(snapshot), items);
        logger.LogInformation(
            "Charged {AmountCents} cents for checkout {State} -> receipt {Receipt}",
            charge.AmountCents,
            snapshot.State,
            charge.Receipt
        );
        return Task.FromResult(charge.Receipt);
    }
}
