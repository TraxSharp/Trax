using System.Net.WebSockets;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Broadcaster.SignalR.Extensions;
using Trax.Effect.Broadcaster.SignalR.Services;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster.SignalR.IntegrationTests;

/// <summary>
/// The dispatcher runs inside the train's lifecycle hooks, which a train awaits inline. These tests use a
/// real Kestrel socket, because the in-memory TestServer transport has no TCP backpressure.
/// <para>Enforces <c>docs/adr/0012-the-signalr-sink-queues-events-and-drops-when-clients-fall-behind.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "docs/adr/0012-the-signalr-sink-queues-events-and-drops-when-clients-fall-behind.md"
)]
public class SignalRSlowClientTests
{
    private static TrainLifecycleEventMessage Failed(string reason) =>
        new(
            MetadataId: 1,
            ExternalId: "ext-1",
            TrainName: "Some.ITrain",
            TrainState: "Failed",
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: reason,
            EventType: "Failed",
            Executor: null,
            Output: null,
            HostName: null,
            HostEnvironment: null
        );

    [Test]
    public async Task A_client_that_stops_reading_must_not_stall_the_lifecycle_hook()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        var registry = new EffectRegistry();
        builder.Services.AddSingleton<IEffectRegistry>(registry);
        new TraxBuilder(builder.Services, registry).AddEffects(effects =>
            effects.UseBroadcaster(b => b.UseSignalRHub())
        );
        await using var app = builder.Build();
        app.MapTraxTrainEventHub(hub => hub.AllowAnonymous());
        await app.StartAsync();

        var address = app
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();
        var hubUri = new Uri(address.Replace("http://", "ws://") + "/hubs/trax-events");

        // Connect and complete the SignalR handshake, then stop reading from the socket.
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(hubUri, CancellationToken.None);
        await socket.SendAsync(
            Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None
        );
        // Read the handshake response, so the connection is registered with the hub before the
        // client stops reading.
        var handshake = new byte[256];
        await socket
            .ReceiveAsync(handshake, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        var dispatcher = app.Services.GetRequiredService<SignalRTrainEventDispatcher>();
        var reason = new string('x', 256 * 1024);

        // Every lifecycle hook (OnStarted, OnCompleted, OnFailed, OnStateChanged) and every remote event
        // goes through this one send, and ServiceTrain awaits the hooks inline. To a client that reads, each
        // send below takes well under a millisecond.
        var slowest = TimeSpan.Zero;
        for (var i = 0; i < 64; i++)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            await dispatcher.HandleAsync(Failed(reason), CancellationToken.None);
            if (started.Elapsed > slowest)
                slowest = started.Elapsed;
        }

        slowest
            .Should()
            .BeLessThan(
                TimeSpan.FromSeconds(2),
                "a connected client that stops reading must not hold the send (and the train awaiting it) "
                    + "(docs/adr/0012-the-signalr-sink-queues-events-and-drops-when-clients-fall-behind.md)"
            );

        socket.Abort();
        await app.StopAsync();
    }
}
