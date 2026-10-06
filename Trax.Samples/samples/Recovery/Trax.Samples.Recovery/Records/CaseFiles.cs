using System.Collections.Concurrent;

namespace Trax.Samples.Recovery.Records;

/// <summary>
/// The data a run reads besides its input: the refund's order and the research brief's audience.
/// Stands in for a database. The "change the data during the backoff" control edits it, so the retry
/// reads a different state from the one the first attempt's decisions were made about.
/// </summary>
public sealed class CaseFiles
{
    private readonly ConcurrentDictionary<string, Order> _orders = new();
    private readonly ConcurrentDictionary<string, string> _audiences = new();

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
    public void OpenRefund(string runId, string orderId)
    {
        if (!Catalog.TryGetValue(orderId, out var order))
            throw new ArgumentException($"There is no order {orderId}.", nameof(orderId));
        _orders[runId] = order;
    }

    /// <summary>Opens the case file of a new research run.</summary>
    public void OpenResearch(string runId) => _audiences[runId] = "engineers";

    public Order OrderFor(string runId) =>
        _orders.TryGetValue(runId, out var order)
            ? order
            : throw new InvalidOperationException($"Run {runId} has no order on file.");

    public string AudienceFor(string runId) =>
        _audiences.TryGetValue(runId, out var audience)
            ? audience
            : throw new InvalidOperationException($"Run {runId} has no audience on file.");

    /// <summary>
    /// Changes what the run will read next: the customer files another refund, or the research is
    /// now for executives. Returns a sentence saying what changed.
    /// </summary>
    public string Change(string runId)
    {
        if (_orders.TryGetValue(runId, out var order))
        {
            _orders[runId] = order with { PriorRefunds = order.PriorRefunds + 1 };
            return $"Order {order.OrderId} now shows {order.PriorRefunds + 1} earlier refund(s).";
        }

        if (_audiences.ContainsKey(runId))
        {
            _audiences[runId] = "executives";
            return "The research is now for executives.";
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
