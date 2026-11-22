using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.Tests.PersistedOperations.Fixtures;
using Trax.Core.Extensions;

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

    /// <summary>Properties for a message published by hand, signed with the test key.</summary>
    private BasicProperties Signed(byte[] body) =>
        new()
        {
            ContentType = "application/json",
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [InvalidationMessageSignature.Header] = InvalidationMessageSignature.Create(
                    TestInvalidationKey.Value,
                    _exchange,
                    body,
                    TimeProvider.System.GetUtcNow()
                ),
            },
        };

    private static async Task<bool> IsRabbitMqReachableAsync()
    {
        try
        {
            var factory = new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(AmqpUri) };
            var conn = await factory.CreateConnectionAsync().ConfigureAwait(false);
            await using (conn.ConfigureAwait(false))
                return conn.IsOpen;
        }
        catch
        {
            return false;
        }
    }

    [SetUp]
    public async Task SetUp()
    {
        if (!await IsRabbitMqReachableAsync())
            Assert.Ignore("RabbitMQ not reachable.");
    }

    [Test]
    public async Task PublishAsync_DeliversMessage_ToReceiverOnSameExchange()
    {
        var options = OnRunExchange(
            new PersistedOperationsBuilder()
                .WithInMemoryCache()
                .UseRabbitMqInvalidation(AmqpUri, TestInvalidationKey.Sign)
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
                    TenantKey: null,
                    receivedKey,
                    PersistedOperationChangeType.Upsert,
                    TimeProvider.System.GetUtcNow().UtcDateTime
                ),
                cts.Token
            );

            // Poll for the invalidation. The receiver runs on its own queue
            // bound to the shared fanout; it sees every message but we only
            // assert on the one matching our unique id.
            var saw = await WaitUntilAsync(
                () =>
                    cache.Invalidations.Exists(p =>
                        string.Equals(p.Id, receivedKey, StringComparison.Ordinal)
                    ),
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
            new PersistedOperationsBuilder()
                .UseRabbitMqInvalidation(AmqpUri, TestInvalidationKey.Sign)
                .Build()
        );
        await using var publisher = new RabbitMqPersistedOperationBroadcaster(
            options,
            NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
        );

        await publisher.PublishAsync(
            new PersistedOperationChangedMessage(
                TenantKey: null,
                $"confirm_{Guid.NewGuid():N}",
                PersistedOperationChangeType.Upsert,
                TimeProvider.System.GetUtcNow().UtcDateTime
            ),
            CancellationToken.None
        );

        // A channel numbers its publishes only in publisher-confirm mode; otherwise this is 0.
        (await publisher.Channel.NotNull().GetNextPublishSequenceNumberAsync())
            .Should()
            .BeGreaterThan(1UL);
    }

    [Test]
    public async Task PublishAsync_AfterItsChannelClosed_OpensAnotherOnTheSameConnection()
    {
        var options = OnRunExchange(
            new PersistedOperationsBuilder()
                .UseRabbitMqInvalidation(AmqpUri, TestInvalidationKey.Sign)
                .Build()
        );
        await using var publisher = new RabbitMqPersistedOperationBroadcaster(
            options,
            NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
        );
        var message = new PersistedOperationChangedMessage(
            TenantKey: null,
            $"reopen_{Guid.NewGuid():N}",
            PersistedOperationChangeType.Upsert,
            TimeProvider.System.GetUtcNow().UtcDateTime
        );
        await publisher.PublishAsync(message, CancellationToken.None);
        var first = publisher.Channel.NotNull();
        await first.CloseAsync(cancellationToken: CancellationToken.None);

        await publisher.PublishAsync(message, CancellationToken.None);

        publisher.Channel.Should().NotBeSameAs(first);
        publisher.Channel.NotNull().IsOpen.Should().BeTrue();
    }

    [Test]
    public async Task PublishAsync_WhenTheBrokerCannotBeReached_Throws()
    {
        var options = new PersistedOperationsBuilder()
            .UseRabbitMqInvalidation("amqp://trax:trax123@localhost:1/", TestInvalidationKey.Sign)
            .Build();
        await using var publisher = new RabbitMqPersistedOperationBroadcaster(
            options,
            NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
        );

        var act = () =>
            publisher.PublishAsync(
                new PersistedOperationChangedMessage(
                    TenantKey: null,
                    "unreachable",
                    PersistedOperationChangeType.Upsert,
                    TimeProvider.System.GetUtcNow().UtcDateTime
                ),
                CancellationToken.None
            );

        await act.Should().ThrowAsync<Exception>();
    }

    [Test]
    public async Task TwoReceivers_BothObserveSameMessage()
    {
        var options = SignedOnRunExchange();

        var key = $"twonodes_{Guid.NewGuid():N}";
        var cacheA = new RecordingCache();
        var cacheB = new RecordingCache();

        await using var publisher = new RabbitMqPersistedOperationBroadcaster(
            options,
            NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
        );
        var receiverA = NewReceiver(options, cacheA);
        var receiverB = NewReceiver(options, cacheB);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await receiverA.StartAsync(cts.Token);
        await receiverB.StartAsync(cts.Token);
        try
        {
            await publisher.PublishAsync(
                new PersistedOperationChangedMessage(
                    TenantKey: null,
                    key,
                    PersistedOperationChangeType.Upsert,
                    TimeProvider.System.GetUtcNow().UtcDateTime
                ),
                cts.Token
            );

            var sawA = await WaitForInvalidationAsync(cacheA, key);
            var sawB = await WaitForInvalidationAsync(cacheB, key);

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
    public async Task AMessageNotSignedWithTheKey_IsDropped_AndEmptiesNothing()
    {
        var options = SignedOnRunExchange();
        var cache = new RecordingCache();
        var receiver = NewReceiver(options, cache);
        await receiver.StartAsync(CancellationToken.None);

        var unsigned = $"unsigned_{Guid.NewGuid():N}";
        var forged = $"forged_{Guid.NewGuid():N}";
        var sentinel = $"sentinel_{Guid.NewGuid():N}";
        try
        {
            await PublishUnsignedAndForgedAsync(options.RabbitMqExchange, unsigned, forged, Body);

            await using var publisher = new RabbitMqPersistedOperationBroadcaster(
                options,
                NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
            );
            await publisher.PublishAsync(Change(sentinel), CancellationToken.None);

            (await WaitForInvalidationAsync(cache, sentinel))
                .Should()
                .BeTrue("a message signed with the key is received");
            cache
                .Invalidations.Should()
                .NotContain(
                    p => p.Id == unsigned || p.Id == forged,
                    "a message with no signature, or another key's, empties nothing"
                );
            receiver.RefusedMessages.Should().Be(2);
        }
        finally
        {
            await receiver.StopAsync(CancellationToken.None);
        }

        static PersistedOperationChangedMessage Change(string id) =>
            new(
                TenantKey: null,
                id,
                PersistedOperationChangeType.Upsert,
                TimeProvider.System.GetUtcNow().UtcDateTime
            );

        static byte[] Body(string id) =>
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Change(id));
    }

    [Test]
    public async Task ReceiverService_StoppedReceiver_DoesNotInvalidateCache()
    {
        // After Stop, a published message must NOT reach the receiver's cache.
        // This proves StopAsync actually unbinds the consumer rather than
        // leaving it silently running.
        var options = SignedOnRunExchange();
        var cache = new RecordingCache();
        var svc = NewReceiver(options, cache);

        // A receiver left running on the same exchange: once it has seen a sentinel published after
        // the message, the stopped receiver would have been handed that message too.
        var liveCache = new RecordingCache();
        var live = NewReceiver(options, liveCache);

        await svc.StartAsync(CancellationToken.None);
        await live.StartAsync(CancellationToken.None);
        await svc.StopAsync(CancellationToken.None);

        await using var publisher = new RabbitMqPersistedOperationBroadcaster(
            options,
            NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
        );
        var key = $"after_stop_{Guid.NewGuid():N}";
        var sentinel = $"sentinel_{Guid.NewGuid():N}";
        try
        {
            foreach (var id in new[] { key, sentinel })
                await publisher.PublishAsync(
                    new PersistedOperationChangedMessage(
                        TenantKey: null,
                        id,
                        PersistedOperationChangeType.Upsert,
                        TimeProvider.System.GetUtcNow().UtcDateTime
                    ),
                    CancellationToken.None
                );

            (await WaitForInvalidationAsync(liveCache, sentinel))
                .Should()
                .BeTrue("the running receiver sees the sentinel");
            liveCache
                .Invalidations.Should()
                .Contain(p => p.Id == key, "the message was published to the exchange");
            cache
                .Invalidations.Should()
                .NotContain(p => p.Id == key, "stopped receiver must not invalidate");
        }
        finally
        {
            await live.StopAsync(CancellationToken.None);
        }

        await svc.DisposeAsync();
        // Idempotent stop after dispose must not throw.
        await svc.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task AReceiverWhoseChannelTheBrokerCloses_EmptiesItsCaches_AndKeepsReceiving()
    {
        var options = SignedOnRunExchange();
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
            await svc.Channel.NotNull().BasicAckAsync(deliveryTag: 9_999, multiple: false);

            (await WaitUntilAsync(() => generation.Current > before, TimeSpan.FromSeconds(10)))
                .Should()
                .BeTrue("a receiver that lost its channel may have missed a broadcast");

            var id = $"after_channel_close_{Guid.NewGuid():N}";
            var received = await PublishUntilReceivedAsync(publisher, cache, id);
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
                .UseRabbitMqInvalidation(AmqpUri, TestInvalidationKey.Sign)
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
        var options = SignedOnRunExchange();
        var cache = new RecordingCache();
        var svc = NewReceiver(options, cache);
        await svc.StartAsync(CancellationToken.None);

        var factory = new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(AmqpUri) };
        await using var conn = await factory.CreateConnectionAsync();
        await using var channel = await conn.CreateChannelAsync(
            cancellationToken: CancellationToken.None
        );
        var sentinel = $"sentinel_{Guid.NewGuid():N}";
        await PublishGarbageThenSentinelAsync(channel, sentinel);
        (await WaitForInvalidationAsync(cache, sentinel))
            .Should()
            .BeTrue("the receiver kept consuming after the malformed message");
        // No invalidations should have been recorded for the malformed payload,
        // and no exception should have escaped the receiver.
        cache.Invalidations.Should().ContainSingle().Which.Id.Should().Be(sentinel);

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

    private PersistedOperationsOptions SignedOnRunExchange() =>
        OnRunExchange(
            new PersistedOperationsBuilder()
                .WithInMemoryCache()
                .UseRabbitMqInvalidation(AmqpUri, TestInvalidationKey.Sign)
                .Build()
        );

    private static PersistedOperationReceiverService NewReceiver(
        PersistedOperationsOptions options,
        RecordingCache cache
    ) =>
        new(
            options,
            cache,
            NoOpInvalidator(),
            NullLogger<PersistedOperationReceiverService>.Instance
        );

    private static Task<bool> WaitForInvalidationAsync(RecordingCache cache, string id) =>
        WaitUntilAsync(
            () =>
                cache.Invalidations.Exists(p => string.Equals(p.Id, id, StringComparison.Ordinal)),
            TimeSpan.FromSeconds(10)
        );

    // One message with no signature and one signed with another key.
    private static async Task PublishUnsignedAndForgedAsync(
        string exchange,
        string unsigned,
        string forged,
        Func<string, byte[]> body
    )
    {
        var factory = new ConnectionFactory { Uri = new Uri(AmqpUri) };
        var connection = await factory.CreateConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var channel = await connection
                .CreateChannelAsync(cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            await using (channel.ConfigureAwait(false))
            {
                await channel
                    .BasicPublishAsync(
                        exchange,
                        string.Empty,
                        mandatory: false,
                        new BasicProperties(),
                        body(unsigned),
                        cancellationToken: CancellationToken.None
                    )
                    .ConfigureAwait(false);
                var otherKey = "another-key-of-thirty-two-bytes!"u8.ToArray();
                await channel
                    .BasicPublishAsync(
                        exchange,
                        string.Empty,
                        mandatory: false,
                        new BasicProperties
                        {
                            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                [InvalidationMessageSignature.Header] =
                                    InvalidationMessageSignature.Create(
                                        otherKey,
                                        exchange,
                                        body(forged),
                                        TimeProvider.System.GetUtcNow()
                                    ),
                            },
                        },
                        body(forged),
                        cancellationToken: CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }
        }
    }

    // Publishes until the receiver has the message, retrying while it is not yet subscribed.
    private static async Task<bool> PublishUntilReceivedAsync(
        RabbitMqPersistedOperationBroadcaster publisher,
        RecordingCache cache,
        string id
    )
    {
        return await WaitUntilAsync(
                async () =>
                {
                    try
                    {
                        await publisher
                            .PublishAsync(
                                new PersistedOperationChangedMessage(
                                    TenantKey: null,
                                    id,
                                    PersistedOperationChangeType.Upsert,
                                    TimeProvider.System.GetUtcNow().UtcDateTime
                                ),
                                CancellationToken.None
                            )
                            .ConfigureAwait(false);
                    }
                    catch (RabbitMQ.Client.Exceptions.PublishException)
                    {
                        // The broker nacks a publish routed to the old channel's auto-delete
                        // queue while it is being deleted. Not yet resubscribed: try again.
                        return false;
                    }
                    return cache.Invalidations.Exists(p =>
                        string.Equals(p.Id, id, StringComparison.Ordinal)
                    );
                },
                TimeSpan.FromSeconds(15)
            )
            .ConfigureAwait(false);
    }

    private async Task PublishGarbageThenSentinelAsync(IChannel channel, string sentinel)
    {
        await channel
            .ExchangeDeclareAsync(
                exchange: _exchange,
                type: RabbitMQ.Client.ExchangeType.Fanout,
                durable: true,
                autoDelete: false,
                cancellationToken: CancellationToken.None
            )
            .ConfigureAwait(false);
        await channel
            .BasicPublishAsync(
                exchange: _exchange,
                routingKey: string.Empty,
                mandatory: false,
                basicProperties: Signed(System.Text.Encoding.UTF8.GetBytes("garbage-not-json")),
                body: System.Text.Encoding.UTF8.GetBytes("garbage-not-json"),
                cancellationToken: CancellationToken.None
            )
            .ConfigureAwait(false);

        // A valid message published after it on the same channel reaches the queue after it, so once
        // the receiver has handled it the malformed one was received and rejected.
        var sentinelBody = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new PersistedOperationChangedMessage(
                TenantKey: null,
                sentinel,
                PersistedOperationChangeType.Upsert,
                TimeProvider.System.GetUtcNow().UtcDateTime
            )
        );
        await channel
            .BasicPublishAsync(
                exchange: _exchange,
                routingKey: string.Empty,
                mandatory: false,
                basicProperties: Signed(sentinelBody),
                body: sentinelBody,
                cancellationToken: CancellationToken.None
            )
            .ConfigureAwait(false);
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

    private static Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout) =>
        WaitUntilAsync(() => Task.FromResult(condition()), timeout);

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = TimeProvider.System.GetUtcNow() + timeout;
        while (TimeProvider.System.GetUtcNow() < deadline)
        {
            if (await condition().ConfigureAwait(false))
                return true;
            // determinism: the poll interval of a wait bounded by the deadline above.
            await Task.Delay(50).ConfigureAwait(false);
        }
        return await condition().ConfigureAwait(false);
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
