using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RabbitMQ.Client;
using Trax.Effect.Broadcaster.RabbitMQ;
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

/// <summary>
/// Every message on the exchange is signed with the shared key, and a receiver passes on only a
/// message whose signature matches and is fresh. Without a key the host has to say so, and a
/// broker on another host has to be reached over TLS unless the host says otherwise.
/// </summary>
[TestFixture]
public class RabbitMqMessageSigningTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly string AmqpUri =
        $"amqp://trax:trax123@localhost:{PortOrDefault(Environment.GetEnvironmentVariable("TRAX_TEST_RABBITMQ_PORT"))}/";

    private static string PortOrDefault(string? port) =>
        string.IsNullOrWhiteSpace(port) ? "5672" : port;

    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"eventType":"Completed"}""");

    private static readonly byte[] OtherKey = Enumerable
        .Range(100, 32)
        .Select(i => (byte)i)
        .ToArray();

    #region Signature

    [Test]
    public void A_signature_made_with_the_key_is_accepted()
    {
        var signature = BroadcastMessageSignature.Create(TestBroadcastKey.Value, "ex", Body, Now);

        Verify(signature).Should().Be(BroadcastSignatureVerdict.Accepted);
    }

    [Test]
    public void A_message_without_a_signature_is_missing()
    {
        Verify(null).Should().Be(BroadcastSignatureVerdict.Missing);
        Verify("").Should().Be(BroadcastSignatureVerdict.Missing);
    }

    [Test]
    public void A_changed_body_does_not_match()
    {
        var signature = BroadcastMessageSignature.Create(TestBroadcastKey.Value, "ex", Body, Now);

        BroadcastMessageSignature
            .Verify(
                TestBroadcastKey.Value,
                "ex",
                Encoding.UTF8.GetBytes("""{"eventType":"Failed"}"""),
                signature,
                Now,
                TimeSpan.FromMinutes(5)
            )
            .Should()
            .Be(BroadcastSignatureVerdict.Invalid);
    }

    [Test]
    public void A_message_signed_for_another_exchange_does_not_match()
    {
        var signature = BroadcastMessageSignature.Create(
            TestBroadcastKey.Value,
            "other",
            Body,
            Now
        );

        Verify(signature).Should().Be(BroadcastSignatureVerdict.Invalid);
    }

    [Test]
    public void A_message_signed_with_another_key_does_not_match()
    {
        var signature = BroadcastMessageSignature.Create(OtherKey, "ex", Body, Now);

        Verify(signature).Should().Be(BroadcastSignatureVerdict.Invalid);
    }

    [Test]
    public void A_signature_with_its_timestamp_changed_does_not_match()
    {
        var signature = BroadcastMessageSignature.Create(
            TestBroadcastKey.Value,
            "ex",
            Body,
            Now.AddHours(-1)
        );
        var moved = signature.Replace(
            $"t={Now.AddHours(-1).ToUnixTimeSeconds()}",
            $"t={Now.ToUnixTimeSeconds()}"
        );

        Verify(moved).Should().Be(BroadcastSignatureVerdict.Invalid);
    }

    [TestCase(-6)]
    [TestCase(6)]
    public void A_signature_further_than_the_skew_from_this_clock_is_stale(int minutes)
    {
        var signature = BroadcastMessageSignature.Create(
            TestBroadcastKey.Value,
            "ex",
            Body,
            Now.AddMinutes(minutes)
        );

        Verify(signature).Should().Be(BroadcastSignatureVerdict.Stale);
    }

    [TestCase("v1")]
    [TestCase("v2,t=1,n=00000000000000000000000000000000,s=AAAA")]
    [TestCase("v1,t=-1,n=00000000000000000000000000000000,s=AAAA")]
    [TestCase("v1,t=1,n=short,s=AAAA")]
    [TestCase("v1,t=1,n=00000000000000000000000000000000,s=not base64!")]
    [TestCase("v1,t=1,n=00000000000000000000000000000000,s=AAAA")]
    public void A_malformed_signature_does_not_match(string signature) =>
        Verify(signature).Should().Be(BroadcastSignatureVerdict.Invalid);

    private static BroadcastSignatureVerdict Verify(string? signature) =>
        BroadcastMessageSignature.Verify(
            TestBroadcastKey.Value,
            "ex",
            Body,
            signature,
            Now,
            TimeSpan.FromMinutes(5)
        );

    #endregion

    #region Verifier

    [Test]
    public void The_verifier_logs_a_dropped_message_as_a_warning_at_most_once_an_interval()
    {
        var time = new FakeTimeProvider(Now);
        var logger = new RecordingLogger();
        var verifier = new BroadcastMessageVerifier(Signed(), logger, time);

        for (var i = 0; i < 5; i++)
            verifier.Accept("ex", Body, null).Should().BeFalse();
        time.Advance(BroadcastMessageVerifier.RefusalWarningInterval);
        verifier.Accept("ex", Body, "garbage").Should().BeFalse();

        verifier.Refused.Should().Be(6);
        logger.Warnings.Should().HaveCount(2);
        logger.Warnings[1].Should().Contain("4 more");
    }

    [Test]
    public void The_verifier_passes_a_signed_message_and_logs_nothing()
    {
        var time = new FakeTimeProvider(Now);
        var logger = new RecordingLogger();
        var verifier = new BroadcastMessageVerifier(Signed(), logger, time);
        var signature = BroadcastMessageSignature.Create(TestBroadcastKey.Value, "ex", Body, Now);

        verifier.Accept("ex", Body, signature).Should().BeTrue();
        logger.Warnings.Should().BeEmpty();
    }

    [Test]
    public void Without_a_key_the_verifier_passes_anything_only_after_AllowUnsignedMessages()
    {
        var options = new RabbitMqBroadcasterOptions { ConnectionString = "amqp://localhost" };
        new BroadcastMessageVerifier(options, null).Accept("ex", Body, null).Should().BeFalse();

        options.AllowUnsignedMessages();
        new BroadcastMessageVerifier(options, null).Accept("ex", Body, null).Should().BeTrue();
    }

    #endregion

    #region Posture

    [Test]
    public void UseRabbitMq_without_a_key_or_AllowUnsignedMessages_is_refused()
    {
        var act = () => Register("amqp://localhost", _ => { });

        act.Should().Throw<ArgumentException>().WithMessage("*SigningKey*AllowUnsignedMessages()*");
    }

    [Test]
    public void UseRabbitMq_with_a_short_key_is_refused()
    {
        var act = () => Register("amqp://localhost", o => o.SigningKey = new byte[16]);

        act.Should().Throw<ArgumentException>().WithMessage("*at least 32 bytes*");
    }

    [Test]
    public void UseRabbitMq_with_a_key_or_AllowUnsignedMessages_registers()
    {
        Register("amqp://localhost", o => o.SigningKey = TestBroadcastKey.Value);
        Register("amqp://localhost", o => o.AllowUnsignedMessages());
    }

    [TestCase("amqp://trax:trax123@rabbitmq.internal:5672/")]
    [TestCase("amqp://10.0.0.5/")]
    public void UseRabbitMq_with_plain_amqp_to_another_host_is_refused(string uri)
    {
        var act = () => Register(uri, o => o.SigningKey = TestBroadcastKey.Value);

        act.Should().Throw<ArgumentException>().WithMessage("*amqps://*AllowInsecureTransport*");
    }

    [TestCase("amqps://trax:trax123@rabbitmq.internal:5671/")]
    [TestCase("amqp://127.0.0.1:5672/")]
    [TestCase("amqp://[::1]:5672/")]
    [TestCase("amqp://localhost/")]
    public void UseRabbitMq_with_amqps_or_a_loopback_broker_registers(string uri) =>
        Register(uri, o => o.SigningKey = TestBroadcastKey.Value);

    [Test]
    public void UseRabbitMq_with_plain_amqp_to_another_host_registers_after_AllowInsecureTransport() =>
        Register(
            "amqp://rabbitmq.internal/",
            o =>
            {
                o.SigningKey = TestBroadcastKey.Value;
                o.AllowInsecureTransport = true;
            }
        );

    [Test]
    public async Task A_receiver_without_a_key_refuses_to_start_before_connecting()
    {
        // The URI points nowhere: the refusal must come before any connection attempt.
        await using var receiver = new RabbitMqTrainEventReceiver(
            new RabbitMqBroadcasterOptions { ConnectionString = "amqp://trax:trax123@127.0.0.1:1/" }
        );

        var act = () => receiver.StartAsync((_, _) => Task.CompletedTask, CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*AllowUnsignedMessages()*");
    }

    [Test]
    public void A_broadcaster_without_a_key_is_refused()
    {
        var act = () =>
            new RabbitMqTrainEventBroadcaster(
                new RabbitMqBroadcasterOptions { ConnectionString = "amqp://localhost" }
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*SigningKey*");
    }

    private static void Register(string uri, Action<RabbitMqBroadcasterOptions> configure) =>
        new TraxBuilder(new ServiceCollection(), new EffectRegistry()).AddEffects(effects =>
            effects.UseBroadcaster(b => b.UseRabbitMq(uri, configure))
        );

    #endregion

    #region Over a broker

    [Test]
    public async Task A_receiver_passes_on_only_the_messages_signed_with_its_key()
    {
        IConnection connection;
        try
        {
            connection = await new ConnectionFactory
            {
                Uri = new Uri(AmqpUri),
            }.CreateConnectionAsync();
        }
        catch (Exception ex)
        {
            Assert.Ignore($"RabbitMQ not reachable at {AmqpUri}: {ex.Message}");
            return;
        }

        await using var _ = connection;
        var options = Signed($"trax.test.signing.{Guid.NewGuid():N}");
        await using var receiver = new RabbitMqTrainEventReceiver(
            options,
            NullLogger<RabbitMqTrainEventReceiver>.Instance
        );
        var received = new ConcurrentQueue<string>();
        var gotSigned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        await receiver.StartAsync(
            (message, _) =>
            {
                received.Enqueue(message.ExternalId);
                if (message.ExternalId == "signed")
                    gotSigned.TrySetResult();
                return Task.CompletedTask;
            },
            CancellationToken.None
        );

        // On one channel, in order: the two the receiver must drop, then the one it must pass on.
        await using var channel = await connection.CreateChannelAsync();
        await RawPublish(channel, options.ExchangeName, "unsigned", key: null);
        await RawPublish(channel, options.ExchangeName, "other-key", OtherKey);
        await using (var broadcaster = new RabbitMqTrainEventBroadcaster(options))
        {
            await broadcaster.PublishAsync(Message("signed"), CancellationToken.None);
            await gotSigned.Task.WaitAsync(Timeout);
        }

        received.Should().Equal(["signed"]);
        receiver.RefusedMessages.Should().Be(2);
        await receiver.StopAsync(CancellationToken.None);
    }

    private static async Task RawPublish(
        IChannel channel,
        string exchange,
        string externalId,
        byte[]? key
    )
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(Message(externalId));
        var properties = new BasicProperties();
        if (key is not null)
            properties.Headers = new Dictionary<string, object?>
            {
                [BroadcastMessageSignature.Header] = BroadcastMessageSignature.Create(
                    key,
                    exchange,
                    body,
                    DateTimeOffset.UtcNow
                ),
            };
        await channel.BasicPublishAsync(exchange, string.Empty, mandatory: false, properties, body);
    }

    private static TrainLifecycleEventMessage Message(string externalId) =>
        new(
            MetadataId: 1,
            ExternalId: externalId,
            TrainName: "Signing.ITrain",
            TrainState: "Completed",
            Timestamp: DateTime.UtcNow,
            FailureJunction: null,
            FailureReason: null,
            EventType: "Completed",
            Executor: null,
            Output: null
        );

    #endregion

    private static RabbitMqBroadcasterOptions Signed(string exchange = "ex") =>
        new()
        {
            ConnectionString = AmqpUri,
            ExchangeName = exchange,
            SigningKey = TestBroadcastKey.Value,
        };

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
