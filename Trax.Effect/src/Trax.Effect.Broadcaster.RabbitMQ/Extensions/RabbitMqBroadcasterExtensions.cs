using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.BroadcasterBuilder;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.RabbitMQ.Extensions;

/// <summary>
/// Adds <c>UseRabbitMq</c> to the broadcaster builder, which makes RabbitMQ the transport that
/// carries train lifecycle events between processes.
/// </summary>
public static class RabbitMqBroadcasterExtensions
{
    /// <summary>
    /// Configures RabbitMQ as the transport for cross-process lifecycle event broadcasting.
    /// </summary>
    /// <param name="builder">The broadcaster builder.</param>
    /// <param name="connectionString">AMQP connection URI (e.g., "amqp://guest:guest@localhost:5672").</param>
    /// <param name="configure">Optional callback to customize RabbitMQ options.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// Registers the options, a <see cref="RabbitMqTrainEventBroadcaster"/> as the singleton
    /// <see cref="ITrainEventBroadcaster"/>, and a <see cref="RabbitMqTrainEventReceiver"/> as the
    /// singleton <see cref="ITrainEventReceiver"/>. No connection is opened here: the broadcaster
    /// connects on its first publish and the receiver when it is started.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="configure"/> set <see cref="RabbitMqBroadcasterOptions.PrefetchCount"/> to 0,
    /// which RabbitMQ reads as "no limit".
    /// </exception>
    public static BroadcasterBuilder UseRabbitMq(
        this BroadcasterBuilder builder,
        string connectionString,
        Action<RabbitMqBroadcasterOptions>? configure = null
    )
    {
        var options = new RabbitMqBroadcasterOptions { ConnectionString = connectionString };
        configure?.Invoke(options);

        if (options.PrefetchCount == 0)
        {
            throw new ArgumentException(
                "UseRabbitMq() requires RabbitMqBroadcasterOptions.PrefetchCount of at least 1. "
                    + "A prefetch count of 0 means no limit, which lets unacknowledged events pile up "
                    + "in the receiving process. Omit the setting to use the default of 64.",
                nameof(configure)
            );
        }

        builder.ServiceCollection.AddSingleton(options);
        builder
            .ServiceCollection.AddSingleton<RabbitMqTrainEventBroadcaster>()
            .AddSingleton<ITrainEventBroadcaster>(sp =>
                sp.GetRequiredService<RabbitMqTrainEventBroadcaster>()
            );
        builder
            .ServiceCollection.AddSingleton<RabbitMqTrainEventReceiver>()
            .AddSingleton<ITrainEventReceiver>(sp =>
                sp.GetRequiredService<RabbitMqTrainEventReceiver>()
            );

        return builder;
    }
}
