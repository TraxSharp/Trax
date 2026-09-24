using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Queueing a train through the operations surface — the GraphQL <c>queueTrain</c> and
/// <c>requeueExecution</c> mutations and the dashboard's re-queue button all land here — has to go
/// through the mediator.
///
/// <para>
/// It used to write the work queue row directly, which skipped the train's <c>[TraxAuthorize]</c>
/// requirements, its <c>OnQueue</c> hook and its subject key. These tests pin the delegation,
/// because a hand-built row would be a second way to enqueue that quietly bypasses all three.
/// </para>
/// </summary>
[TestFixture]
public class OperationsServiceEnqueueTests
{
    private ITrainExecutionService _execution = null!;
    private OperationsService _service = null!;

    public record ProbeInput
    {
        public int CustomerId { get; init; }
    }

    public interface IProbeTrain;

    [SetUp]
    public void SetUp()
    {
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery
            .DiscoverTrains()
            .Returns([
                new TrainRegistration
                {
                    ServiceType = typeof(IProbeTrain),
                    ImplementationType = typeof(IProbeTrain),
                    InputType = typeof(ProbeInput),
                    OutputType = typeof(object),
                    Lifetime = ServiceLifetime.Scoped,
                    ServiceTypeName = typeof(IProbeTrain).FullName!,
                    ImplementationTypeName = typeof(IProbeTrain).FullName!,
                    InputTypeName = typeof(ProbeInput).FullName!,
                    OutputTypeName = typeof(object).FullName!,
                    RequiredPolicies = [],
                    RequiredRoles = [],
                    IsQuery = false,
                    IsMutation = true,
                    IsBroadcastEnabled = false,
                    IsRemote = false,
                    GraphQLOperations = GraphQLOperation.Queue,
                },
            ]);

        _execution = Substitute.For<ITrainExecutionService>();
        _execution
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new QueueTrainResult(42, "ext-42"));

        _service = new OperationsService(
            discovery,
            Substitute.For<IDataContextProviderFactory>(),
            new SchedulerConfiguration(),
            _execution
        );
    }

    private Task<OperationResult> Queue(QueueTrainInput input) =>
        _service.QueueTrainAsync(input, CancellationToken.None);

    [Test]
    public async Task Queueing_goes_through_the_execution_service()
    {
        var result = await Queue(
            new QueueTrainInput(typeof(IProbeTrain).FullName!, "{\"customerId\":1}")
        );

        result.Success.Should().BeTrue();
        result.Id.Should().Be(42, "the id comes from the entry the mediator created");

        await _execution
            .Received(1)
            .QueueAsync(
                typeof(IProbeTrain).FullName!,
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task A_scheduled_enqueue_keeps_its_scheduled_time()
    {
        var when = DateTime.UtcNow.AddHours(3);

        await Queue(
            new QueueTrainInput(typeof(IProbeTrain).FullName!, "{\"customerId\":1}")
            {
                ScheduledAt = when,
            }
        );

        await _execution
            .Received(1)
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                when,
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task Priority_is_carried_through()
    {
        await Queue(
            new QueueTrainInput(typeof(IProbeTrain).FullName!, "{\"customerId\":1}")
            {
                Priority = 7,
            }
        );

        await _execution
            .Received(1)
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                7,
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task An_authorization_failure_is_not_flattened_into_a_failed_result()
    {
        _execution
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<QueueTrainResult>>(_ => throw new UnauthorizedAccessException("nope"));

        var act = async () =>
            await Queue(new QueueTrainInput(typeof(IProbeTrain).FullName!, "{\"customerId\":1}"));

        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>(
                "not being allowed to run something is not a validation outcome, and reporting it "
                    + "as one would tell the caller more than it should"
            );
    }

    [Test]
    public async Task An_unknown_train_is_still_a_friendly_failure_and_never_reaches_the_mediator()
    {
        var result = await Queue(new QueueTrainInput("Nope.NotATrain", "{}"));

        result.Success.Should().BeFalse();
        await _execution
            .DidNotReceive()
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            );
    }
}
