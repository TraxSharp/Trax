using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Samples.SignalRBroadcaster.Workarounds;

/// <summary>
/// TEMPORARY, not part of the pattern this sample teaches; do not copy it.
///
/// <para>The SignalR sink is one long-lived object that is also handed to every run as a lifecycle
/// hook, and the run disposes its hooks when it ends. Disposing the sink stops its delivery queue,
/// so in the current Trax.Effect the first train a host runs silences the hub for good. This wraps
/// the sink, for the run's purposes only, in a hook that forwards every event and has nothing to
/// dispose. The host still stops the sink properly at shutdown.</para>
///
/// <para>Delete this file and the <c>KeepSignalRSinkAlive()</c> call once Trax.Effect stops
/// disposing shared hooks.</para>
/// </summary>
internal static class SignalRSinkKeepAlive
{
    private const string SinkFactoryName = "SignalRTrainEventDispatcherFactory";

    public static IServiceCollection KeepSignalRSinkAlive(this IServiceCollection services)
    {
        for (var i = 0; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (
                descriptor.ServiceType != typeof(ITrainLifecycleHookFactory)
                || descriptor.ImplementationFactory is not { } create
            )
                continue;

            services[i] = ServiceDescriptor.Singleton<ITrainLifecycleHookFactory>(sp =>
            {
                var factory = (ITrainLifecycleHookFactory)create(sp);
                return factory.GetType().Name == SinkFactoryName
                    ? new NonDisposingFactory(factory.Create())
                    : factory;
            });
        }

        return services;
    }

    private sealed class NonDisposingFactory(ITrainLifecycleHook sink) : ITrainLifecycleHookFactory
    {
        public ITrainLifecycleHook Create() => new Forwarder(sink);
    }

    private sealed class Forwarder(ITrainLifecycleHook sink) : ITrainLifecycleHook
    {
        public Task OnStarted(Metadata metadata, CancellationToken ct) =>
            sink.OnStarted(metadata, ct);

        public Task OnCompleted(Metadata metadata, CancellationToken ct) =>
            sink.OnCompleted(metadata, ct);

        public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
            sink.OnFailed(metadata, exception, ct);

        public Task OnCancelled(Metadata metadata, CancellationToken ct) =>
            sink.OnCancelled(metadata, ct);

        public Task OnStateChanged(Metadata metadata, CancellationToken ct) =>
            sink.OnStateChanged(metadata, ct);
    }
}
