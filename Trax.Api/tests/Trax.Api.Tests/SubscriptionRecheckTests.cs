using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.Tests.Auth;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Enums;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;
using OperationResult = HotChocolate.Execution.OperationResult;

namespace Trax.Api.Tests;

/// <summary>
/// A subscription's authorization is decided when it subscribes and again at every connection
/// re-check interval while it runs. A lifecycle, junction, data-change or operator subscription
/// whose subscriber no longer passes ends with a <c>TRAX_AUTHORIZATION</c> error; one that still
/// passes goes on.
///
/// <para>Enforces <c>docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md")]
[TestFixture]
public class SubscriptionRecheckTests
{
    private const string Adr =
        "docs/adr/0011-subscriptions-carry-the-authorization-of-the-data-they-stream.md";

    private const string OpsPolicy = "Ops";
    private const string MembersPolicy = "Members";
    private const string AuthorizationCode = "TRAX_AUTHORIZATION";

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private const string LifecycleQuery = "subscription { onTrainCompleted { trainName } }";
    private const string JunctionQuery =
        "subscription { onJunctionEvent(metadataId: 42) { sequence } }";
    private const string DataChangedQuery = "subscription { onDataChanged { domain } }";
    private const string OperatorQuery =
        "subscription { onMachineInstanceChanged { rowId state } }";

    public interface IMembersTrain;

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private ManualTimeProvider _time = new(Start);
    private ServiceProvider? _provider;
    private volatile bool _opsOpen = true;
    private volatile bool _membersOpen = true;
    private volatile bool _opsThrows;

    [SetUp]
    public void Admit()
    {
        _time = new ManualTimeProvider(Start);
        _opsOpen = true;
        _membersOpen = true;
        _opsThrows = false;
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        _provider = null;
    }

    #region Lifecycle

    [Test]
    public async Task Lifecycle_operations_view_ends_when_the_gate_no_longer_passes()
    {
        var executor = await BuildAsync(OperationsByPolicy);
        var stream = await SubscribeAsync(executor, LifecycleQuery);
        var results = Collect(stream);
        await FirstAsync(results, () => PublishCompleted("Some.Train"));

        _opsOpen = false;

        (await EndAsync(results))
            .Should()
            .Be(
                AuthorizationCode,
                "the operations gate is asked again at the re-check, per " + Adr
            );
    }

    [Test]
    public async Task Lifecycle_operations_view_goes_on_while_the_gate_passes()
    {
        var executor = await BuildAsync(OperationsByPolicy);
        var stream = await SubscribeAsync(executor, LifecycleQuery);
        var results = Collect(stream);
        await FirstAsync(results, () => PublishCompleted("Some.Train"));

        for (var i = 0; i < 3; i++)
            _time.Advance(Interval);

        var before = results.Received;
        await FirstAsync(results, () => PublishCompleted("Some.Later.Train"), after: before);
        results.Ended.Should().BeFalse("a subscriber that still passes keeps receiving");
    }

    [Test]
    public async Task Lifecycle_ends_when_the_check_throws()
    {
        var executor = await BuildAsync(OperationsByPolicy);
        var stream = await SubscribeAsync(executor, LifecycleQuery);
        var results = Collect(stream);
        await FirstAsync(results, () => PublishCompleted("Some.Train"));

        _opsThrows = true;

        (await EndAsync(results))
            .Should()
            .Be(AuthorizationCode, "a check that cannot answer does not admit, per " + Adr);
    }

    [Test]
    public async Task Lifecycle_broadcast_view_ends_when_a_train_it_was_admitted_to_is_withdrawn()
    {
        var executor = await BuildAsync(g => g, MembersBroadcast());
        var stream = await SubscribeAsync(executor, LifecycleQuery);
        var results = Collect(stream);
        await FirstAsync(results, () => PublishCompleted(typeof(IMembersTrain).FullName!));

        _membersOpen = false;

        (await EndAsync(results))
            .Should()
            .Be(
                AuthorizationCode,
                "a broadcast train's posture is asked again at the re-check, per " + Adr
            );
    }

    #endregion

    #region Junction events

    [Test]
    public async Task Junction_events_end_when_the_gate_no_longer_passes()
    {
        var executor = await BuildAsync(OperationsByPolicy);
        var results = Collect(await SubscribeAsync(executor, JunctionQuery));

        _opsOpen = false;

        (await EndAsync(results)).Should().Be(AuthorizationCode, "per " + Adr);
    }

