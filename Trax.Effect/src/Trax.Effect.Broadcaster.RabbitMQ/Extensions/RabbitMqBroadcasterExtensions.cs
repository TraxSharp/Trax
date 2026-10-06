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
    /// connects when it first has an event to send and the receiver when it is started. Publishing
    /// only queues the event, so an unreachable broker never holds up a train: the broadcaster's
    /// background sender retries, and drops events once 1024 are waiting.
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

        if (options.EffectiveJunctionExchangeName == options.ExchangeName)
        {
            throw new ArgumentException(
                "UseRabbitMq() requires RabbitMqBroadcasterOptions.JunctionExchangeName to differ "
                    + "from ExchangeName, so a receiver that predates junction events never receives "
                    + "one. Omit it to use the default, ExchangeName + \".junctions\".",
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
            .ServiceCollection.AddSingleton(sp => new RabbitMqTrainEventReceiver(
                sp.GetRequiredService<RabbitMqBroadcasterOptions>(),
                sp.GetService<Microsoft.Extensions.Logging.ILogger<RabbitMqTrainEventReceiver>>()
            )
            {
                // Only a host that handles junction events binds their exchange.
                BindJunctionExchange =
                    sp.GetService<IServiceProviderIsService>()
                        ?.IsService(typeof(IJunctionEventHandler))
                    ?? true,
            })
            .AddSingleton<ITrainEventReceiver>(sp =>
                sp.GetRequiredService<RabbitMqTrainEventReceiver>()
            );

        return builder;
    }
}
