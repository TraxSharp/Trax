using System.Collections.Concurrent;
using Trax.Effect.StateMachine;

namespace Trax.Samples.StateMachine.E2E.Utilities;

/// <summary>
/// The test's <see cref="ICharge"/>: it records every charge it is asked to make instead of only logging
/// it, so a test can count charges and see the amount. It reads the amount exactly as the sample's
/// <see cref="LoggingCharge"/> does, with <see cref="CheckoutMachine.AmountCents"/> on the snapshot the
/// effect runner hands it, which is the server's stored copy of the draft.
/// </summary>
public sealed class RecordingCharge : ICharge
{
    private readonly ConcurrentQueue<Charged> _charges = new();

    public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default)
    {
        var receipt = $"rcpt_{Guid.NewGuid():N}";
        _charges.Enqueue(
            new Charged(
                snapshot.State,
                CheckoutMachine.AmountCents(snapshot),
                Items(snapshot),
                receipt
            )
        );
        return Task.FromResult(receipt);
    }

    /// <summary>
    /// The charges made for drafts whose items include <paramref name="item"/>. Each test puts an item
    /// no other test uses in its cart, so it sees only its own charges.
    /// </summary>
    public IReadOnlyList<Charged> For(string item) =>
        _charges.Where(c => c.Items.Contains(item)).ToList();

    private static IReadOnlyList<string> Items(Snapshot snapshot) =>
        snapshot.Context["items"]!.AsArray().Select(i => i!.GetValue<string>()).ToList();
}

/// <summary>One charge: the state it was made from, the amount in cents, the items and the receipt.</summary>
public sealed record Charged(
    string State,
    long AmountCents,
    IReadOnlyList<string> Items,
    string Receipt
);
