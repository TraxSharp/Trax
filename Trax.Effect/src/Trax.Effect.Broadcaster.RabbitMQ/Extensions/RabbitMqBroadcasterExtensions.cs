using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.BroadcasterBuilder;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.RabbitMQ.Extensions;

public static class RabbitMqBroadcasterExtensions
{
    /// <summary>
    /// Configures RabbitMQ as the transport for cross-process lifecycle event broadcasting.
    /// </summary>
    /// <param name="builder">The broadcaster builder.</param>
    /// <param name="connectionString">AMQP connection URI (e.g., "amqp://guest:guest@localhost:5672").</param>
    /// <param name="configure">Optional callback to customize RabbitMQ options.</param>
    public static BroadcasterBuilder UseRabbitMq(
        this BroadcasterBuilder builder,
        string connectionString,
        Action<RabbitMqBroadcasterOptions>? configure = null
    )
    {
        var options = new RabbitMqBroadcasterOptions { ConnectionString = connectionString };
        configure?.Invoke(options);

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
