using Trax.Samples.Recovery.Orders;

namespace Trax.Samples.Recovery.Trains.OrderDemo;

/// <summary>What goes wrong with the next food order.</summary>
public enum OrderTrouble
{
    /// <summary>Nothing: the pizza arrives.</summary>
    None,

    /// <summary>The restaurant's tablet does not answer in time, once; it is tried again.</summary>
    TabletTimesOut,

    /// <summary>The restaurant is closed: the order is cancelled before anything is charged.</summary>
    RestaurantClosed,
}
