using System.Text.Json;
using Amazon.Lambda.SQSEvents;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RequestSigning;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Sqs.Lambda;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// The SQS runner entry point, its posture and its signature check.
///
/// <para>Enforces <c>docs/adr/0006-a-runner-requires-an-authorization-posture.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0006-a-runner-requires-an-authorization-posture.md")]
[TestFixture]
public class SqsJobRunnerHandlerTests
{
    [Test]
    public async Task HandleAsync_SingleRecord_DispatchesToHandler()
    {
        var handler = new FakeRequestHandler();
        var sut = CreateHandler(handler);

        var sqsEvent = new SQSEvent
        {
            Records =
            [
                new SQSEvent.SQSMessage
                {
                    MessageId = "m1",
                    Body = JsonSerializer.Serialize(new RemoteJobRequest(MetadataId: 5)),
                },
            ],
        };

        await sut.HandleAsync(sqsEvent);

        handler.ExecuteCalls.Should().HaveCount(1);
        handler.ExecuteCalls[0].MetadataId.Should().Be(5);
    }

    [Test]
    public async Task HandleAsync_BodyWithARepeatedProperty_IsRefused()
    {
        var handler = new FakeRequestHandler();
        var sut = CreateHandler(handler);

        var sqsEvent = new SQSEvent
        {
            Records =
            [
                new SQSEvent.SQSMessage
                {
                    MessageId = "m1",
                    Body = "{\"MetadataId\":1,\"MetadataId\":2}",
                },
            ],
        };

        var act = async () => await sut.HandleAsync(sqsEvent);

        await act.Should().ThrowAsync<JsonException>();
        handler.ExecuteCalls.Should().BeEmpty();
    }

    [Test]
    public async Task HandleAsync_MultipleRecords_ProcessesAllInOrder()
    {
        var handler = new FakeRequestHandler();
        var sut = CreateHandler(handler);

        var sqsEvent = new SQSEvent
        {
            Records =
            [
                new SQSEvent.SQSMessage
                {
                    MessageId = "m1",
                    Body = JsonSerializer.Serialize(new RemoteJobRequest(MetadataId: 1)),
                },
                new SQSEvent.SQSMessage
                {
                    MessageId = "m2",
                    Body = JsonSerializer.Serialize(new RemoteJobRequest(MetadataId: 2)),
                },
                new SQSEvent.SQSMessage
                {
                    MessageId = "m3",
                    Body = JsonSerializer.Serialize(new RemoteJobRequest(MetadataId: 3)),
                },
            ],
        };

        await sut.HandleAsync(sqsEvent);

        handler.ExecuteCalls.Select(r => r.MetadataId).Should().Equal(1, 2, 3);
    }

    [Test]
    public async Task HandleAsync_HandlerThrows_RethrowsExceptionForLambdaRetry()
    {
        var handler = new FakeRequestHandler
        {
            ExecuteException = new InvalidOperationException("nope"),
        };
        var sut = CreateHandler(handler);

        var sqsEvent = new SQSEvent
        {
            Records =
            [
                new SQSEvent.SQSMessage
                {
                    MessageId = "m1",
                    Body = JsonSerializer.Serialize(new RemoteJobRequest(MetadataId: 1)),
                },
            ],
        };

        var act = () => sut.HandleAsync(sqsEvent);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("nope");
    }

    [Test]
    public async Task HandleAsync_InvalidJsonBody_ThrowsInvalidOperation()
    {
        var handler = new FakeRequestHandler();
        var sut = CreateHandler(handler);

        var sqsEvent = new SQSEvent
        {
            Records = [new SQSEvent.SQSMessage { MessageId = "m1", Body = "null" }],
        };

        var act = () => sut.HandleAsync(sqsEvent);
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to deserialize*");

        handler.ExecuteCalls.Should().BeEmpty();
    }

    [Test]
    public async Task HandleAsync_EmptyRecords_NoOp()
    {
        var handler = new FakeRequestHandler();
        var sut = CreateHandler(handler);

        await sut.HandleAsync(new SQSEvent { Records = [] });

        handler.ExecuteCalls.Should().BeEmpty();
    }

    [Test]
    public async Task HandleAsync_PassesCancellationTokenThrough()
    {
        var handler = new FakeRequestHandler();
        var sut = CreateHandler(handler);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        var sqsEvent = new SQSEvent
        {
            Records =
            [
                new SQSEvent.SQSMessage
                {
                    MessageId = "m1",
                    Body = JsonSerializer.Serialize(new RemoteJobRequest(MetadataId: 9)),
                },
            ],
        };

        await sut.HandleAsync(sqsEvent, token);

        handler.LastCancellationToken.Should().Be(token);
    }

    [Test]
    public async Task HandleBatchAsync_RecordWithNoBody_IsReportedWithoutRunningAnything()
    {
        var handler = new FakeRequestHandler();
        var sut = CreateHandler(handler);

        var response = await sut.HandleBatchAsync(
            new SQSEvent { Records = [new SQSEvent.SQSMessage { MessageId = "m1", Body = null }] }
        );

        response.BatchItemFailures.Select(f => f.ItemIdentifier).Should().Equal(["m1"]);
        handler.ExecuteCalls.Should().BeEmpty("a message with no body names no run");
    }

