using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trax.Effect.Broadcaster.SignalR.Services;
using Trax.Effect.Configuration.BroadcasterBuilder;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Broadcaster.SignalR.Extensions;

/// <summary>
/// Adds <c>UseSignalRHub</c> to the broadcaster builder, which pushes train lifecycle events to
/// browser and other SignalR clients. Pair it with <see cref="SignalRHubEndpointExtensions"/> to
/// map the hub those clients connect to.
/// </summary>
public static class SignalRBroadcasterExtensions
{
    /// <summary>
    /// Adds a SignalR sink to the broadcaster pipeline so connected clients (Blazor, JS, etc.)
    /// receive train lifecycle events in real time. Compose alongside cross-process transports
    /// like <c>UseRabbitMq()</c>, or use on its own when producer and consumer run in the same
    /// process.
    /// </summary>
    /// <param name="builder">The broadcaster builder.</param>
    /// <param name="configure">
    /// Optional callback to filter events (<c>OnlyForEvents</c>, <c>OnlyForTrains</c>),
    /// replace the default <c>TraxClientEvent</c> projection (<c>WithProjection</c>), or send each
    /// step of a run as well (<c>WithJunctionEvents</c>).
    /// </param>
    /// <remarks>
    /// Map the hub endpoint with an authorization posture, e.g.
    /// <c>app.MapTraxTrainEventHub(hub =&gt; hub.RequireAuthorization())</c>,
    /// and call <c>services.AddSignalR()</c> on the host. The dispatcher is registered
    /// as both an <see cref="Trax.Effect.Services.TrainLifecycleHook.ITrainLifecycleHook"/>
    /// (local-event path) and an <see cref="ITrainEventHandler"/> (remote-event path), so
    /// browsers receive events whether trains run in-process or arrive over a transport.
    /// A lifecycle hook only queues the event: delivery to clients happens on a background
    /// sender, so a slow or stalled client never holds up a train. See
    /// <see cref="Configuration.SignalRSinkOptions.SignalRSinkOptions.WithDeliveryQueueCapacity"/>.
    /// </remarks>
    public static BroadcasterBuilder UseSignalRHub(
        this BroadcasterBuilder builder,
        Action<Configuration.SignalRSinkOptions.SignalRSinkOptions>? configure = null
    )
    {
        var options = (Configuration.SignalRSinkOptions.SignalRSinkOptions)
            Activator.CreateInstance(
                typeof(Configuration.SignalRSinkOptions.SignalRSinkOptions),
                nonPublic: true
            )!;
        configure?.Invoke(options);
        var config = options.Build();

        builder.ServiceCollection.AddSingleton(config);
        builder.ServiceCollection.AddSingleton<SignalRTrainEventDispatcher>();

        // UseBroadcaster() always registers TrainEventReceiverService + BroadcastLifecycleHook,
        // both of which need a transport. SignalR is a sink, not a transport, so wire no-op
        // implementations as fallbacks. A real transport (e.g. UseRabbitMq) registers via
        // AddSingleton and takes precedence over these TryAdds.
        builder.ServiceCollection.TryAddSingleton<
            ITrainEventBroadcaster,
            NullTrainEventBroadcaster
        >();
        builder.ServiceCollection.TryAddSingleton<ITrainEventReceiver, NullTrainEventReceiver>();

        builder.ServiceCollection.AddSingleton<ITrainLifecycleHookFactory>(
            sp => new SignalRTrainEventDispatcherFactory(
                sp.GetRequiredService<SignalRTrainEventDispatcher>()
            )
        );

        builder.ServiceCollection.AddSingleton<ITrainEventHandler>(sp =>
            sp.GetRequiredService<SignalRTrainEventDispatcher>()
        );

        // Junction events reach the dispatcher only when the host asked for them.
        if (config.JunctionEvents)
            builder.ServiceCollection.AddSingleton<IJunctionEventHandler>(sp =>
                sp.GetRequiredService<SignalRTrainEventDispatcher>()
            );

        // The dispatcher delivers from a background queue; stopping the host drains it.
        builder.ServiceCollection.AddHostedService(sp =>
            sp.GetRequiredService<SignalRTrainEventDispatcher>()
        );

        builder.EffectRegistry?.Register(
            typeof(SignalRTrainEventDispatcherFactory),
            toggleable: false
        );

        return builder;
    }
}
