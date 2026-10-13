using System.Collections.Concurrent;
using Trax.Samples.Recovery.Faults;

namespace Trax.Samples.Recovery.Orders;

/// <summary>
/// The pizzeria the <c>order</c> machine orders from, simulated: its tablet takes an order after a short pause. The
/// demo can arm trouble for the next order (<see cref="FaultInjector.ArmNextOrder"/>): a tablet that does not answer
/// once, or a restaurant that is closed. Every call to the tablet is kept, so a client can see an attempt that timed
/// out and the one that was answered.
/// </summary>
public sealed class Restaurant(FaultInjector faults, DemoPace pace)
{
    /// <summary>The restaurant's name.</summary>
    public const string Name = "Pizzeria Napoli";

    private const int Kept = 200;
    private readonly ConcurrentQueue<TabletCall> _calls = new();

    /// <summary>The last calls to the tablet, oldest first.</summary>
    public IReadOnlyList<TabletCall> Calls => [.. _calls];

    /// <summary>Sends the order to the restaurant's tablet, once, and waits for it to answer.</summary>
    /// <exception cref="TimeoutException">The tablet did not answer within <see cref="DemoPace.TabletTimeout"/>.</exception>
    /// <exception cref="InvalidOperationException">The restaurant is closed.</exception>
    public async Task SendAsync(string orderId, int attempt, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            if (faults.TryFire(orderId, CrashPoint.TabletTimesOut))
            {
                await Task.Delay(pace.TabletTimeout, cancellationToken);
                throw new TimeoutException(
                    $"{Name}'s tablet did not answer in {pace.TabletTimeout.TotalSeconds:0.#} s."
                );
            }

            await Task.Delay(pace.StepDelay, cancellationToken);
            if (faults.TryFire(orderId, CrashPoint.RestaurantClosed))
                throw new InvalidOperationException($"{Name} is closed.");

            Keep(new TabletCall(orderId, attempt, startedAt, DateTimeOffset.UtcNow, null));
        }
        catch (Exception e) when (e is TimeoutException or InvalidOperationException)
        {
            Keep(new TabletCall(orderId, attempt, startedAt, DateTimeOffset.UtcNow, e.Message));
            throw;
        }
    }

    private void Keep(TabletCall call)
    {
        _calls.Enqueue(call);
        while (_calls.Count > Kept)
            _calls.TryDequeue(out _);
    }
}

/// <summary>One call to the restaurant's tablet: answered when <see cref="Problem"/> is null.</summary>
public sealed record TabletCall(
    string OrderId,
    int Attempt,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string? Problem
);
