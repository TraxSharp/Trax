using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.Tests.PersistedOperations.Fixtures;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// The invalidation broadcast's signing key and transport posture: persisted operations and the
/// standalone store refuse to start without a key or with plain <c>amqp://</c> to another host,
/// unless the host opts out by name, and a node lets through only a message signed with the key.
/// </summary>
[TestFixture]
public class InvalidationSigningTests
{
    private const string Exchange = "trax.persisted_operations.invalidation";
    private static readonly byte[] Body = "{\"Id\":\"a\"}"u8.ToArray();

    [Test]
    public void Build_ABrokerWithoutAKey_RefusesToStart()
    {
        var act = () =>
            new PersistedOperationsBuilder().UseRabbitMqInvalidation("amqp://localhost").Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*SigningKey*AllowUnsignedMessages*");
    }

    [Test]
    public void Build_AKeyShorterThan32Bytes_RefusesToStart()
    {
        var act = () =>
            new PersistedOperationsBuilder()
                .UseRabbitMqInvalidation("amqp://localhost", o => o.SigningKey = new byte[31])
                .Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*at least 32 bytes*");
    }

    [Test]
    public void Build_UnsignedMessagesAllowedByName_Starts()
    {
        var options = new PersistedOperationsBuilder()
            .UseRabbitMqInvalidation("amqp://localhost", o => o.AllowUnsignedMessages())
            .Build();

        options.RabbitMqInvalidation.UnsignedMessagesAllowed.Should().BeTrue();
    }

    [TestCase("amqp://trax:trax123@rabbitmq.internal:5672/", false)]
    [TestCase("amqp://trax:trax123@10.0.0.4/", false)]
    [TestCase("amqps://trax:trax123@rabbitmq.internal:5671/", true)]
    [TestCase("amqp://trax:trax123@127.0.0.1:5672/", true)]
    [TestCase("amqp://trax:trax123@localhost/", true)]
    public void Build_PlainAmqpToAnotherHost_RefusesToStart(string uri, bool starts)
    {
        var act = () =>
            new PersistedOperationsBuilder()
                .UseRabbitMqInvalidation(uri, TestInvalidationKey.Sign)
                .Build();

        if (starts)
            act.Should().NotThrow();
        else
            act.Should().Throw<InvalidOperationException>().WithMessage("*not reached over TLS*");
    }

    [Test]
    public void Build_PlainAmqpWithAllowInsecureTransport_Starts()
    {
        var act = () =>
            new PersistedOperationsBuilder()
                .UseRabbitMqInvalidation(
                    "amqp://trax:trax123@rabbitmq.internal/",
                    o =>
                    {
                        o.SigningKey = TestInvalidationKey.Value;
                        o.AllowInsecureTransport = true;
                    }
                )
                .Build();

        act.Should().NotThrow();
    }

