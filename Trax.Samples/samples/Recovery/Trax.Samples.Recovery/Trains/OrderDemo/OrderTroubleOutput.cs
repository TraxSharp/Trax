using Trax.Samples.Recovery.Orders;

namespace Trax.Samples.Recovery.Trains.OrderDemo;

public record OrderTroubleOutput
{
    public required OrderTrouble Trouble { get; init; }
}
