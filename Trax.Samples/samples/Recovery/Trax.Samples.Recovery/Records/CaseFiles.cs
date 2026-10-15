using System.Collections.Concurrent;

namespace Trax.Samples.Recovery.Records;

/// <summary>
/// The data a refund run reads besides its input: the order.
/// Stands in for a database. The "change the data during the backoff" control edits it, so the retry
/// reads a different state from the one the first attempt's decisions were made about.
/// </summary>
public sealed class CaseFiles
{
    private readonly ConcurrentDictionary<string, Order> _orders = new();

    // Who opened each case: only they may change it.
    private readonly ConcurrentDictionary<string, string?> _openers = new();

    /// <summary>The orders a refund run can be started for.</summary>
    public static readonly IReadOnlyDictionary<string, Order> Catalog = new Dictionary<
        string,
        Order
    >
    {
        ["A-1001"] = new("A-1001", 89.00m, "Arrived broken", 0, "dana@example.com"),
        ["A-1002"] = new("A-1002", 420.00m, "Never arrived", 0, "lee@example.com"),
        ["A-1003"] = new("A-1003", 35.50m, "Changed my mind", 2, "sam@example.com"),
    };

    /// <summary>Opens the case file of a new run, copying the order from the catalog.</summary>
    public void OpenRefund(string runId, string orderId, string? openedBy = null)
    {
        if (!Catalog.TryGetValue(orderId, out var order))
            throw new ArgumentException($"There is no order {orderId}.", nameof(orderId));
        _orders[runId] = order;
        _openers[runId] = openedBy;
    }

    public Order OrderFor(string runId) =>
        _orders.TryGetValue(runId, out var order)
            ? order
            : throw new InvalidOperationException($"Run {runId} has no order on file.");

    /// <summary>
    /// Changes what the run will read next: the customer files another refund. Returns a sentence
    /// saying what changed.
    /// </summary>
    public string Change(string runId, string? changedBy = null)
    {
        // Another caller's case answers as one that does not exist. A case nobody opened is nobody's, so a caller
        // without an id changes nothing.
        if (
            _orders.TryGetValue(runId, out var order)
            && _openers.TryGetValue(runId, out var opener)
            && opener is not null
            && string.Equals(opener, changedBy, StringComparison.Ordinal)
        )
        {
            _orders[runId] = order with { PriorRefunds = order.PriorRefunds + 1 };
            return $"Order {order.OrderId} now shows {order.PriorRefunds + 1} earlier refund(s).";
        }

        throw new InvalidOperationException($"Run {runId} has no case file.");
    }
}

/// <summary>An order as the order system holds it.</summary>
public sealed record Order(
    string OrderId,
    decimal Amount,
    string Reason,
    int PriorRefunds,
    string CustomerEmail
);
