using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Broadcaster.SignalR.Extensions;
using Trax.Effect.Broadcaster.SignalR.Models;
using Trax.Effect.Broadcaster.SignalR.Services;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Effect.Tests.Broadcaster.SignalR.Fakes.Trains;
using Trax.Effect.Tests.Broadcaster.SignalR.Fixtures;

namespace Trax.Effect.Tests.Broadcaster.SignalR.IntegrationTests;

[TestFixture]
public class SignalRHubEndpointTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static TrainLifecycleEventMessage Message(
        string trainName = "T.IDoThing",
        string eventType = "Completed",
        string externalId = "ext-1"
    ) =>
        new(
            MetadataId: 1,
            ExternalId: externalId,
            TrainName: trainName,
            TrainState: "Completed",
            Timestamp: new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc),
            FailureJunction: null,
            FailureReason: null,
            EventType: eventType,
            Executor: null,
            Output: null
        );

    [Test]
    public async Task ClientConnects_AtCustomPath()
    {
        await using var server = await SignalRTestServer.StartAsync(hubPath: "/hubs/custom");
        await using var connection = server.CreateClient();

        await connection.StartAsync().WaitAsync(Timeout);

        connection.State.Should().Be(HubConnectionState.Connected);
    }

    [Test]
    public async Task Hub_DeliversTraxClientEventToConnectedClient_ViaIHubContext()
    {
        await using var server = await SignalRTestServer.StartAsync();
        await using var connection = server.CreateClient();

        var tcs = new TaskCompletionSource<TraxClientEvent>();
        connection.On<TraxClientEvent>("TrainEvent", evt => tcs.TrySetResult(evt));

        await connection.StartAsync().WaitAsync(Timeout);

        var hub = server.GetService<IHubContext<TraxTrainEventHub, ITraxTrainEventClient>>();
        var payload = new TraxClientEvent(
            7,
            "ext-7",
            "T.IDoThing",
            "Completed",
            DateTime.UtcNow,
            null
        );
        await hub.Clients.All.TrainEvent(payload);

        var received = await tcs.Task.WaitAsync(Timeout);
        received.ExternalId.Should().Be("ext-7");
        received.MetadataId.Should().Be(7);
        received.TrainName.Should().Be("T.IDoThing");
    }

    [Test]
    public async Task Dispatcher_DeliversToConnectedClient_EndToEnd()
    {
        await using var server = await SignalRTestServer.StartAsync();
        await using var connection = server.CreateClient();

        var tcs = new TaskCompletionSource<TraxClientEvent>();
        connection.On<TraxClientEvent>("TrainEvent", evt => tcs.TrySetResult(evt));

        await connection.StartAsync().WaitAsync(Timeout);

        var dispatcher = server.GetService<SignalRTrainEventDispatcher>();
        await dispatcher.HandleAsync(Message(externalId: "end-to-end"), CancellationToken.None);

        var received = await tcs.Task.WaitAsync(Timeout);
        received.ExternalId.Should().Be("end-to-end");
    }

    [Test]
    public async Task Dispatcher_FilterExclusion_ExcludedEventIsNotDelivered_IncludedIs()
    {
        await using var server = await SignalRTestServer.StartAsync(opts =>
            opts.OnlyForEvents("Completed")
        );
        await using var connection = server.CreateClient();

        var tcs = new TaskCompletionSource<TraxClientEvent>();
        connection.On<TraxClientEvent>("TrainEvent", evt => tcs.TrySetResult(evt));

        await connection.StartAsync().WaitAsync(Timeout);

        var dispatcher = server.GetService<SignalRTrainEventDispatcher>();

        // Fire excluded event first.
        await dispatcher.HandleAsync(
            Message(eventType: "Failed", externalId: "excluded"),
            CancellationToken.None
        );

        // Fire matching event immediately after.
        await dispatcher.HandleAsync(
            Message(eventType: "Completed", externalId: "included"),
            CancellationToken.None
        );

        var received = await tcs.Task.WaitAsync(Timeout);

        // Positive proof: received the *included* event, not the excluded one.
        received.ExternalId.Should().Be("included");
        received.EventType.Should().Be("Completed");
    }

    [Test]
    public void MapTraxTrainEventHub_WithoutAddSignalR_ThrowsHelpfulError()
    {
        var hostBuilder = new HostBuilder().ConfigureWebHost(webHost =>
            webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapTraxTrainEventHub());
                })
        );

        Func<Task> act = async () => await hostBuilder.StartAsync();

        act.Should().ThrowAsync<InvalidOperationException>().Result.WithMessage("*AddSignalR()*");
    }
}
