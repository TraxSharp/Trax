using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Broadcaster.SignalR.Extensions;
using Trax.Effect.Broadcaster.SignalR.Models;
using Trax.Effect.Broadcaster.SignalR.Services;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster.SignalR.IntegrationTests;

[TestFixture]
public class SignalRBroadcasterCompositionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static TrainLifecycleEventMessage Message(string externalId = "ext-1") =>
        new(
            MetadataId: 1,
            ExternalId: externalId,
            TrainName: "T.IDoThing",
            TrainState: "Completed",
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: "Completed",
            Executor: "RemoteExecutor",
            Output: null
        );

    private static async Task<IHost> StartHostWithBothTransports(
        ITrainEventBroadcaster broadcasterStub,
        ITrainEventReceiver receiverStub
    )
    {
        var hostBuilder = new HostBuilder().ConfigureWebHost(webHost =>
            webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddSignalR();
                    services.AddRouting();

                    var registry = new EffectRegistry();
                    services.AddSingleton<IEffectRegistry>(registry);
                    var traxBuilder = new TraxBuilder(services, registry);

                    traxBuilder.AddEffects(effects =>
                        effects.UseBroadcaster(b =>
                            b.UseRabbitMq("amqp://stub-not-used").UseSignalRHub()
                        )
                    );

                    // Replace the RabbitMQ broadcaster + receiver with NSubstitute stubs
                    // so the test doesn't require a real RabbitMQ instance.
                    services
                        .Where(d =>
                            d.ServiceType == typeof(ITrainEventBroadcaster)
                            || d.ServiceType == typeof(ITrainEventReceiver)
                        )
                        .ToList()
                        .ForEach(d => services.Remove(d));

                    services.AddSingleton(broadcasterStub);
                    services.AddSingleton(receiverStub);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapTraxTrainEventHub());
                })
        );

        return await hostBuilder.StartAsync();
    }

    [Test]
    public async Task RemoteEvent_DeliveredToSignalRDispatcherViaTrainEventHandler()
    {
        var broadcaster = Substitute.For<ITrainEventBroadcaster>();
        var receiver = Substitute.For<ITrainEventReceiver>();
        receiver.DisposeAsync().Returns(ValueTask.CompletedTask);

        using var host = await StartHostWithBothTransports(broadcaster, receiver);
        try
        {
            var server = host.GetTestServer();
            await using var connection = new HubConnectionBuilder()
                .WithUrl(
                    new Uri(server.BaseAddress, "hubs/trax-events"),
                    options =>
                    {
                        options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                        options.Transports = Microsoft
                            .AspNetCore
                            .Http
                            .Connections
                            .HttpTransportType
                            .LongPolling;
                    }
                )
                .Build();

            var tcs = new TaskCompletionSource<TraxClientEvent>();
            connection.On<TraxClientEvent>("TrainEvent", evt => tcs.TrySetResult(evt));
            await connection.StartAsync().WaitAsync(Timeout);

            // Simulate the receiver service: dispatch a remote message through every
            // registered ITrainEventHandler. The SignalR dispatcher is one of them.
            var handlers = host.Services.GetServices<ITrainEventHandler>().ToList();
            handlers
                .Should()
                .Contain(
                    h => h is SignalRTrainEventDispatcher,
                    "SignalR dispatcher must be registered as a remote-event handler"
                );

            foreach (var h in handlers)
                await h.HandleAsync(Message("remote-1"), CancellationToken.None);

            var received = await tcs.Task.WaitAsync(Timeout);
            received.ExternalId.Should().Be("remote-1");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    public async Task BothSinks_Registered_SignalRAndRabbitMqHooksPresent()
    {
        var broadcaster = Substitute.For<ITrainEventBroadcaster>();
        var receiver = Substitute.For<ITrainEventReceiver>();
        receiver.DisposeAsync().Returns(ValueTask.CompletedTask);

        using var host = await StartHostWithBothTransports(broadcaster, receiver);
        try
        {
            // Broadcaster is the RabbitMQ side (local event sink).
            host.Services.GetRequiredService<ITrainEventBroadcaster>()
                .Should()
                .BeSameAs(broadcaster);

            // SignalR dispatcher is registered as both lifecycle hook (via factory) and event handler.
            host.Services.GetRequiredService<SignalRTrainEventDispatcher>().Should().NotBeNull();
            host.Services.GetServices<ITrainEventHandler>()
                .OfType<SignalRTrainEventDispatcher>()
                .Should()
                .HaveCount(1);
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
