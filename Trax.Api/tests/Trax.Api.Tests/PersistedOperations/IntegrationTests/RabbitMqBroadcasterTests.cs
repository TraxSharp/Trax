using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Storage;

namespace Trax.Api.Tests.PersistedOperations.IntegrationTests;

/// <summary>
/// Integration tests against a real RabbitMQ broker (the docker-compose
/// trax_rabbitmq container). Exercises the publish/receive path end to end.
/// Tests filter by a unique id-prefix so concurrent runs do not collide on
/// the shared exchange.
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
        var options = new PersistedOperationsBuilder()
            .UseDatabase("Host=fake")
            .WithInMemoryCache()
            .UseRabbitMqInvalidation(AmqpUri)
            .Build();

        var receivedKey = $"test_{Guid.NewGuid():N}";
        var cache = new RecordingCache();

        await using var publisher = new RabbitMqPersistedOperationBroadcaster(
            options,
            NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
        );
        var receiver = new PersistedOperationReceiverService(
            options,
            cache,
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
    public async Task TwoReceivers_BothObserveSameMessage()
    {
        var options = new PersistedOperationsBuilder()
            .UseDatabase("Host=fake")
            .WithInMemoryCache()
            .UseRabbitMqInvalidation(AmqpUri)
            .Build();

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
            NullLogger<PersistedOperationReceiverService>.Instance
        );
        var receiverB = new PersistedOperationReceiverService(
            options,
            cacheB,
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
        var options = new PersistedOperationsBuilder()
            .UseDatabase("Host=fake")
            .WithInMemoryCache()
            .UseRabbitMqInvalidation(AmqpUri)
            .Build();
        var cache = new RecordingCache();
        var svc = new PersistedOperationReceiverService(
            options,
            cache,
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
    public async Task ReceiverService_DisposeWithoutStop_ReleasesResourcesIdempotently()
    {
        var options = new PersistedOperationsBuilder()
            .UseDatabase("Host=fake")
            .WithInMemoryCache()
            .UseRabbitMqInvalidation(AmqpUri)
            .Build();
        var svc = new PersistedOperationReceiverService(
            options,
            new RecordingCache(),
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
        var options = new PersistedOperationsBuilder()
            .UseDatabase("Host=fake")
            .WithInMemoryCache()
            .UseRabbitMqInvalidation(AmqpUri)
            .Build();
        var cache = new RecordingCache();
        var svc = new PersistedOperationReceiverService(
            options,
            cache,
            NullLogger<PersistedOperationReceiverService>.Instance
        );
        await svc.StartAsync(CancellationToken.None);

        var factory = new RabbitMQ.Client.ConnectionFactory { Uri = new Uri(AmqpUri) };
        await using var conn = await factory.CreateConnectionAsync();
        await using var channel = await conn.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(
            exchange: RabbitMqPersistedOperationBroadcaster.ExchangeName,
            type: RabbitMQ.Client.ExchangeType.Fanout,
            durable: true,
            autoDelete: false
        );
        await channel.BasicPublishAsync(
            exchange: RabbitMqPersistedOperationBroadcaster.ExchangeName,
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
            DatabaseConnectionString = "Host=fake",
            RabbitMqConnectionString = string.Empty,
        };

        Action act = () =>
            _ = new RabbitMqPersistedOperationBroadcaster(
                options,
                NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
            );
        act.Should().Throw<InvalidOperationException>();
    }

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
