using System.Collections.Concurrent;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.StateMachine;

/// <summary>
/// A stand-in for a real payment gateway, in memory and for the demo only. The checkout's
/// <see cref="ICharge"/> calls it when the machine takes <c>Review → Paid</c>, and it keeps every charge it
/// takes per customer, so the page can show what the server actually charged. The page itself never calls
/// it: there is no charge endpoint.
/// </summary>
public sealed class SimulatedPaymentProvider
{
    private readonly ConcurrentDictionary<string, List<ProviderCharge>> _charges = new(
        StringComparer.Ordinal
    );

    public ProviderCharge Charge(string customer, long amountCents, int items)
    {
        var charge = new ProviderCharge
        {
            Receipt = $"rcpt_{Guid.NewGuid():N}",
            AmountCents = amountCents,
            Items = items,
            ChargedAt = DateTime.UtcNow,
        };
        var list = _charges.GetOrAdd(customer, _ => []);
        lock (list)
            list.Add(charge);
        return charge;
    }

    /// <summary>The customer's charges, newest first.</summary>
    public IReadOnlyList<ProviderCharge> ChargesFor(string customer)
    {
        if (!_charges.TryGetValue(customer, out var list))
            return [];
        lock (list)
            return Enumerable.Reverse(list).ToList();
    }
}