    [Test]
    public async Task Junction_events_in_the_broadcast_view_end_when_the_train_is_withdrawn()
    {
        var executor = await BuildAsync(g => g, MembersBroadcast());
        var results = Collect(await SubscribeAsync(executor, JunctionQuery));

        _membersOpen = false;

        (await EndAsync(results)).Should().Be(AuthorizationCode, "per " + Adr);
    }

    #endregion

    #region Data changes

    [Test]
    public async Task Data_changes_end_when_the_gate_no_longer_passes()
    {
        var executor = await BuildAsync(OperationsByPolicy);
        var stream = await SubscribeAsync(executor, DataChangedQuery);
        var results = Collect(stream);
        await FirstAsync(
            results,
            () =>
                Send(
                    nameof(LifecycleSubscriptions.OnDataChanged),
                    new DataChangedEvent(ChangeDomain.WorkQueue, DateTime.UtcNow)
                )
        );

        _opsOpen = false;

        (await EndAsync(results)).Should().Be(AuthorizationCode, "per " + Adr);
    }

    #endregion

    #region Operator machine feed

    [Test]
    public async Task Operator_machine_feed_ends_when_the_gate_no_longer_passes()
    {
        var feed = Channel.CreateUnbounded<MachineInstanceRecord>();
        var executor = await BuildAsync(OperationsByPolicy, operations: Operations(feed));
        var results = Collect(await SubscribeAsync(executor, OperatorQuery));
        await FirstAsync(results, () => feed.Writer.TryWrite(Record()));

        _opsOpen = false;

        (await EndAsync(results))
            .Should()
            .Be(AuthorizationCode, "the operator feed's gate is asked again, per " + Adr);
    }

    [Test]
    public async Task Operator_machine_feed_goes_on_while_the_gate_passes()
    {
        var feed = Channel.CreateUnbounded<MachineInstanceRecord>();
        var executor = await BuildAsync(OperationsByPolicy, operations: Operations(feed));
        var results = Collect(await SubscribeAsync(executor, OperatorQuery));
        await FirstAsync(results, () => feed.Writer.TryWrite(Record()));

        for (var i = 0; i < 3; i++)
            _time.Advance(Interval);

        var before = results.Received;
        await FirstAsync(results, () => feed.Writer.TryWrite(Record()), after: before);
        results.Ended.Should().BeFalse();
    }

    #endregion

    #region Over a socket

    [Test]
    public async Task Over_a_socket_the_subscription_ends_with_its_error_and_the_socket_stays_open()
    {
        using var host = await StartSocketHostAsync(MembersBroadcast());
        var client = host.GetTestServer().CreateWebSocketClient();
        client.SubProtocols.Add("graphql-transport-ws");
        using var socket = await client.ConnectClosingAsync(new Uri("ws://localhost/trax/graphql"));
        await SendAsync(
            socket,
            new { type = "connection_init", payload = new { apiKey = SocketKey } }
        );
        (await ReceiveAsync(socket)).GetProperty("type").GetString().Should().Be("connection_ack");
        await SendAsync(
            socket,
            new
            {
                id = "1",
                type = "subscribe",
                payload = new { query = LifecycleQuery },
            }
        );

        _membersOpen = false;

        var frames = new List<JsonElement>();
        while (frames.Count == 0 || frames[^1].GetProperty("type").GetString() == "next")
            frames.Add(await ReceiveAsync(socket));
        var raw = string.Join("\n", frames.Select(f => f.GetRawText()));
        frames.Should().ContainSingle(raw);
        frames[0].GetProperty("type").GetString().Should().Be("error", raw);
        frames[0]
            .GetProperty("payload")
            .EnumerateArray()
            .Single()
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be(AuthorizationCode, "per " + Adr);

        await SendAsync(socket, new { type = "ping" });
        (await ReceiveAsync(socket))
            .GetProperty("type")
            .GetString()
            .Should()
            .Be("pong", "the socket stays open");
    }

    private const string SocketKey = "recheck-socket-key-0123456789abcdefghijk";

