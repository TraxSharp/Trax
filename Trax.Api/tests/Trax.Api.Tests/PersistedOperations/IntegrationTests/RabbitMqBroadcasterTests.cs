using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.Tests.PersistedOperations.Fixtures;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

/// <summary>
/// Integration tests against a real RabbitMQ broker (the docker-compose
/// trax_rabbitmq container). Exercises the publish/receive path end to end.
/// The fixture publishes on an exchange of its own, so concurrent runs on one
/// broker never receive each other's messages.
/// </summary>
[TestFixture]
[Category("Integration")]
public class RabbitMqBroadcasterTests
{
    // Both the docker-compose dev broker (Trax.Samples/docker-compose.yml)
    // and the CI service container (.github/workflows/pull_request.yml) are
    // configured with these credentials. Default 'guest' would work locally
    // but RabbitMQ rejects it from non-localhost in CI's port-forwarded setup.
    private const string AmqpUri = "amqp://trax:trax123@localhost:5672/";

    private readonly string _exchange = RunExchange.New();

    [OneTimeTearDown]
    public Task OneTimeTearDown() => RunExchange.DeleteAsync(AmqpUri, _exchange);

    /// <summary>Options for this fixture's own exchange.</summary>
    private PersistedOperationsOptions OnRunExchange(PersistedOperationsOptions options)
    {
        options.RabbitMqExchange = _exchange;
        return options;
    }

