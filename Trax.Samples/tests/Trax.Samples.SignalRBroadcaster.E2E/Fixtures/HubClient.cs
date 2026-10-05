using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Trax.Samples.SignalRBroadcaster.E2E.Factories;

namespace Trax.Samples.SignalRBroadcaster.E2E.Fixtures;

/// <summary>
/// A SignalR client of the sample's hub, as a browser would be: it carries the sign-in cookie (or
/// none), and collects every <c>TrainEvent</c> the hub sends.
/// </summary>
public sealed class HubClient : IAsyncDisposable
{
    private readonly ConcurrentQueue<LiveTrainEvent> _received = new();

    private HubClient(HubConnection connection)
    {
        Connection = connection;
        connection.On<LiveTrainEvent>("TrainEvent", _received.Enqueue);
    }

    public HubConnection Connection { get; }

    public static HubClient Create(BroadcasterFactory factory, string? cookie)
    {
        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, "/hubs/trax-events"),
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                    options.Transports = HttpTransportType.LongPolling;
                    if (cookie is not null)
                        options.Headers["Cookie"] = cookie;
                }
            )
            .Build();
        return new HubClient(connection);
    }

    /// <summary>Waits until an event matching <paramref name="match"/> arrives.</summary>
    public async Task<LiveTrainEvent> WaitFor(
        Func<LiveTrainEvent, bool> match,
        TimeSpan? timeout = null
    )
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            var hit = _received.FirstOrDefault(match);
            if (hit is not null)
                return hit;
            // determinism: polling interval while waiting, bounded by the deadline above.
            await Task.Delay(50);
        }

        throw new TimeoutException(
            "No matching event arrived. Received: "
                + string.Join(", ", _received.Select(e => $"{e.TrainName}/{e.EventType}"))
        );
    }

    public ValueTask DisposeAsync() => Connection.DisposeAsync();

    /// <summary>Signs in through the demo endpoint and returns the cookie header a browser would send.</summary>
    public static async Task<string> SignIn(BroadcasterFactory factory)
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
            }
        );
        var response = await client.PostAsync("/demo/sign-in", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        return string.Join(
            "; ",
            response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0])
        );
    }
}