    private async Task<IHost> StartSocketHostAsync(TrainRegistration broadcast)
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([broadcast]);
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddRouting();
                        services.AddSingleton<TraxMarker>();
                        services.AddSingleton(discovery);
                        services.AddSingleton(Substitute.For<IEffectRegistry>());
                        services.AddTraxApiKeyAuth(keys => keys.Add(SocketKey, id: "member"));
                        services.AddAuthorization(o =>
                            o.AddPolicy(MembersPolicy, p => p.RequireAssertion(_ => _membersOpen))
                        );
                        services.AddDbContext<OrderTestDbContext>(o =>
                            o.UseInMemoryDatabase("SubscriptionRecheckSocket_" + Guid.NewGuid())
                        );
                        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
                            services,
                            g =>
                                g.AddDbContext<OrderTestDbContext>()
                                    .RecheckConnectionCredentialsEvery(TimeSpan.FromSeconds(1))
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(e => e.MapGraphQL("/trax/graphql", "trax"));
                    })
            )
            .Build();
        await host.StartAsync();
        return host;
    }

    private static async Task SendAsync(WebSocket socket, object message)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.SendAsync(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message)),
            WebSocketMessageType.Text,
            true,
            cts.Token
        );
    }

    private static async Task<JsonElement> ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[8192];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var text = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException(
                    $"The socket closed: {result.CloseStatus} {result.CloseStatusDescription}"
                );
            text.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }
        return JsonDocument.Parse(text.ToArray()).RootElement.Clone();
    }

    #endregion

    #region Host

    private static TraxGraphQLBuilder OperationsByPolicy(TraxGraphQLBuilder g) =>
        g.ExposeOperationQueries().GateOperations(policy: OpsPolicy);

    private async Task<IRequestExecutor> BuildAsync(
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder> configure,
        TrainRegistration? broadcast = null,
        IOperationsService? operations = null
    )
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns(broadcast is null ? [] : [broadcast]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton<TimeProvider>(_time);
        if (operations is not null)
            services.AddSingleton(operations);
        services.AddAuthorizationCore(o =>
        {
            o.AddPolicy(
                OpsPolicy,
                p =>
                    p.RequireAssertion(_ =>
                        _opsThrows
                            ? throw new InvalidOperationException("The policy store is down.")
                            : _opsOpen
                    )
            );
            o.AddPolicy(MembersPolicy, p => p.RequireAssertion(_ => _membersOpen));
        });
        services.AddDbContext<OrderTestDbContext>(o =>
            o.UseInMemoryDatabase("SubscriptionRecheck_" + Guid.NewGuid())
        );
        Trax.Api.GraphQL.Extensions.GraphQLServiceExtensions.AddTraxGraphQL(
            services,
            g =>
                configure(
                    g.AddDbContext<OrderTestDbContext>().RecheckConnectionCredentialsEvery(Interval)
                )
        );

        _provider = services.BuildServiceProvider();
        return await _provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
    }

    private static TrainRegistration MembersBroadcast() =>
        new()
        {
            ServiceType = typeof(IMembersTrain),
            ImplementationType = typeof(IMembersTrain),
            InputType = typeof(object),
            OutputType = typeof(object),
            Lifetime = ServiceLifetime.Transient,
            ServiceTypeName = typeof(IMembersTrain).FullName!,
            ImplementationTypeName = typeof(IMembersTrain).FullName!,
            InputTypeName = "Object",
            OutputTypeName = "Object",
            RequiredPolicies = [MembersPolicy],
            RequiredRoles = [],
            HasAuthorizeAttribute = true,
            HasAllowAnonymousAttribute = false,
            IsQuery = false,
            IsMutation = false,
            IsBroadcastEnabled = true,
            GraphQLOperations = GraphQLOperation.Run,
            IsRemote = false,
        };

    private static IOperationsService Operations(Channel<MachineInstanceRecord> feed)
    {
        var operations = Substitute.For<IOperationsService>();
        operations
            .WatchMachineInstancesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => Read(feed, call.ArgAt<CancellationToken>(1)));
        return operations;
    }

    private static async IAsyncEnumerable<MachineInstanceRecord> Read(
        Channel<MachineInstanceRecord> feed,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        await foreach (var record in feed.Reader.ReadAllAsync(ct))
            yield return record;
    }

    private static MachineInstanceRecord Record() =>
        new(
            RowId: 1,
            Machine: "wizard",
            OwnerKind: SnapshotOwnerKind.User,
            Id: Guid.NewGuid(),
            State: "Naming",
            Version: 1,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            HasLiveInvokedRun: false
        );

    private void PublishCompleted(string trainName) =>
        Send(
            nameof(LifecycleSubscriptions.OnTrainCompleted),
            new TrainLifecycleEvent(
                MetadataId: 1,
                ExternalId: "ext",
                TrainName: trainName,
                TrainState: TrainState.Completed,
                Timestamp: DateTime.UtcNow,
                FailureJunction: null,
                FailureReason: null,
                Output: null,
                HostName: "worker-1",
                HostEnvironment: "Production"
            )
        );

    private void Send<T>(string topic, T message) =>
        _provider!
            .GetRequiredService<ITopicEventSender>()
            .SendAsync(topic, message)
            .AsTask()
            .GetAwaiter()
            .GetResult();

    private static async Task<IResponseStream> SubscribeAsync(
        IRequestExecutor executor,
        string query
    )
    {
        var user = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "member")], "Test")
        );
        var result = await executor.ExecuteAsync(
            OperationRequestBuilder
                .New()
                .SetDocument(query)
                .SetGlobalState("ClaimsPrincipal", user)
                .Build()
        );
        return result
            .Should()
            .BeAssignableTo<IResponseStream>(
                "the subscriber passes when it subscribes: {0}",
                (result as OperationResult)?.ToJson()
            )
            .Which;
    }

    #endregion

    #region Reading

    /// <summary>Every result of a subscription, read in the background until it ends.</summary>
    private sealed class Results
    {
        private readonly List<OperationResult> _items = [];
        private readonly Lock _gate = new();

        public Task Reading { get; set; } = Task.CompletedTask;

        public bool Ended => Reading.IsCompleted;

        public int Received
        {
            get
            {
                lock (_gate)
                    return _items.Count;
            }
        }

        public IReadOnlyList<OperationResult> Items
        {
            get
            {
                lock (_gate)
                    return [.. _items];
            }
        }

        public void Add(OperationResult result)
        {
            lock (_gate)
                _items.Add(result);
        }
    }

    private static Results Collect(IResponseStream stream)
    {
        var results = new Results();
        results.Reading = Task.Run(async () =>
        {
            await using (stream)
                await foreach (var result in stream.ReadResultsAsync())
                    results.Add(result);
        });
        return results;
    }

    /// <summary>Publishes with <paramref name="publish"/> until a result past <paramref name="after"/> arrives.</summary>
    private static async Task FirstAsync(Results results, Action publish, int after = 0)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (results.Received <= after && !results.Ended && DateTime.UtcNow < deadline)
        {
            publish();
            // allowed-delay: re-publish interval, bounded by the 10s deadline; ExecuteAsync can
            // return before the topic subscription is registered.
            await Task.WhenAny(results.Reading, Task.Delay(100));
        }
        results.Received.Should().BeGreaterThan(after, "an event should reach the subscriber");
        results.Items[after].Errors.Should().BeNullOrEmpty();
    }

    /// <summary>
    /// Moves the clock on an interval at a time until the subscription ends, and returns the code
    /// of the error it ended with.
    /// </summary>
    private async Task<string?> EndAsync(Results results)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!results.Ended && DateTime.UtcNow < deadline)
        {
            _time.Advance(Interval);
            // allowed-delay: lets the re-check run, bounded by the 10s deadline; WhenAny wakes the
            // instant the subscription ends.
            await Task.WhenAny(results.Reading, Task.Delay(50));
        }

        results.Ended.Should().BeTrue("a subscriber that no longer passes is not kept");
        await results.Reading;
        results.Items.Should().NotBeEmpty();
        using var last = JsonDocument.Parse(results.Items[^1].ToJson());
        (
            !last.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind == JsonValueKind.Null
        )
            .Should()
            .BeTrue("the last result carries no event: " + last.RootElement.GetRawText());
        var raw = last.RootElement.GetRawText();
        last.RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeTrue("the subscription ends with an error: " + raw);
        return
            errors.EnumerateArray().Single().TryGetProperty("extensions", out var extensions)
            && extensions.TryGetProperty("code", out var code)
            ? code.GetString()
            : raw;
    }

    #endregion
}
