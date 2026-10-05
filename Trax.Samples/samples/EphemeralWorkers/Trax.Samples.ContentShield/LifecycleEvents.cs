namespace Trax.Samples.ContentShield;

/// <summary>
/// The RabbitMQ exchange the API and the runner share for train events. Every process bound to an
/// exchange receives every event published on it, so another Trax application on the same broker
/// must not use this one, or the default <c>trax.lifecycle</c>: its runs would reach this sample's
/// subscribers. Both processes read the name from here so they cannot drift apart.
/// </summary>
public static class LifecycleEvents
{
    public const string ExchangeName = "contentshield.lifecycle";
}
