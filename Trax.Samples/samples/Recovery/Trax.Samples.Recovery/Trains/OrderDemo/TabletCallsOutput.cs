using Trax.Samples.Recovery.Orders;

namespace Trax.Samples.Recovery.Trains.OrderDemo;

public record TabletCallsOutput
{
    /// <summary>The restaurant the tablet belongs to.</summary>
    public required string Restaurant { get; init; }

    /// <summary>The last calls to the restaurant's tablet, oldest first.</summary>
    public required IReadOnlyList<TabletCall> Calls { get; init; }
}
