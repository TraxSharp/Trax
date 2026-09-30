using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trax.Effect.Configuration.BroadcasterBuilder;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Extensions;

/// <summary>
/// Adds <c>UseBroadcaster()</c> to the effect builder, which publishes train lifecycle events and data-change
/// signals to other processes through a transport package such as Trax.Effect.Broadcaster.RabbitMQ.
/// See https://traxsharp.net/docs/sdk-reference/configuration/use-broadcaster.
/// </summary>
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
    ///   <item><see cref="BroadcastChangeSink"/> — forwards coalesced data-change signals to the broadcaster</item>
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
        // One identity per service provider: replicas, and hosts sharing a process, each get their own.
        builder.ServiceCollection.TryAddSingleton<BroadcastInstance>();

        var broadcasterBuilder = new BroadcasterBuilder(builder);
        configure(broadcasterBuilder);

        builder.AddLifecycleHook<BroadcastLifecycleHook>(toggleable: false);

        builder.ServiceCollection.AddHostedService<TrainEventReceiverService>();

        // Fan coalesced data-change signals out cross-process over the same transport, so a
        // split scheduler process can push work-queue/dead-letter changes to the API's clients.
        builder.ServiceCollection.AddSingleton<IChangeSignalSink, BroadcastChangeSink>();

        return builder;
    }
}
