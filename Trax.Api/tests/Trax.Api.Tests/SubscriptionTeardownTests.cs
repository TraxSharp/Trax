using System.Net.WebSockets;
using System.Text;
using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// A subscription's topic registration ends with the subscription, without waiting for the next
/// event on its topic: when the socket that carried it goes away, closed or aborted, and when its
/// response stream is disposed in-process. A quiet topic would otherwise hold a buffer for every
/// subscriber that has left.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SubscriptionTeardownTests
{
    private const string Subscription = "subscription { onDataChanged { domain } }";

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Test]
    public async Task AbortedSocket_EndsItsTopicSubscription_WithoutAnEvent()
    {
        var topics = new CountingTopicReceiver.Counter();
        await using var app = await StartKestrelAsync(topics);

        using var client = await SubscribeOverSocketAsync(app);
        (await WaitForAsync(() => topics.Open == 1))
            .Should()
            .BeTrue("the subscription registers on its topic");

        // Drops the TCP connection without a close frame, as a client that crashes or loses its
        // network does.
        client.Abort();

        (await WaitForAsync(() => topics.Open == 0))
            .Should()
            .BeTrue(
                "an aborted socket ends its subscription without waiting for an event on the topic"
            );
    }

    [Test]
    public async Task ClosedSocket_EndsItsTopicSubscription()
    {
        var topics = new CountingTopicReceiver.Counter();
        await using var app = await StartKestrelAsync(topics);

        using var client = await SubscribeOverSocketAsync(app);
        (await WaitForAsync(() => topics.Open == 1)).Should().BeTrue();

        using var cts = new CancellationTokenSource(Deadline);
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token);

        (await WaitForAsync(() => topics.Open == 0)).Should().BeTrue();
    }

    [Test]
    public async Task DisposedResponseStream_EndsAnEnumerationAlreadyRunning()
    {
        var topics = new CountingTopicReceiver.Counter();
        var services = HostServices(new ServiceCollection(), topics);
        await using var provider = services.BuildServiceProvider();
        var executor = await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        var stream = (await executor.ExecuteAsync(Subscription)).ExpectResponseStream();
        var reader = Task.Run(async () =>
        {
            await foreach (var _ in stream.ReadResultsAsync()) { }
        });
        (await WaitForAsync(() => topics.Open == 1)).Should().BeTrue();

        await stream.DisposeAsync();

        (await WaitForAsync(() => topics.Open == 0))
            .Should()
            .BeTrue("disposing the response stream unsubscribes from the topic");
        // allowed-delay: an upper bound on the reader, which wins the race as soon as it ends.
        (await Task.WhenAny(reader, Task.Delay(Deadline)))
            .Should()
            .BeSameAs(reader, "the running enumeration ends with its subscription");
    }

    [TestCase("onTrainStarted", "OnTrainStarted")]
    [TestCase("onTrainCompleted", "OnTrainCompleted")]
    [TestCase("onTrainFailed", "OnTrainFailed")]
    [TestCase("onTrainCancelled", "OnTrainCancelled")]
    [TestCase("onTrainStateChanged", "OnTrainStateChanged")]
    public async Task LifecycleField_DeliversItsTopic_AndEndsWithItsResponseStream(
        string field,
        string topic
    )
    {
        var topics = new CountingTopicReceiver.Counter();
        await using var provider = HostServices(new ServiceCollection(), topics)
            .BuildServiceProvider();
        var executor = await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        var stream = (
            await executor.ExecuteAsync($"subscription {{ {field} {{ trainName sequence }} }}")
        ).ExpectResponseStream();
        var first = new TaskCompletionSource<HotChocolate.Execution.OperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var reader = Task.Run(async () =>
        {
            await foreach (var item in stream.ReadResultsAsync())
                first.TrySetResult(item);
        });
        (await WaitForAsync(() => topics.Open == 1)).Should().BeTrue();

        await LifecycleEventPublisher
            .For(provider.GetRequiredService<ITopicEventSender>())
            .PublishAsync(
                topic,
                new TrainLifecycleEvent(
                    1,
                    "ext",
                    "Some.Train",
                    TrainState.Completed,
                    DateTime.UtcNow,
                    null,
                    null,
                    null
                ),
                default
            );
        // allowed-delay: an upper bound on the delivery, which wins the race as soon as it lands.
        (await Task.WhenAny(first.Task, Task.Delay(Deadline)))
            .Should()
            .BeSameAs(first.Task);
        var data = (IReadOnlyDictionary<string, object?>)(await first.Task).DataMap()[field]!;
        data["trainName"].Should().Be("Some.Train");
        data["sequence"].Should().Be(1L);

        await stream.DisposeAsync();
        (await WaitForAsync(() => topics.Open == 0)).Should().BeTrue();
        // allowed-delay: an upper bound on the reader, which wins the race as soon as it ends.
        (await Task.WhenAny(reader, Task.Delay(Deadline)))
            .Should()
            .BeSameAs(reader);
    }

    [Test]
    public async Task SubscribeCancelledAfterRegistering_ReleasesItsTopicRegistration()
    {
        var topics = new CountingTopicReceiver.Counter();
        await using var provider = HostServices(new ServiceCollection(), topics)
            .BuildServiceProvider();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // The receiver registers on the topic whatever the token says, so the cancellation lands
        // after the registration, while the subscription reads its baseline.
        var receiver = new CountingTopicReceiver(
            new IgnoresCancellation(provider.GetRequiredService<ITopicEventReceiver>()),
            topics
        );
        var subscribe = () =>
            new LifecycleSubscriptions()
                .SubscribeToTrainStarted(
                    receiver,
                    provider.GetRequiredService<ITopicEventSender>(),
                    provider.GetRequiredService<LifecycleSubscriptionAccess>(),
                    user: null,
                    cancelled.Token
                )
                .AsTask();

        await subscribe.Should().ThrowAsync<OperationCanceledException>();
        topics.Open.Should().Be(0, "a subscription that never started holds no registration");
    }

    [Test]
    public async Task JunctionEventField_EndsItsTopicRegistrationWithItsResponseStream()
    {
        var topics = new CountingTopicReceiver.Counter();
        await using var provider = HostServices(new ServiceCollection(), topics)
            .BuildServiceProvider();
        var executor = await provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");

        var stream = (
            await executor.ExecuteAsync(
                "subscription { onJunctionEvent(metadataId: 1) { trainName sequence } }"
            )
        ).ExpectResponseStream();
        var reader = Task.Run(async () =>
        {
            await foreach (var _ in stream.ReadResultsAsync()) { }
        });
        (await WaitForAsync(() => topics.Open == 1))
            .Should()
            .BeTrue("the step feed registers on the junction event topic");

        await stream.DisposeAsync();

        (await WaitForAsync(() => topics.Open == 0))
            .Should()
            .BeTrue("disposing the step feed's response stream unsubscribes from its topic");
        // allowed-delay: an upper bound on the reader, which wins the race as soon as it ends.
        (await Task.WhenAny(reader, Task.Delay(Deadline)))
            .Should()
            .BeSameAs(reader, "the running enumeration ends with its subscription");
    }

    [Test]
    public async Task JunctionEventSubscribeCancelledAfterRegistering_ReleasesItsTopicRegistration()
    {
        var topics = new CountingTopicReceiver.Counter();
        await using var provider = HostServices(new ServiceCollection(), topics)
            .BuildServiceProvider();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var receiver = new CountingTopicReceiver(
            new IgnoresCancellation(provider.GetRequiredService<ITopicEventReceiver>()),
            topics
        );
        var subscribe = () =>
            new LifecycleSubscriptions()
                .SubscribeToJunctionEvent(
                    1,
                    receiver,
                    provider.GetRequiredService<ITopicEventSender>(),
                    provider.GetRequiredService<LifecycleSubscriptionAccess>(),
                    user: null,
                    cancelled.Token
                )
                .AsTask();

        await subscribe.Should().ThrowAsync<OperationCanceledException>();
        topics.Open.Should().Be(0, "a step feed that never started holds no registration");
    }

    private sealed class IgnoresCancellation(ITopicEventReceiver inner) : ITopicEventReceiver
    {
        public ValueTask<ISourceStream<TMessage>> SubscribeAsync<TMessage>(
            string topicName,
            CancellationToken cancellationToken = default
        ) => inner.SubscribeAsync<TMessage>(topicName, CancellationToken.None);

        public ValueTask<ISourceStream<TMessage>> SubscribeAsync<TMessage>(
            string topicName,
            int? bufferCapacity,
            TopicBufferFullMode? bufferFullMode,
            CancellationToken cancellationToken = default
        ) =>
            inner.SubscribeAsync<TMessage>(
                topicName,
                bufferCapacity,
                bufferFullMode,
                CancellationToken.None
            );
    }

    private static IServiceCollection HostServices(
        IServiceCollection services,
        CountingTopicReceiver.Counter topics
    )
    {
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<ITrainDiscoveryService>());
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton(Substitute.For<ITraxScheduler>());
        services.AddSingleton(Substitute.For<ITraxHealthService>());
        services.AddSingleton(Substitute.For<IOperationsService>());
        services.AddSingleton(Substitute.For<ITrainExecutionService>());
        services.AddTraxGraphQL(g => g.ExposeOperationQueries().AllowAnonymousOperations());

        // Count the topic registrations each subscription opens and closes, around the
        // receiver HotChocolate registered.
        var original = services.Single(d => d.ServiceType == typeof(ITopicEventReceiver));
        services.Remove(original);
        services.AddSingleton<ITopicEventReceiver>(sp => new CountingTopicReceiver(
            (ITopicEventReceiver)original.ImplementationFactory!(sp),
            topics
        ));
        return services;
    }

    private static async Task<WebApplication> StartKestrelAsync(
        CountingTopicReceiver.Counter topics
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        HostServices(builder.Services, topics);
        var app = builder.Build();
        app.UseRouting();
        app.UseTraxGraphQL();
        await app.StartAsync();
        return app;
    }

    private static async Task<ClientWebSocket> SubscribeOverSocketAsync(WebApplication app)
    {
        var address = new Uri(
            app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First()
        );
        var client = new ClientWebSocket();
        client.Options.AddSubProtocol("graphql-transport-ws");
        using var cts = new CancellationTokenSource(Deadline);
        await client.ConnectAsync(
            new Uri($"ws://{address.Host}:{address.Port}/trax/graphql"),
            cts.Token
        );

        await SendAsync(client, """{"type":"connection_init","payload":{}}""", cts.Token);
        var buffer = new byte[4096];
        var ack = await client.ReceiveAsync(buffer, cts.Token);
        Encoding.UTF8.GetString(buffer, 0, ack.Count).Should().Contain("connection_ack");

        await SendAsync(
            client,
            $$$"""{"id":"1","type":"subscribe","payload":{"query":"{{{Subscription}}}"}}""",
            cts.Token
        );
        return client;
    }

    private static Task SendAsync(ClientWebSocket client, string message, CancellationToken ct) =>
        client.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, ct);

    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Deadline;
        while (!condition())
        {
            if (DateTime.UtcNow > until)
                return false;
            // allowed-delay: poll interval for a condition, bounded by Deadline.
            await Task.Delay(20);
        }
        return true;
    }

    /// <summary>
    /// Wraps the topic receiver and counts the topic registrations that are open: one per
    /// subscribe, released when its source stream is disposed.
    /// </summary>
    private sealed class CountingTopicReceiver(
        ITopicEventReceiver inner,
        CountingTopicReceiver.Counter counter
    ) : ITopicEventReceiver
    {
        public sealed class Counter
        {
            private int _open;

            public int Open => Volatile.Read(ref _open);

            public void Opened() => Interlocked.Increment(ref _open);

            public void Closed() => Interlocked.Decrement(ref _open);
        }

        public ValueTask<ISourceStream<TMessage>> SubscribeAsync<TMessage>(
            string topicName,
            CancellationToken cancellationToken = default
        ) => SubscribeAsync<TMessage>(topicName, null, null, cancellationToken);

        public async ValueTask<ISourceStream<TMessage>> SubscribeAsync<TMessage>(
            string topicName,
            int? bufferCapacity,
            TopicBufferFullMode? bufferFullMode,
            CancellationToken cancellationToken = default
        )
        {
            var stream = await inner.SubscribeAsync<TMessage>(
                topicName,
                bufferCapacity,
                bufferFullMode,
                cancellationToken
            );
            counter.Opened();
            return new Counted<TMessage>(stream, counter);
        }

        private sealed class Counted<TMessage>(ISourceStream<TMessage> inner, Counter counter)
            : ISourceStream<TMessage>
        {
            private int _disposed;

            public IAsyncEnumerable<TMessage> ReadEventsAsync() => inner.ReadEventsAsync();

            IAsyncEnumerable<object?> ISourceStream.ReadEventsAsync() =>
                ((ISourceStream)inner).ReadEventsAsync();

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                    counter.Closed();
                return inner.DisposeAsync();
            }
        }
    }
}
