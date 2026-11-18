using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.BroadcasterBuilder;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// An in-process broadcast transport for hosts that share one process. Each host joins as an <see cref="Endpoint"/>,
/// which is both its broadcaster and its receiver. A published message is serialized to JSON bytes and handed, in
/// publish order, to every endpoint whose receiver has started, the publisher's own included.
/// </summary>
internal sealed class LoopbackBus
{
    private readonly Lock _gate = new();
    private readonly List<Endpoint> _started = [];

    public Endpoint Join() => new(this);

    private void Publish(byte[] body)
    {
        lock (_gate)
            foreach (var endpoint in _started)
                endpoint.Enqueue(body);
    }

    private void Start(Endpoint endpoint)
    {
        lock (_gate)
            _started.Add(endpoint);
    }

    private void Stop(Endpoint endpoint)
    {
        lock (_gate)
            _started.Remove(endpoint);
    }

    /// <summary>One host's side of the bus.</summary>
    internal sealed class Endpoint(LoopbackBus bus) : ITrainEventBroadcaster, ITrainEventReceiver
    {
        private readonly ConcurrentQueue<TrainLifecycleEventMessage> _published = new();
        private Channel<byte[]>? _inbox;
        private Task? _pump;

        /// <summary>Every message this host published.</summary>
        public IReadOnlyCollection<TrainLifecycleEventMessage> Published => _published.ToArray();

        /// <summary>Registers this endpoint as the host's transport, as <c>UseRabbitMq</c> registers its own.</summary>
        public BroadcasterBuilder Register(BroadcasterBuilder builder)
        {
            builder.ServiceCollection.AddSingleton<ITrainEventBroadcaster>(this);
            builder.ServiceCollection.AddSingleton<ITrainEventReceiver>(this);
            return builder;
        }

        public Task PublishAsync(TrainLifecycleEventMessage message, CancellationToken ct)
        {
            _published.Enqueue(message);
            bus.Publish(JsonSerializer.SerializeToUtf8Bytes(message));
            return Task.CompletedTask;
        }

        public Task StartAsync(
            Func<TrainLifecycleEventMessage, CancellationToken, Task> handler,
            CancellationToken ct
        )
        {
            var inbox = Channel.CreateUnbounded<byte[]>(
                new UnboundedChannelOptions { SingleReader = true }
            );
            _inbox = inbox;
            _pump = Task.Run(
                async () =>
                {
                    await foreach (
                        var body in inbox
                            .Reader.ReadAllAsync(cancellationToken: ct)
                            .ConfigureAwait(false)
                    )
                    {
                        var message = JsonSerializer
                            .Deserialize<TrainLifecycleEventMessage>(body)
                            .NotNull();
                        await handler(message, CancellationToken.None).ConfigureAwait(false);
                    }
                },
                cancellationToken: ct
            );
            bus.Start(this);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken ct)
        {
            bus.Stop(this);
            _inbox?.Writer.TryComplete();
            if (_pump is not null)
                await _pump.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync() =>
            await StopAsync(CancellationToken.None).ConfigureAwait(false);

        internal void Enqueue(byte[] body) => _inbox?.Writer.TryWrite(body);
    }
}
