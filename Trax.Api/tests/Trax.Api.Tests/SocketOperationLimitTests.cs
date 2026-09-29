using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;
using static Trax.Api.Tests.Auth.SocketInterceptorTestHelpers;

namespace Trax.Api.Tests;

/// <summary>
/// A WebSocket connection runs a bounded number of operations at once. An operation started past
/// the limit is refused with <c>TRAX_SOCKET_OPERATION_LIMIT</c> and takes no place; a completed
/// operation frees its place; the limit is counted per connection.
///
/// <para>Enforces <c>docs/adr/0015-a-socket-runs-a-bounded-number-of-operations.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0015-a-socket-runs-a-bounded-number-of-operations.md")]
[TestFixture]
public class SocketOperationLimitTests
{
    private const string Adr = "docs/adr/0015-a-socket-runs-a-bounded-number-of-operations.md";
    private const string Subscription = "subscription { onTrainFailed { trainName } }";

    private static TraxCompositeSocketInterceptor Interceptor(int limit) =>
        new(new TraxApplicationServices(new ServiceCollection().BuildServiceProvider()), limit);

    /// <summary>True when the interceptor marked the operation as past the limit.</summary>
    private static async Task<bool> RefusedAsync(
        TraxCompositeSocketInterceptor interceptor,
        HotChocolate.AspNetCore.Subscriptions.ISocketSession session,
        string id
    )
    {
        var builder = OperationRequestBuilder.New().SetDocument(Subscription);
        await interceptor.OnRequestAsync(session, id, builder);
        var request = (OperationRequest)builder.Build();
        return request.ContextData?.ContainsKey(SocketOperationLimitRequestMiddleware.ExceededKey)
            == true;
    }

    #region The interceptor counts each connection's running operations

    [Test]
    public async Task PastTheLimit_IsRefused()
    {
        var interceptor = Interceptor(2);
        var (session, _) = NewSession();

        (await RefusedAsync(interceptor, session, "1")).Should().BeFalse();
        (await RefusedAsync(interceptor, session, "2")).Should().BeFalse();
        (await RefusedAsync(interceptor, session, "3"))
            .Should()
            .BeTrue("a connection runs at most the limit at once, per " + Adr);
    }

    [Test]
    public async Task CompletedOperation_FreesItsPlace()
    {
        var interceptor = Interceptor(2);
        var (session, _) = NewSession();
        await RefusedAsync(interceptor, session, "1");
        await RefusedAsync(interceptor, session, "2");

        await interceptor.OnCompleteAsync(session, "1");

        (await RefusedAsync(interceptor, session, "3"))
            .Should()
            .BeFalse("a completed operation frees its place, per " + Adr);
    }

    [Test]
    public async Task Limit_IsPerConnection()
    {
        var interceptor = Interceptor(1);
        var (first, _) = NewSession();
        var (second, _) = NewSession();
        await RefusedAsync(interceptor, first, "1");

        (await RefusedAsync(interceptor, second, "1"))
            .Should()
            .BeFalse("the limit is counted per connection, per " + Adr);
    }

    [Test]
    public async Task RefusedOperation_TakesNoPlace()
    {
        var interceptor = Interceptor(1);
        var (session, _) = NewSession();
        await RefusedAsync(interceptor, session, "1");
        (await RefusedAsync(interceptor, session, "2")).Should().BeTrue();

        // The refused operation completes too; it held no place, so it frees none.
        await interceptor.OnCompleteAsync(session, "2");
        (await RefusedAsync(interceptor, session, "3")).Should().BeTrue();

        await interceptor.OnCompleteAsync(session, "1");
        (await RefusedAsync(interceptor, session, "4"))
            .Should()
            .BeFalse("only the admitted operation held a place, per " + Adr);
    }

