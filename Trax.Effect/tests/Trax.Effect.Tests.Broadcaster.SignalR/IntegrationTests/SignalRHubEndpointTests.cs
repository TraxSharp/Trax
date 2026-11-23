using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Core.Extensions;
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
        var server = await SignalRTestServer.StartAsync(hubPath: "/hubs/custom");
        await using var serverDisposal = server.ConfigureAwait(false);
        var connection = server.CreateClient();
        await using var connectionDisposal = connection.ConfigureAwait(false);

        await connection.StartAsync().WaitAsync(Timeout, TimeProvider.System);

        connection.State.Should().Be(HubConnectionState.Connected);
    }

    [Test]
    public async Task Hub_DeliversTraxClientEventToConnectedClient_ViaIHubContext()
    {
        var server = await SignalRTestServer.StartAsync();
        await using var serverDisposal = server.ConfigureAwait(false);
        var connection = server.CreateClient();
        await using var connectionDisposal = connection.ConfigureAwait(false);

        var tcs = new TaskCompletionSource<TraxClientEvent>();
        connection.On<TraxClientEvent>("TrainEvent", evt => tcs.TrySetResult(evt));

        await connection.StartAsync().WaitAsync(Timeout, TimeProvider.System);

        var hub = server.GetService<IHubContext<TraxTrainEventHub, ITraxTrainEventClient>>();
        var payload = new TraxClientEvent(
            7,
            "ext-7",
            "T.IDoThing",
            "Completed",
            TestTime.UtcDateTime,
            FailureReason: null
        );
        await hub.Clients.All.TrainEvent(payload);

        var received = await tcs.Task.WaitAsync(Timeout, TimeProvider.System);
        received.ExternalId.Should().Be("ext-7");
        received.MetadataId.Should().Be(7);
        received.TrainName.Should().Be("T.IDoThing");
    }

    [Test]
    public async Task Dispatcher_DeliversToConnectedClient_EndToEnd()
    {
        var server = await SignalRTestServer.StartAsync();
        await using var serverDisposal = server.ConfigureAwait(false);
        var connection = server.CreateClient();
        await using var connectionDisposal = connection.ConfigureAwait(false);

        var tcs = new TaskCompletionSource<TraxClientEvent>();
        connection.On<TraxClientEvent>("TrainEvent", evt => tcs.TrySetResult(evt));

        await connection.StartAsync().WaitAsync(Timeout, TimeProvider.System);

        var dispatcher = server.GetService<SignalRTrainEventDispatcher>();
        await dispatcher.HandleAsync(Message(externalId: "end-to-end"), CancellationToken.None);

        var received = await tcs.Task.WaitAsync(Timeout, TimeProvider.System);
        received.ExternalId.Should().Be("end-to-end");
    }

    [Test]
    public async Task Dispatcher_FilterExclusion_ExcludedEventIsNotDelivered_IncludedIs()
    {
        var server = await SignalRTestServer.StartAsync(opts => opts.OnlyForEvents("Completed"));
        await using var serverDisposal = server.ConfigureAwait(false);
        var connection = server.CreateClient();
        await using var connectionDisposal = connection.ConfigureAwait(false);

        var tcs = new TaskCompletionSource<TraxClientEvent>();
        connection.On<TraxClientEvent>("TrainEvent", evt => tcs.TrySetResult(evt));

        await connection.StartAsync().WaitAsync(Timeout, TimeProvider.System);

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

        var received = await tcs.Task.WaitAsync(Timeout, TimeProvider.System);

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
                    app.UseEndpoints(endpoints =>
                        endpoints.MapTraxTrainEventHub(hub => hub.AllowAnonymous())
                    );
                })
        );

        Func<Task> act = async () => await hostBuilder.StartAsync().ConfigureAwait(false);

        act.Should().ThrowAsync<InvalidOperationException>().Result.WithMessage("*AddSignalR()*");
    }

    private static async Task<HttpConnectionDispatcherOptions> MappedOptionsAsync(
        Action<IEndpointRouteBuilder> map
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSignalR();
        var app = builder.Build();
        await using var appDisposal = app.ConfigureAwait(false);
        map(app);

        return ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(ds => ds.Endpoints)
            .Select(e => e.Metadata.GetMetadata<HttpConnectionDispatcherOptions>())
            .First(o => o is not null)
            .NotNull();
    }

    [Test]
    public async Task MapTraxTrainEventHub_SetsAShortTransportSendTimeout()
    {
        var options = await MappedOptionsAsync(endpoints =>
            endpoints.MapTraxTrainEventHub(hub => hub.AllowAnonymous())
        );

        options
            .TransportSendTimeout.Should()
            .Be(SignalRHubEndpointExtensions.DefaultTransportSendTimeout)
            .And.BeLessThan(new HttpConnectionDispatcherOptions().TransportSendTimeout);
    }

    [Test]
    public async Task MapTraxTrainEventHub_HostOverridesTheTransportSendTimeout()
    {
        var options = await MappedOptionsAsync(endpoints =>
            endpoints.MapTraxTrainEventHub(
                "/hubs/trax-events",
                hub =>
                    hub.AllowAnonymous()
                        .ConfigureConnection(o => o.TransportSendTimeout = TimeSpan.FromSeconds(7))
            )
        );

        options.TransportSendTimeout.Should().Be(TimeSpan.FromSeconds(7));
    }
}
