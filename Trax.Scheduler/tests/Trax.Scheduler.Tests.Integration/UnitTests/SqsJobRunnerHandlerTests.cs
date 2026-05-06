using System.Text.Json;
using Amazon.Lambda.SQSEvents;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Sqs.Lambda;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

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

    private static SqsJobRunnerHandler CreateHandler(FakeRequestHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITraxRequestHandler>(handler);
        return new SqsJobRunnerHandler(services.BuildServiceProvider());
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