    [Test]
    public async Task PublicConstructor_DefaultsTo100()
    {
        var interceptor = new TraxCompositeSocketInterceptor(
            new TraxApplicationServices(new ServiceCollection().BuildServiceProvider())
        );
        var (session, _) = NewSession();

        for (var i = 0; i < 100; i++)
            (await RefusedAsync(interceptor, session, i.ToString())).Should().BeFalse();

        (await RefusedAsync(interceptor, session, "100")).Should().BeTrue();
    }

    #endregion

    #region The builder

    [Test]
    public void Builder_DefaultIs100()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.MaxOperationsPerConnectionValue.Should().Be(100);
    }

    [Test]
    public void Builder_Override_Stored()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        builder.MaxOperationsPerConnection(7);

        builder.MaxOperationsPerConnectionValue.Should().Be(7);
    }

    [Test]
    public void Builder_NonPositive_Throws()
    {
        var builder = new TraxGraphQLBuilder(new ServiceCollection());

        ((Action)(() => builder.MaxOperationsPerConnection(0)))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
        ((Action)(() => builder.MaxOperationsPerConnection(-1)))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region Over a real socket

    [Test]
    public async Task ThirdSubscription_WithLimitTwo_GetsTheLimitError()
    {
        using var host = await StartHostAsync(limit: 2);
        using var ws = await ConnectAsync(host);
        await SendAsync(ws, new { type = "connection_init", payload = new { } });
        (await ReceiveAsync(ws)).GetProperty("type").GetString().Should().Be("connection_ack");

        foreach (var id in new[] { "1", "2", "3" })
            await SendAsync(
                ws,
                new
                {
                    id,
                    type = "subscribe",
                    payload = new { query = Subscription },
                }
            );

        var message = await ReceiveAsync(ws);

        message.GetProperty("id").GetString().Should().Be("3");
        message
            .GetRawText()
            .Should()
            .Contain(
                SocketOperationLimitRequestMiddleware.ErrorCode,
                "the operation past the limit is refused with a coded error, per " + Adr
            );
    }

    #endregion

    private static async Task<IHost> StartHostAsync(int limit)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddSingleton<TraxMarker>();
                        var discovery = Substitute.For<ITrainDiscoveryService>();
                        discovery.DiscoverTrains().Returns([]);
                        services.AddSingleton(discovery);
                        services.AddSingleton(Substitute.For<IEffectRegistry>());
                        services.AddTraxGraphQL(graphql =>
                            graphql
                                .ExposeOperationQueries()
                                .AllowAnonymousOperations()
                                .MaxOperationsPerConnection(limit)
                        );
                        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
                        services.AddScoped(_ => Substitute.For<IOperationsService>());
                        services.AddScoped(_ =>
                            Substitute.For<Trax.Mediator.Services.TrainExecution.ITrainExecutionService>()
                        );
                        services.AddScoped(_ =>
                            Substitute.For<Trax.Scheduler.Services.TraxScheduler.ITraxScheduler>()
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .Build();

        await host.StartAsync();
        return host;
    }

    private static async Task<WebSocket> ConnectAsync(IHost host)
    {
        var client = host.GetTestServer().CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        return await client.ConnectClosingAsync(new Uri("ws://localhost/trax/graphql"), default);
    }

    private static Task SendAsync(WebSocket ws, object message) =>
        ws.SendAsync(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message)),
            WebSocketMessageType.Text,
            true,
            default
        );

    /// <summary>The next message other than a ping or pong.</summary>
    private static async Task<JsonElement> ReceiveAsync(WebSocket ws)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[16384];
        while (true)
        {
            var received = await ws.ReceiveAsync(buffer, cts.Token);
            if (received.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException(
                    "The server closed the socket: " + received.CloseStatusDescription
                );
            if (received.Count == 0)
                continue;
            var root = JsonDocument
                .Parse(Encoding.UTF8.GetString(buffer, 0, received.Count))
                .RootElement.Clone();
            if (root.GetProperty("type").GetString() is "ping" or "pong")
                continue;
            return root;
        }
    }
}
