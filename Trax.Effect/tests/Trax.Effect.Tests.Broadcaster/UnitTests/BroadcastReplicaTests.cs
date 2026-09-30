using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

/// <summary>
/// Replicas of one app share an entry assembly, so they share an executor name. Each host stamps
/// its own instance id on what it publishes, and a receiver drops only what carries its own id.
/// </summary>
[TestFixture]
public class BroadcastReplicaTests
{
    [Test]
    public async Task TwoReplicasOfOneApp_EachReceivesTheOthersEvent_AndDropsItsOwn()
    {
        var bus = new InMemoryBus();
        await using var a = await Replica.StartAsync(bus);
        await using var b = await Replica.StartAsync(bus);

        await a.Hook.OnCompleted(CompletedRun("from-a"), CancellationToken.None);
        await b.Hook.OnCompleted(CompletedRun("from-b"), CancellationToken.None);

        a.Handled.Select(m => m.ExternalId).Should().Equal("from-b");
        b.Handled.Select(m => m.ExternalId).Should().Equal("from-a");
    }

    [Test]
    public async Task TwoReplicasOfOneApp_KeepTheirExecutorForDisplay()
    {
        var bus = new InMemoryBus();
        await using var a = await Replica.StartAsync(bus);
        await using var b = await Replica.StartAsync(bus);

        await a.Hook.OnCompleted(CompletedRun("from-a"), CancellationToken.None);

        var received = b.Handled.Should().ContainSingle().Subject;
        received.Executor.Should().Be(bus.Published.Single().Executor);
        received.InstanceId.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task ChangeSignalsFromAnotherReplica_AreDelivered()
    {
        var bus = new InMemoryBus();
        await using var a = await Replica.StartAsync(bus);
        await using var b = await Replica.StartAsync(bus);

        await a.Sink.FlushAsync(
            [Trax.Effect.Services.ChangeSignal.ChangeDomain.WorkQueue],
            CancellationToken.None
        );

        a.Handled.Should().BeEmpty();
        b.Handled.Should()
            .ContainSingle()
            .Which.EventType.Should()
            .Be(TrainLifecycleEventMessage.DataChangedEventType);
    }

    [Test]
    public async Task AnEventWithNoInstanceId_IsFromAnotherHost()
    {
        var bus = new InMemoryBus();
        await using var a = await Replica.StartAsync(bus);

        // A publisher on an older version stamps no instance id. It cannot be this host.
        await bus.PublishAsync(
            new TrainLifecycleEventMessage(
                MetadataId: 1,
                ExternalId: "old-publisher",
                TrainName: "T",
                TrainState: "Completed",
                Timestamp: DateTime.UtcNow,
                FailureJunction: null,
                FailureReason: null,
                EventType: "Completed",
                Executor: System.Reflection.Assembly.GetEntryAssembly()?.GetAssemblyProject(),
                Output: null
            ),
            CancellationToken.None
        );

        a.Handled.Select(m => m.ExternalId).Should().Equal("old-publisher");
    }

    private static Metadata CompletedRun(string externalId)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = "Replica.ITrain",
                ExternalId = externalId,
                Input = null,
            }
        );
        metadata.TrainState = TrainState.Completed;
        return metadata;
    }

    private sealed class Replica : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IHostedService _receiverService;

        private Replica(
            ServiceProvider provider,
            IHostedService receiverService,
            RecordingHandler handler
        )
        {
            _provider = provider;
            _receiverService = receiverService;
            Handler = handler;
            Hook = ActivatorUtilities.CreateInstance<BroadcastLifecycleHook>(provider);
            Sink = ActivatorUtilities.CreateInstance<BroadcastChangeSink>(provider);
        }

        private RecordingHandler Handler { get; }
        public BroadcastLifecycleHook Hook { get; }
        public BroadcastChangeSink Sink { get; }
        public IReadOnlyList<TrainLifecycleEventMessage> Handled => Handler.Messages;

        public static async Task<Replica> StartAsync(InMemoryBus bus)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            var handler = new RecordingHandler();
            var receiver = new InMemoryReceiver(bus);
            services.AddSingleton<ITrainEventHandler>(handler);

            var builder = new TraxBuilder(services, new EffectRegistry());
            builder.AddEffects(effects =>
                effects.UseBroadcaster(b =>
                {
                    b.ServiceCollection.AddSingleton<ITrainEventBroadcaster>(bus);
                    b.ServiceCollection.AddSingleton<ITrainEventReceiver>(receiver);
                })
            );

            var provider = services.BuildServiceProvider();
            var receiverService = provider
                .GetServices<IHostedService>()
                .OfType<TrainEventReceiverService>()
                .Single();

            // ExecuteAsync runs in the background, so wait for it to subscribe before publishing.
            await receiverService.StartAsync(CancellationToken.None);
            await receiver.Subscribed.WaitAsync(TimeSpan.FromSeconds(10));
            return new Replica(provider, receiverService, handler);
        }

        public async ValueTask DisposeAsync()
        {
            await _receiverService.StopAsync(CancellationToken.None);
            await _provider.DisposeAsync();
        }
    }

    private sealed class RecordingHandler : ITrainEventHandler
    {
        private readonly List<TrainLifecycleEventMessage> _messages = [];

        public IReadOnlyList<TrainLifecycleEventMessage> Messages
        {
            get
            {
                lock (_messages)
                    return _messages.ToList();
            }
        }

        public Task HandleAsync(TrainLifecycleEventMessage message, CancellationToken ct)
        {
            lock (_messages)
                _messages.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>A fanout that delivers each publish inline to every started receiver.</summary>
    private sealed class InMemoryBus : ITrainEventBroadcaster
    {
        private readonly object _gate = new();
        private readonly List<Func<TrainLifecycleEventMessage, CancellationToken, Task>> _subs = [];
        private readonly List<TrainLifecycleEventMessage> _published = [];

        public IReadOnlyList<TrainLifecycleEventMessage> Published
        {
            get
            {
                lock (_gate)
                    return _published.ToList();
            }
        }

        public void Subscribe(Func<TrainLifecycleEventMessage, CancellationToken, Task> handler)
        {
            lock (_gate)
                _subs.Add(handler);
        }

        public void Unsubscribe(Func<TrainLifecycleEventMessage, CancellationToken, Task> handler)
        {
            lock (_gate)
                _subs.Remove(handler);
        }

        public async Task PublishAsync(TrainLifecycleEventMessage message, CancellationToken ct)
        {
            List<Func<TrainLifecycleEventMessage, CancellationToken, Task>> subs;
            lock (_gate)
            {
                _published.Add(message);
                subs = _subs.ToList();
            }
            foreach (var sub in subs)
                await sub(message, ct);
        }
    }

    private sealed class InMemoryReceiver(InMemoryBus bus) : ITrainEventReceiver
    {
        private Func<TrainLifecycleEventMessage, CancellationToken, Task>? _handler;
        private readonly TaskCompletionSource _subscribed = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task Subscribed => _subscribed.Task;

        public Task StartAsync(
            Func<TrainLifecycleEventMessage, CancellationToken, Task> handler,
            CancellationToken ct
        )
        {
            _handler = handler;
            bus.Subscribe(handler);
            _subscribed.TrySetResult();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct)
        {
            if (_handler is not null)
                bus.Unsubscribe(_handler);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