    [Test]
    public void AddPersistedOperationStore_ABrokerWithoutAKey_RefusesToStart()
    {
        var act = () =>
            new ServiceCollection().AddPersistedOperationStore(store =>
                store.UseRabbitMqInvalidation("amqp://localhost")
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*SigningKey*");
    }

    [Test]
    public async Task AddPersistedOperationStore_HandsItsKeyToTheBroadcaster()
    {
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddPersistedOperationStore(store =>
            store.UseRabbitMqInvalidation("amqp://localhost", TestInvalidationKey.Sign)
        );
        await using var sp = sc.BuildServiceProvider();

        sp.GetRequiredService<PersistedOperationsOptions>()
            .RabbitMqInvalidation.SigningKey.Should()
            .Equal(TestInvalidationKey.Value);
    }

    [Test]
    public void TheBroadcaster_WithoutAKey_RefusesToBeCreated()
    {
        var options = new PersistedOperationsOptions
        {
            RabbitMqConnectionString = "amqp://localhost",
        };

        var act = () =>
            new RabbitMqPersistedOperationBroadcaster(
                options,
                NullLogger<RabbitMqPersistedOperationBroadcaster>.Instance
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*SigningKey*");
    }

    [Test]
    public void AMessageSignedWithTheKey_IsAccepted()
    {
        var clock = new ManualClock();
        var verifier = Verifier(clock);

        verifier
            .Accept(Exchange, Body, Sign(TestInvalidationKey.Value, Exchange, clock))
            .Should()
            .BeTrue();
    }

    [Test]
    public void AMessageWithNoSignature_OrAnotherKeys_OrForAnotherExchange_IsDropped()
    {
        var clock = new ManualClock();
        var verifier = Verifier(clock);
        var otherKey = "another-key-of-thirty-two-bytes!"u8.ToArray();

        verifier.Accept(Exchange, Body, null).Should().BeFalse();
        verifier.Accept(Exchange, Body, Sign(otherKey, Exchange, clock)).Should().BeFalse();
        verifier
            .Accept(Exchange, Body, Sign(TestInvalidationKey.Value, "trax.lifecycle", clock))
            .Should()
            .BeFalse();
        verifier
            .Accept(Exchange, "{\"Id\":\"b\"}"u8, Sign(TestInvalidationKey.Value, Exchange, clock))
            .Should()
            .BeFalse("the signature covers the body");
        verifier.Refused.Should().Be(4);
    }

    [Test]
    public void AMessageOlderThanTheClockSkew_IsDropped()
    {
        var clock = new ManualClock();
        var verifier = Verifier(clock);
        var signature = Sign(TestInvalidationKey.Value, Exchange, clock);

        clock.Advance(TimeSpan.FromMinutes(6));

        verifier.Accept(Exchange, Body, signature).Should().BeFalse();
    }

    [Test]
    public void WithUnsignedMessagesAllowed_AMessageWithNoSignature_IsAccepted()
    {
        var verifier = new InvalidationMessageVerifier(
            Exchange,
            new RabbitMqInvalidationOptions().AllowUnsignedMessages(),
            NullLogger.Instance
        );

        verifier.Accept(Exchange, Body, null).Should().BeTrue();
    }

    [Test]
    public void AMessageSignedWithTheSameKeyUnderTheTrainEventScheme_IsDropped()
    {
        // The train-event broadcaster's MAC, built by hand: the same header format, the same
        // fields, the same key, and its own domain tag.
        var clock = new ManualClock();
        var verifier = Verifier(clock);
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds();
        const string messageId = "0123456789ABCDEF0123456789ABCDEF";
        var mac = MacUnder(
            "trax-broadcast/v1",
            TestInvalidationKey.Value,
            Exchange,
            timestamp,
            messageId
        );
        var signature = $"v1,t={timestamp},n={messageId},s={Convert.ToBase64String(mac)}";

        verifier
            .Accept(Exchange, Body, signature)
            .Should()
            .BeFalse("an invalidation's MAC is tagged apart from a train event's");

        var ours = MacUnder(
            InvalidationMessageSignature.DomainTag,
            TestInvalidationKey.Value,
            Exchange,
            timestamp,
            messageId
        );
        verifier
            .Accept(
                Exchange,
                Body,
                $"v1,t={timestamp},n={messageId},s={Convert.ToBase64String(ours)}"
            )
            .Should()
            .BeTrue("the same construction under the invalidation tag is accepted");
    }

    [Test]
    public void ADeliveryFromAnotherExchange_IsDropped_EvenWhenSignedForIt()
    {
        var clock = new ManualClock();
        var verifier = Verifier(clock);
        const string other = "trax.somewhere_else";

        verifier
            .Accept(other, Body, Sign(TestInvalidationKey.Value, other, clock))
            .Should()
            .BeFalse("only the exchange the node subscribed to delivers invalidations");
        verifier.Accept("", Body, Sign(TestInvalidationKey.Value, "", clock)).Should().BeFalse();
        verifier.Refused.Should().Be(2);
    }

    [Test]
    public void WithUnsignedMessagesAllowed_ADeliveryFromAnotherExchange_IsDropped()
    {
        var verifier = new InvalidationMessageVerifier(
            Exchange,
            new RabbitMqInvalidationOptions().AllowUnsignedMessages(),
            NullLogger.Instance
        );

        verifier.Accept("trax.somewhere_else", Body, null).Should().BeFalse();
    }

    private static byte[] MacUnder(
        string tag,
        byte[] key,
        string exchange,
        long timestamp,
        string messageId
    )
    {
        var exchangeBytes = Encoding.UTF8.GetBytes(exchange);
        var input = Encoding
            .UTF8.GetBytes($"{tag}\n{timestamp}\n{messageId}\n{exchangeBytes.Length}\n")
            .Concat(exchangeBytes)
            .Concat("\n"u8.ToArray())
            .Concat(Body)
            .ToArray();
        return HMACSHA256.HashData(key, input);
    }

    private static InvalidationMessageVerifier Verifier(TimeProvider clock) =>
        new(
            Exchange,
            new RabbitMqInvalidationOptions { SigningKey = TestInvalidationKey.Value },
            NullLogger.Instance,
            clock
        );

    private static string Sign(byte[] key, string exchange, TimeProvider clock) =>
        InvalidationMessageSignature.Create(key, exchange, Body, clock.GetUtcNow());
}
