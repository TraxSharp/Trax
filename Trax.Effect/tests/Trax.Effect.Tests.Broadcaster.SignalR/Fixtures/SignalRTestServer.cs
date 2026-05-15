using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Broadcaster.SignalR.Configuration.SignalRSinkOptions;
using Trax.Effect.Broadcaster.SignalR.Extensions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;

namespace Trax.Effect.Tests.Broadcaster.SignalR.Fixtures;

/// <summary>
/// Builds a TestServer hosting <c>TraxTrainEventHub</c> and a matching HubConnection
/// over the in-memory transport. Use in tests that need a real SignalR round-trip.
/// </summary>
internal sealed class SignalRTestServer : IAsyncDisposable
{
    public IHost Host { get; }
    public string HubPath { get; }

    private SignalRTestServer(IHost host, string hubPath)
    {
        Host = host;
        HubPath = hubPath;
    }

    public static async Task<SignalRTestServer> StartAsync(
        Action<SignalRSinkOptions>? configure = null,
        string hubPath = "/hubs/trax-events"
    )
    {
        var hostBuilder = new HostBuilder().ConfigureWebHost(webHost =>
        {
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
                        effects.UseBroadcaster(b => b.UseSignalRHub(configure))
                    );
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapTraxTrainEventHub(hubPath));
                });
        });

        var host = await hostBuilder.StartAsync();
        return new SignalRTestServer(host, hubPath);
    }

    public HubConnection CreateClient()
    {
        var server = Host.GetTestServer();
        return new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, HubPath.TrimStart('/')),
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
    }

    public T GetService<T>()
        where T : notnull => Host.Services.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        await Host.StopAsync();
        Host.Dispose();
    }
}