    [Test]
    public async Task HandleBatchAsync_FailedRunWhoseStateCannotBeRead_IsDeliveredAgain()
    {
        var handler = new FakeRequestHandler
        {
            ExecuteException = new InvalidOperationException("nope"),
        };
        var factory = Substitute.For<IDataContextProviderFactory>();
        factory
            .CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IDataContext>>(_ => throw new InvalidOperationException("db down"));
        var sut = CreateHandler(handler, configure: s => s.AddSingleton(factory));

        var response = await sut.HandleBatchAsync(new SQSEvent { Records = [Message(1)] });

        response
            .BatchItemFailures.Select(f => f.ItemIdentifier)
            .Should()
            .Equal(
                ["m1"],
                "with the run's state unknown, delivering again is safer than dropping the message"
            );
    }

    private static SqsJobRunnerHandler CreateHandler(
        FakeRequestHandler handler,
        Action<TraxJobRunnerOptions>? runner = null,
        Action<IServiceCollection>? configure = null
    )
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);
        services.AddLogging();
        services.AddSingleton<ITraxRequestHandler>(handler);
        var options = new TraxJobRunnerOptions();
        (runner ?? (o => o.AllowUnsignedRequests()))(options);
        services.AddSingleton(options);
        services.AddSingleton<INonceStore, InMemoryNonceStore>();
        services.AddSingleton<RunnerRequestVerifier>();
        return new SqsJobRunnerHandler(services.BuildServiceProvider());
    }

    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private static SQSEvent.SQSMessage Message(long metadataId, byte[]? signWith = null)
    {
        var body = JsonSerializer.Serialize(new RemoteJobRequest(MetadataId: metadataId));
        var message = new SQSEvent.SQSMessage { MessageId = $"m{metadataId}", Body = body };
        if (signWith is not null)
            message.MessageAttributes = new Dictionary<string, SQSEvent.MessageAttribute>
            {
                [RunnerRequestSignature.HeaderName] = new()
                {
                    DataType = "String",
                    StringValue = RunnerRequestSignature.Create(
                        signWith,
                        RunnerRequestPurpose.Execute,
                        System.Text.Encoding.UTF8.GetBytes(body)
                    ),
                },
            };
        return message;
    }

    [Test]
    public async Task HandleAsync_NoPosture_IsRefused()
    {
        var handler = new FakeRequestHandler();
        var sut = CreateHandler(handler, _ => { });

        var act = async () => await sut.HandleAsync(new SQSEvent { Records = [Message(1)] });

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*no authorization posture*");
        handler.ExecuteCalls.Should().BeEmpty();
    }

    [Test]
    public async Task HandleAsync_SigningKey_UnsignedMessage_IsRefused()
    {
        var handler = new FakeRequestHandler();
        var sut = CreateHandler(handler, o => o.SigningKey = Key);

        var act = async () => await sut.HandleAsync(new SQSEvent { Records = [Message(1)] });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Missing*");
        handler
            .ExecuteCalls.Should()
            .BeEmpty(
                "a runner with a signing key refuses an unsigned message (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );
    }

    [Test]
    public async Task HandleAsync_SigningKey_MessageSignedWithAnotherKey_IsRefused()
    {
        var handler = new FakeRequestHandler();
        var sut = CreateHandler(handler, o => o.SigningKey = Key);
        var otherKey = Enumerable.Repeat((byte)9, 32).ToArray();

        var act = async () =>
            await sut.HandleAsync(new SQSEvent { Records = [Message(1, otherKey)] });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Invalid*");
        handler.ExecuteCalls.Should().BeEmpty();
    }

    [Test]
    public async Task HandleAsync_SigningKey_RedeliveredSignedMessage_RunsEachTime()
    {
        // SQS redelivers the same message by design; the Pending metadata row stops a second run.
        var handler = new FakeRequestHandler();
        var sut = CreateHandler(handler, o => o.SigningKey = Key);
        var message = Message(3, Key);

        await sut.HandleAsync(new SQSEvent { Records = [message] });
        await sut.HandleAsync(new SQSEvent { Records = [message] });

        handler.ExecuteCalls.Should().HaveCount(2);
    }

    private sealed class FakeRequestHandler : ITraxRequestHandler
    {
        public List<RemoteJobRequest> ExecuteCalls { get; } = [];
        public CancellationToken LastCancellationToken { get; private set; }
        public Exception? ExecuteException { get; set; }

        public Task<ExecuteJobResult> ExecuteJobAsync(
            RemoteJobRequest request,
            CancellationToken ct = default
        )
        {
            ExecuteCalls.Add(request);
            LastCancellationToken = ct;
            if (ExecuteException is not null)
                throw ExecuteException;
            return Task.FromResult(new ExecuteJobResult(request.MetadataId));
        }

        public Task<RemoteRunResponse> RunTrainAsync(
            RemoteRunRequest request,
            CancellationToken ct = default
        ) => throw new NotImplementedException();
    }
}
