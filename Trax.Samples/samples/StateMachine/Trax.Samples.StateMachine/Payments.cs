using System.Collections.Concurrent;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Samples.StateMachine;

/// <summary>A charge the payment provider took.</summary>
public record ProviderCharge
{
    public required string Receipt { get; init; }
    public required long AmountCents { get; init; }
    public required int Items { get; init; }
    public required DateTime ChargedAt { get; init; }
}

/// <summary>
/// A stand-in for a real payment gateway, in memory and for the demo only. The checkout's
/// <see cref="ICharge"/> calls it when the machine takes <c>Review → Paid</c>, and it keeps every charge it
/// takes per customer, so the page can show what the server actually charged. The page itself never calls
/// it: there is no charge endpoint.
/// </summary>
public sealed class SimulatedPaymentProvider
{
    private readonly ConcurrentDictionary<string, List<ProviderCharge>> _charges = new();

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

public record ListChargesInput;

public record ListChargesOutput
{
    public required IReadOnlyList<ProviderCharge> Charges { get; init; }
}

public interface IListCharges : IServiceTrain<ListChargesInput, ListChargesOutput> { }

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
