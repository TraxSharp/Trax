using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.BroadcasterBuilder;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Extensions;

public static class BroadcasterExtensions
{
    /// <summary>
    /// Configures cross-process lifecycle event broadcasting.
    /// Use the <paramref name="configure"/> callback to select a transport
    /// (e.g., <c>UseRabbitMq()</c>).
    /// </summary>
    /// <remarks>
    /// This registers:
    /// <list type="bullet">
    ///   <item><see cref="BroadcastLifecycleHook"/> — publishes lifecycle events to the broadcaster</item>
    ///   <item><see cref="TrainEventReceiverService"/> — hosted service that consumes events and dispatches to handlers</item>
    /// </list>
    /// The transport-specific <see cref="ITrainEventBroadcaster"/> and <see cref="ITrainEventReceiver"/>
    /// are registered by the callback (e.g., <c>b.UseRabbitMq("amqp://...")</c>).
    /// </remarks>
    public static TBuilder UseBroadcaster<TBuilder>(
        this TBuilder builder,
        Action<BroadcasterBuilder> configure
    )
        where TBuilder : TraxEffectBuilder
    {
        var broadcasterBuilder = new BroadcasterBuilder(builder);
        configure(broadcasterBuilder);

        builder.ServiceCollection.AddTransient<BroadcastLifecycleHook>();
        builder
            .ServiceCollection.AddSingleton<BroadcastLifecycleHookFactory>()
            .AddSingleton<ITrainLifecycleHookFactory>(sp =>
                sp.GetRequiredService<BroadcastLifecycleHookFactory>()
            );

        builder.EffectRegistry?.Register(typeof(BroadcastLifecycleHookFactory), toggleable: false);

        builder.ServiceCollection.AddHostedService<TrainEventReceiverService>();

        return builder;
    }
}