    private static bool IsRabbitMqReachable()
    {
        try
        {
            var factory = new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(AmqpUri) };
            using var conn = factory.CreateConnectionAsync().GetAwaiter().GetResult();
            return conn.IsOpen;
        }
        catch
        {
            return false;
        }
    }

    [SetUp]
    public void SetUp()
    {
        if (!IsRabbitMqReachable())
            Assert.Ignore("RabbitMQ not reachable.");
    }

    [Test]
    public async Task PublishAsync_DeliversMessage_ToReceiverOnSameExchange()
    {
        var options = OnRunExchange(
            new PersistedOperationsBuilder()
                .WithInMemoryCache()
                .UseRabbitMqInvalidation(AmqpUri)
                .Build()
        );

        var receivedKey = $"test_{Guid.NewGuid():N}";
        var cache = new RecordingCache();

        await using var publisher = new RabbitMqPersistedOperationBroadcaster(
            options,
            NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
        );
        var receiver = new PersistedOperationReceiverService(
            options,
            cache,
            NoOpInvalidator(),
            NullLogger<PersistedOperationReceiverService>.Instance
        );

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await receiver.StartAsync(cts.Token);
        try
        {
            await publisher.PublishAsync(
                new PersistedOperationChangedMessage(
                    null,
                    receivedKey,
                    PersistedOperationChangeType.Upsert,
                    DateTime.UtcNow
                ),
                cts.Token
            );

            // Poll for the invalidation. The receiver runs on its own queue
            // bound to the shared fanout; it sees every message but we only
            // assert on the one matching our unique id.
            var saw = await WaitUntilAsync(
                () => cache.Invalidations.Any(p => p.Id == receivedKey),
                TimeSpan.FromSeconds(10)
            );
            saw.Should().BeTrue("the receiver should observe the published invalidation");
        }
        finally
        {
            await receiver.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task PublishAsync_CompletesOnlyOnceTheBrokerHasConfirmedIt()
    {
        var options = OnRunExchange(
            new PersistedOperationsBuilder().UseRabbitMqInvalidation(AmqpUri).Build()
        );
        await using var publisher = new RabbitMqPersistedOperationBroadcaster(
            options,
            NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
        );

        await publisher.PublishAsync(
            new PersistedOperationChangedMessage(
                null,
                $"confirm_{Guid.NewGuid():N}",
                PersistedOperationChangeType.Upsert,
                DateTime.UtcNow
            ),
            CancellationToken.None
        );

        // A channel numbers its publishes only in publisher-confirm mode; otherwise this is 0.
        (await publisher.Channel!.GetNextPublishSequenceNumberAsync())
            .Should()
            .BeGreaterThan(1UL);
    }

    [Test]
    public async Task PublishAsync_AfterItsChannelClosed_OpensAnotherOnTheSameConnection()
    {
        var options = OnRunExchange(
            new PersistedOperationsBuilder().UseRabbitMqInvalidation(AmqpUri).Build()
        );
        await using var publisher = new RabbitMqPersistedOperationBroadcaster(
            options,
            NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
        );
        var message = new PersistedOperationChangedMessage(
            null,
            $"reopen_{Guid.NewGuid():N}",
            PersistedOperationChangeType.Upsert,
            DateTime.UtcNow
        );
        await publisher.PublishAsync(message, CancellationToken.None);
        var first = publisher.Channel!;
        await first.CloseAsync();

        await publisher.PublishAsync(message, CancellationToken.None);

        publisher.Channel.Should().NotBeSameAs(first);
        publisher.Channel!.IsOpen.Should().BeTrue();
    }

    [Test]
    public async Task PublishAsync_WhenTheBrokerCannotBeReached_Throws()
    {
        var options = new PersistedOperationsBuilder()
            .UseRabbitMqInvalidation("amqp://trax:trax123@localhost:1/")
            .Build();
        await using var publisher = new RabbitMqPersistedOperationBroadcaster(
            options,
            NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
        );

        var act = () =>
            publisher.PublishAsync(
                new PersistedOperationChangedMessage(
                    null,
                    "unreachable",
                    PersistedOperationChangeType.Upsert,
                    DateTime.UtcNow
                ),
                CancellationToken.None
            );

        await act.Should().ThrowAsync<Exception>();
    }

    [Test]
    public async Task TwoReceivers_BothObserveSameMessage()
    {
        var options = OnRunExchange(
            new PersistedOperationsBuilder()
                .WithInMemoryCache()
                .UseRabbitMqInvalidation(AmqpUri)
                .Build()
        );

        var key = $"twonodes_{Guid.NewGuid():N}";
        var cacheA = new RecordingCache();
        var cacheB = new RecordingCache();

        await using var publisher = new RabbitMqPersistedOperationBroadcaster(
            options,
            NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
        );
        var receiverA = new PersistedOperationReceiverService(
            options,
            cacheA,
            NoOpInvalidator(),
            NullLogger<PersistedOperationReceiverService>.Instance
        );
        var receiverB = new PersistedOperationReceiverService(
            options,
            cacheB,
            NoOpInvalidator(),
            NullLogger<PersistedOperationReceiverService>.Instance
        );

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await receiverA.StartAsync(cts.Token);
        await receiverB.StartAsync(cts.Token);
        try
        {
            await publisher.PublishAsync(
                new PersistedOperationChangedMessage(
                    null,
                    key,
                    PersistedOperationChangeType.Upsert,
                    DateTime.UtcNow
                ),
                cts.Token
            );

            var sawA = await WaitUntilAsync(
                () => cacheA.Invalidations.Any(p => p.Id == key),
                TimeSpan.FromSeconds(10)
            );
            var sawB = await WaitUntilAsync(
                () => cacheB.Invalidations.Any(p => p.Id == key),
                TimeSpan.FromSeconds(10)
            );

            sawA.Should().BeTrue("receiver A should see the broadcast");
            sawB.Should().BeTrue("receiver B should see the broadcast");
        }
        finally
        {
            await receiverA.StopAsync(CancellationToken.None);
            await receiverB.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task ReceiverService_StoppedReceiver_DoesNotInvalidateCache()
    {
        // After Stop, a published message must NOT reach the receiver's cache.
        // This proves StopAsync actually unbinds the consumer rather than
        // leaving it silently running.
        var options = OnRunExchange(
            new PersistedOperationsBuilder()
                .WithInMemoryCache()
                .UseRabbitMqInvalidation(AmqpUri)
                .Build()
        );
        var cache = new RecordingCache();
        var svc = new PersistedOperationReceiverService(
            options,
            cache,
            NoOpInvalidator(),
            NullLogger<PersistedOperationReceiverService>.Instance
        );

        await svc.StartAsync(CancellationToken.None);
        await svc.StopAsync(CancellationToken.None);

        await using var publisher = new RabbitMqPersistedOperationBroadcaster(
            options,
            NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
        );
        var key = $"after_stop_{Guid.NewGuid():N}";
        await publisher.PublishAsync(
            new PersistedOperationChangedMessage(
                null,
                key,
                PersistedOperationChangeType.Upsert,
                DateTime.UtcNow
            ),
            CancellationToken.None
        );

        // Wait briefly to make sure no message arrives.
        await Task.Delay(500);
        cache
            .Invalidations.Should()
            .NotContain(p => p.Id == key, "stopped receiver must not invalidate");

        await svc.DisposeAsync();
        // Idempotent stop after dispose must not throw.
        await svc.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task AReceiverWhoseChannelTheBrokerCloses_EmptiesItsCaches_AndKeepsReceiving()
    {
        var options = OnRunExchange(
            new PersistedOperationsBuilder()
                .WithInMemoryCache()
                .UseRabbitMqInvalidation(AmqpUri)
                .Build()
        );
        var cache = new RecordingCache();
        var generation = new PersistedOperationCacheGeneration();
        var svc = new PersistedOperationReceiverService(
            options,
            cache,
            new HotChocolateOperationCacheInvalidator(
                new ServiceCollection().BuildServiceProvider(),
                generation,
                NullLogger<HotChocolateOperationCacheInvalidator>.Instance
            ),
            NullLogger<PersistedOperationReceiverService>.Instance
        );
        await using var publisher = new RabbitMqPersistedOperationBroadcaster(
            options,
            NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
        );
        await svc.StartAsync(CancellationToken.None);
        try
        {
            var before = generation.Current;

            // Acknowledging a delivery the channel never had is a channel-level error: the broker
            // closes that channel and leaves the connection up.
            await svc.Channel!.BasicAckAsync(deliveryTag: 9_999, multiple: false);

            (await WaitUntilAsync(() => generation.Current > before, TimeSpan.FromSeconds(10)))
                .Should()
                .BeTrue("a receiver that lost its channel may have missed a broadcast");

            var id = $"after_channel_close_{Guid.NewGuid():N}";
            var received = await WaitUntilAsync(
                () =>
                {
                    publisher
                        .PublishAsync(
                            new PersistedOperationChangedMessage(
                                null,
                                id,
                                PersistedOperationChangeType.Upsert,
                                DateTime.UtcNow
                            ),
                            CancellationToken.None
                        )
                        .GetAwaiter()
                        .GetResult();
                    return cache.Invalidations.Any(p => p.Id == id);
                },
                TimeSpan.FromSeconds(15)
            );
            received.Should().BeTrue("the receiver subscribes again on a new channel");
        }
        finally
        {
            await svc.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task ReceiverService_DisposeWithoutStop_ReleasesResourcesIdempotently()
    {
        var options = OnRunExchange(
            new PersistedOperationsBuilder()
                .WithInMemoryCache()
                .UseRabbitMqInvalidation(AmqpUri)
                .Build()
        );
        var svc = new PersistedOperationReceiverService(
            options,
            new RecordingCache(),
            NoOpInvalidator(),
            NullLogger<PersistedOperationReceiverService>.Instance
        );
        await svc.StartAsync(CancellationToken.None);

        // First dispose tears down the AMQP channel + connection.
        await svc.DisposeAsync();
        // Second dispose must be idempotent (the framework may call it twice).
        var secondDispose = svc.DisposeAsync();
        secondDispose.IsCompleted.Should().BeTrue("dispose must be idempotent");
        await secondDispose;
    }

    [Test]
    public async Task ReceiverService_MalformedMessage_NacksAndContinues()
    {
        // Publish a non-JSON byte payload directly to the exchange. The
        // receiver's OnMessageAsync should fail to deserialize, log, and
        // nack without crashing the host.
        var options = OnRunExchange(
            new PersistedOperationsBuilder()
                .WithInMemoryCache()
                .UseRabbitMqInvalidation(AmqpUri)
                .Build()
        );
        var cache = new RecordingCache();
        var svc = new PersistedOperationReceiverService(
            options,
            cache,
            NoOpInvalidator(),
            NullLogger<PersistedOperationReceiverService>.Instance
        );
        await svc.StartAsync(CancellationToken.None);

        var factory = new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(AmqpUri) };
        await using var conn = await factory.CreateConnectionAsync();
        await using var channel = await conn.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(
            exchange: _exchange,
            type: RabbitMQ.Client.ExchangeType.Fanout,
            durable: true,
            autoDelete: false
        );
        await channel.BasicPublishAsync(
            exchange: _exchange,
            routingKey: string.Empty,
            mandatory: false,
            basicProperties: new RabbitMQ.Client.BasicProperties
            {
                ContentType = "application/json",
            },
            body: System.Text.Encoding.UTF8.GetBytes("garbage-not-json"),
            cancellationToken: CancellationToken.None
        );

        // Give it a moment to be received and rejected.
        await Task.Delay(500);
        // No invalidations should have been recorded for the malformed payload,
        // and no exception should have escaped the receiver.
        cache.Invalidations.Should().BeEmpty();

        await svc.StopAsync(CancellationToken.None);
        await svc.DisposeAsync();
    }

    [Test]
    public async Task PublishAsync_EmptyConnectionString_ThrowsAtConstruction()
    {
        var options = new PersistedOperationsOptions
        {
            CacheEnabled = true,
            RabbitMqConnectionString = string.Empty,
        };

        Action act = () =>
            _ = new RabbitMqPersistedOperationBroadcaster(
                options,
                NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
            );
        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// Builds an invalidator backed by an empty service provider. The
    /// invalidator's HC-cache lookups all return null, so it is effectively
    /// a no-op for tests that only care about the RabbitMQ receive path.
    /// </summary>
    private static HotChocolateOperationCacheInvalidator NoOpInvalidator() =>
        new(
            new ServiceCollection().BuildServiceProvider(),
            new PersistedOperationCacheGeneration(),
            NullLogger<HotChocolateOperationCacheInvalidator>.Instance
        );

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(50);
        }
        return condition();
    }

    private sealed class RecordingCache : IPersistedOperationCache
    {
        public List<(string? TenantKey, string Id)> Invalidations { get; } = new();

        public string? TryGet(string? tenantKey, string id) => null;

        public void Set(string? tenantKey, string id, string document) { }

        public void Invalidate(string? tenantKey, string id)
        {
            lock (Invalidations)
                Invalidations.Add((tenantKey, id));
        }
    }
}
