using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Broadcaster.SignalR.Configuration;
using Trax.Effect.Broadcaster.SignalR.Extensions;
using Trax.Effect.Broadcaster.SignalR.Services;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Tests.Broadcaster.SignalR.UnitTests;

[TestFixture]
public class SignalRSinkRegistrationTests
{
    private static ServiceProvider BuildProvider(Action<TraxBuilder>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();

        var registry = new EffectRegistry();
        var builder = new TraxBuilder(services, registry);

        builder.AddEffects(effects => effects.UseBroadcaster(b => b.UseSignalRHub()));
        extra?.Invoke(builder);

        return services.BuildServiceProvider();
    }

    [Test]
    public void UseSignalRHub_RegistersDispatcherSingleton()
    {
        using var sp = BuildProvider();

        var first = sp.GetRequiredService<SignalRTrainEventDispatcher>();
        var second = sp.GetRequiredService<SignalRTrainEventDispatcher>();

        first.Should().BeSameAs(second);
    }

    [Test]
    public void UseSignalRHub_RegistersConfigurationAsSingleton()
    {
        using var sp = BuildProvider();

        sp.GetRequiredService<SignalRSinkConfiguration>().Should().NotBeNull();
    }

    [Test]
    public void UseSignalRHub_RegistersLifecycleHookFactoryThatReturnsTheDispatcher()
    {
        using var sp = BuildProvider();

        var dispatcher = sp.GetRequiredService<SignalRTrainEventDispatcher>();
        var factories = sp.GetServices<ITrainLifecycleHookFactory>().ToList();
        var signalRFactory = factories.OfType<SignalRTrainEventDispatcherFactory>().Single();

        ITrainLifecycleHook hook = signalRFactory.Create();
        hook.Should().BeSameAs(dispatcher);
    }

    [Test]
    public void UseSignalRHub_RegistersTrainEventHandlerResolvingToDispatcher()
    {
        using var sp = BuildProvider();

        var dispatcher = sp.GetRequiredService<SignalRTrainEventDispatcher>();
        var handler = sp.GetServices<ITrainEventHandler>()
            .OfType<SignalRTrainEventDispatcher>()
            .Single();

        handler.Should().BeSameAs(dispatcher);
    }

    [Test]
    public void UseSignalRHub_HookFactoryAndEventHandlerShareTheSameSingletonInstance()
    {
        using var sp = BuildProvider();

        var dispatcher = sp.GetRequiredService<SignalRTrainEventDispatcher>();
        var hookFromFactory = sp.GetServices<ITrainLifecycleHookFactory>()
            .OfType<SignalRTrainEventDispatcherFactory>()
            .Single()
            .Create();
        var handler = sp.GetServices<ITrainEventHandler>()
            .OfType<SignalRTrainEventDispatcher>()
            .Single();

        hookFromFactory.Should().BeSameAs(dispatcher);
        handler.Should().BeSameAs(dispatcher);
        ((object)hookFromFactory).Should().BeSameAs(handler);
    }

    [Test]
    public void UseSignalRHub_FactoryMarkedNonToggleableInEffectRegistry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var registry = new EffectRegistry();
        var builder = new TraxBuilder(services, registry);

        builder.AddEffects(effects => effects.UseBroadcaster(b => b.UseSignalRHub()));

        registry.IsToggleable(typeof(SignalRTrainEventDispatcherFactory)).Should().BeFalse();
        registry.IsEnabled(typeof(SignalRTrainEventDispatcherFactory)).Should().BeTrue();
    }

    [Test]
    public void UseSignalRHub_AppliesFilterOptionsToRegisteredConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var registry = new EffectRegistry();
        var builder = new TraxBuilder(services, registry);

        builder.AddEffects(effects =>
            effects.UseBroadcaster(b =>
                b.UseSignalRHub(o => o.OnlyForEvents("Completed", "Failed"))
            )
        );

        using var sp = services.BuildServiceProvider();
        var config = sp.GetRequiredService<SignalRSinkConfiguration>();

        config.EventTypeFilter.Should().BeEquivalentTo(new[] { "Completed", "Failed" });
    }

    [Test]
    public async Task UseSignalRHub_StandaloneRegistersNoOpTransport()
    {
        // SignalR-only topology: no UseRabbitMq. The no-op broadcaster + receiver
        // make UseBroadcaster()'s hosted service + BroadcastLifecycleHook resolvable
        // so the host can start without a real transport.
        await using var sp = BuildProvider();

        sp.GetRequiredService<ITrainEventBroadcaster>().Should().NotBeNull();
        sp.GetRequiredService<ITrainEventReceiver>().Should().NotBeNull();
    }

    [Test]
    public async Task StandaloneSignalR_NullBroadcaster_PublishAsyncIsCalled_NoThrow()
    {
        // BroadcastLifecycleHook calls ITrainEventBroadcaster.PublishAsync on every
        // local lifecycle event. In a SignalR-only topology the resolved broadcaster
        // is the no-op; that path must complete successfully (no throw, no hang).
        await using var sp = BuildProvider();
        var broadcaster = sp.GetRequiredService<ITrainEventBroadcaster>();

        var message = new TrainLifecycleEventMessage(
            MetadataId: 1,
            ExternalId: "x",
            TrainName: "T.IT",
            TrainState: "Completed",
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: "Completed",
            Executor: null,
            Output: null
        );

        Func<Task> act = () => broadcaster.PublishAsync(message, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task StandaloneSignalR_NullReceiver_StartStopDispose_NoThrow()
    {
        // TrainEventReceiverService calls StartAsync on the receiver. In a SignalR-only
        // topology, the resolved receiver is the no-op; Start/Stop/Dispose must all
        // complete without throwing.
        await using var sp = BuildProvider();
        var receiver = sp.GetRequiredService<ITrainEventReceiver>();
        var handlerCalled = false;
        Func<TrainLifecycleEventMessage, CancellationToken, Task> handler = (_, _) =>
        {
            handlerCalled = true;
            return Task.CompletedTask;
        };

        await receiver.StartAsync(handler, CancellationToken.None);
        await receiver.StopAsync(CancellationToken.None);
        Func<Task> dispose = async () => await receiver.DisposeAsync();
        await dispose.Should().NotThrowAsync();

        // The no-op receiver must not invoke the supplied handler — there is no transport
        // delivering events, so any call would represent a phantom-event regression.
        handlerCalled.Should().BeFalse();
    }

    [Test]
    public async Task UseSignalRHub_ChainsWithUseRabbitMq_BothTransportsRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        var registry = new EffectRegistry();
        var builder = new TraxBuilder(services, registry);

        builder.AddEffects(effects =>
            effects.UseBroadcaster(b => b.UseRabbitMq("amqp://localhost").UseSignalRHub())
        );

        await using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<ITrainEventBroadcaster>().Should().NotBeNull();
        sp.GetRequiredService<ITrainEventReceiver>().Should().NotBeNull();
        sp.GetRequiredService<SignalRTrainEventDispatcher>().Should().NotBeNull();
        sp.GetServices<ITrainEventHandler>()
            .OfType<SignalRTrainEventDispatcher>()
            .Should()
            .HaveCount(1);
    }
}
