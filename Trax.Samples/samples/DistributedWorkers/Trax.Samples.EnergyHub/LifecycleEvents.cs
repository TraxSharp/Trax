namespace Trax.Samples.EnergyHub;

/// <summary>
/// The RabbitMQ exchange the hub and the worker share for train events. Every process bound to an
/// exchange receives every event published on it, so another Trax application on the same broker
/// must not use this one, or the default <c>trax.lifecycle</c>: its runs would reach this sample's
/// operators. Both processes read the name from here so they cannot drift apart.
/// </summary>
public static class LifecycleEvents
{
    public const string ExchangeName = "energyhub.lifecycle";
}
