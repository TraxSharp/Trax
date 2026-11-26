using Microsoft.Extensions.Configuration;

namespace Trax.Samples.EnergyHub;

/// <summary>
/// Where the hub and the worker read their broker from, and whether they may reach it over plain
/// <c>amqp://</c>. <c>UseRabbitMq</c> refuses cleartext to a broker that is not on loopback. A
/// broker <c>trax deploy</c> runs beside the app is reached by name on the app's own network (a
/// namespace only the app's pods may enter, or a podman network only its pods join), over plain
/// AMQP, so the deployment says so with <see cref="AllowCleartextKey"/>. Anywhere else, use an
/// <c>amqps://</c> URI.
/// </summary>
public static class BrokerTransport
{
    /// <summary>
    /// The name the broker is read under: <c>ConnectionStrings:RabbitMQ</c> as a URI, or
    /// <c>RabbitMQ:Host</c>, <c>RabbitMQ:UserName</c> and <c>RabbitMQ:Password</c>, which is how a
    /// deployment hands it over.
    /// </summary>
    public const string Name = "RabbitMQ";

    /// <summary>Configuration key that allows plain AMQP to a broker that is not on loopback.</summary>
    public const string AllowCleartextKey = "Broadcast:AllowInsecureTransport";

    /// <summary>
    /// True only when <see cref="AllowCleartextKey"/> is the text <c>true</c>. Anything else, a
    /// missing value or one that is not a boolean, keeps cleartext refused rather than stopping the
    /// host on a parse error.
    /// </summary>
    public static bool AllowsCleartext(IConfiguration configuration) =>
        string.Equals(
            configuration[AllowCleartextKey]?.Trim(),
            "true",
            StringComparison.OrdinalIgnoreCase
        );
}
