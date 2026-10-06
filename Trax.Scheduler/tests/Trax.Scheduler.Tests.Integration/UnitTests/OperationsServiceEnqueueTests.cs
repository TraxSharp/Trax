using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using Trax.Core.Exceptions;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Mediator.Configuration;
using Trax.Mediator.Exceptions;
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
///
/// <para>Enforces <c>docs/adr/0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md</c>: a refusal
/// comes back as a failed result, and an infrastructure failure is logged and thrown, never
/// returned with its message.</para>
/// </summary>
[Property(
    "adr",
    "docs/adr/0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md"
)]
[TestFixture]
public class OperationsServiceEnqueueTests
{
    private ITrainDiscoveryService _discovery = null!;
    private ITrainExecutionService _execution = null!;
    private CapturingLogger _logger = null!;
    private OperationsService _service = null!;

    public record ProbeInput
    {
        public int CustomerId { get; init; }
    }

    public interface IProbeTrain;

    [SetUp]
    public void SetUp()
    {
        var discovery = _discovery = Substitute.For<ITrainDiscoveryService>();
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

        _logger = new CapturingLogger();
        _service = new OperationsService(
            discovery,
            Substitute.For<IDataContextProviderFactory>(),
            new SchedulerConfiguration(),
            _execution,
            logger: _logger
        );
    }

    private void EnqueueThrows(Exception ex) =>
        _execution
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<QueueTrainResult>>(_ => throw ex);

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
    public async Task An_oversized_input_is_refused_without_the_cap_or_its_size()
    {
        // The real mediator, so the refusal is the size cap's own: nothing about the cap or the
        // input's size may reach the caller, the same promise Trax.Api's error filter makes.
        var mediatorConfiguration = new Trax.Mediator.Configuration.MediatorConfiguration();
        var service = new OperationsService(
            _discovery,
            Substitute.For<IDataContextProviderFactory>(),
            new SchedulerConfiguration(),
            new TrainExecutionService(
                _discovery,
                runExecutor: null!,
                concurrencyLimiter: null!,
                Substitute.For<IDataContextProviderFactory>(),
                mediatorConfiguration,
                new ServiceCollection().BuildServiceProvider()
            )
        );
        var oversized =
            "{\"customerId\":1,\"pad\":\""
            + new string('x', mediatorConfiguration.MaxInputJsonBytes)
            + "\"}";

        var result = await service.QueueTrainAsync(
            new QueueTrainInput(typeof(IProbeTrain).FullName!, oversized),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Be("The train input failed validation.");
        result
            .Message.Should()
            .NotContain(
                mediatorConfiguration.MaxInputJsonBytes.ToString(),
                "the cap is not echoed to the caller"
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

    [Test]
    public async Task A_database_outage_is_thrown_rather_than_reported_as_a_refusal()
    {
        // The real mediator, over a data context that cannot reach its server: the enqueue
        // fails where it opens the connection, after every check on the input has passed.
        var data = Substitute.For<IDataContextProviderFactory>();
        data.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new NpgsqlException("Failed to connect to 10.0.0.5:5432"));

        var mediator = new TrainExecutionService(
            _discovery,
            runExecutor: null!,
            concurrencyLimiter: null!,
            data,
            new MediatorConfiguration(),
            new ServiceCollection().BuildServiceProvider()
        );
        var service = new OperationsService(
            _discovery,
            data,
            new SchedulerConfiguration(),
            mediator,
            logger: _logger
        );

        OperationResult? result = null;
        var act = async () =>
            result = await service.QueueTrainAsync(
                new QueueTrainInput(typeof(IProbeTrain).FullName!, "{\"customerId\":1}"),
                CancellationToken.None
            );

        await act.Should()
            .ThrowAsync<NpgsqlException>(
                "an unreachable database is the server failing, not the train refusing the input, "
                    + "and a thrown exception is masked by the GraphQL error filter (see docs/adr/0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md)"
            );
        result.Should().BeNull("no result may carry the exception's message to the caller");
        _logger
            .Errors.Should()
            .ContainSingle("the failure is logged where an operator can see it")
            .Which.Should()
            .BeOfType<NpgsqlException>();
    }

    [Test]
    public async Task A_data_layer_failure_wrapped_by_ef_is_thrown_rather_than_reported()
    {
        EnqueueThrows(
            new DbUpdateException(
                "An error occurred while saving the entity changes.",
                new NpgsqlException("Failed to connect to 10.0.0.5:5432")
            )
        );

        var act = async () =>
            await Queue(new QueueTrainInput(typeof(IProbeTrain).FullName!, "{\"customerId\":1}"));

        await act.Should()
            .ThrowAsync<DbUpdateException>(
                "a failure anywhere in the data layer is not a refusal (see docs/adr/0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md)"
            );
    }

    [Test]
    public async Task A_timeout_inside_the_on_queue_hook_is_thrown_rather_than_reported()
    {
        EnqueueThrows(
            new InvalidOperationException(
                "Hook failed.",
                new TimeoutException("Timed out talking to 10.0.0.5")
            )
        );

        var act = async () =>
            await Queue(new QueueTrainInput(typeof(IProbeTrain).FullName!, "{\"customerId\":1}"));

        await act.Should()
            .ThrowAsync<InvalidOperationException>(
                "the exception chain is what decides, not the outermost type (see docs/adr/0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md)"
            );
    }

    [Test]
    public async Task A_missing_authorization_service_is_thrown_rather_than_reported_as_a_refusal()
    {
        var missing = new TrainAuthorizationNotConfiguredException(
            typeof(IProbeTrain).FullName!,
            "Train 'IProbeTrain' declares [TraxAuthorize] but no ITrainAuthorizationService is registered."
        );
        EnqueueThrows(missing);

        OperationResult? result = null;
        var act = async () =>
            result = await Queue(
                new QueueTrainInput(typeof(IProbeTrain).FullName!, "{\"customerId\":1}")
            );

        (
            await act.Should()
                .ThrowAsync<TrainAuthorizationNotConfiguredException>(
                    "a host with no enforcer is misconfigured; that is not an answer about the input "
                        + "(see docs/adr/0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md)"
                )
        )
            .Which.Should()
            .BeSameAs(missing);
        result.Should().BeNull();
        _logger
            .Errors.Should()
            .ContainSingle("the misconfiguration is logged where an operator can see it")
            .Which.Should()
            .BeSameAs(missing);
    }

    [Test]
    public async Task A_missing_authorization_service_is_thrown_by_a_service_built_without_a_logger()
    {
        EnqueueThrows(
            new TrainAuthorizationNotConfiguredException(
                typeof(IProbeTrain).FullName!,
                "Train 'IProbeTrain' declares [TraxAuthorize] but no ITrainAuthorizationService is registered."
            )
        );
        var service = new OperationsService(
            _discovery,
            Substitute.For<IDataContextProviderFactory>(),
            new SchedulerConfiguration(),
            _execution
        );

        var act = async () =>
            await service.QueueTrainAsync(
                new QueueTrainInput(typeof(IProbeTrain).FullName!, "{\"customerId\":1}"),
                CancellationToken.None
            );

        await act.Should()
            .ThrowAsync<TrainAuthorizationNotConfiguredException>(
                "with nowhere to log it, the misconfiguration still reaches the caller"
            );
    }

    [Test]
    public async Task A_hook_refusal_is_still_reported_as_a_refusal()
    {
        EnqueueThrows(new TrainException("Customer 1 is on hold."));

        var result = await Queue(
            new QueueTrainInput(typeof(IProbeTrain).FullName!, "{\"customerId\":1}")
        );

        result.Success.Should().BeFalse();
        result
            .Message.Should()
            .Be(
                "The enqueue was refused: Customer 1 is on hold.",
                "a plain TrainException's message is the train author's, written for the caller"
            );
        _logger.Errors.Should().BeEmpty("a refusal is an answer, not a server fault");
    }

    [Test]
    public async Task A_hook_refusal_of_any_other_type_is_reported_with_a_fixed_message()
    {
        var thrown = new InvalidOperationException("secret-host:6379 did not answer");
        EnqueueThrows(thrown);

        var result = await Queue(
            new QueueTrainInput(typeof(IProbeTrain).FullName!, "{\"customerId\":1}")
        );

        result.Success.Should().BeFalse("it is still a refusal, not a server fault");
        result
            .Message.Should()
            .Be(
                "The enqueue was refused.",
                "only a plain TrainException's message reaches the caller (see docs/adr/0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md)"
            );
        result.Message.Should().NotContain("secret-host");
        _logger
            .Warnings.Should()
            .ContainSingle("the refusal's real exception is logged for an operator")
            .Which.Should()
            .BeSameAs(thrown);
    }

    [Test]
    public async Task A_refusal_from_a_type_derived_from_train_exception_is_reported_with_a_fixed_message()
    {
        EnqueueThrows(new DerivedTrainException("secret-host:6379 did not answer"));

        var result = await Queue(
            new QueueTrainInput(typeof(IProbeTrain).FullName!, "{\"customerId\":1}")
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Be("The enqueue was refused.");
    }

    [Test]
    public async Task A_hook_that_runs_past_its_limit_is_reported_with_the_mediators_message()
    {
        EnqueueThrows(
            new QueueHookTimeoutException(typeof(IProbeTrain).FullName!, TimeSpan.FromSeconds(5))
        );

        var result = await Queue(
            new QueueTrainInput(typeof(IProbeTrain).FullName!, "{\"customerId\":1}")
        );

        result.Success.Should().BeFalse();
        result.Message.Should().StartWith("The enqueue was refused: ").And.Contain("OnQueue");
    }

    private sealed class DerivedTrainException(string message) : TrainException(message);

    [Test]
    public async Task A_cancelled_deferred_entry_is_still_reported_as_a_refusal()
    {
        EnqueueThrows(new QueuedWorkCancelledException(7, typeof(IProbeTrain).FullName!));

        var result = await Queue(
            new QueueTrainInput(typeof(IProbeTrain).FullName!, "{\"customerId\":1}")
        );

        result.Success.Should().BeFalse();
        result.Message.Should().StartWith("The enqueue was refused: Work queue entry 7");
    }

    private sealed class CapturingLogger : ILogger<OperationsService>
    {
        public List<Exception> Errors { get; } = [];

        public List<Exception> Warnings { get; } = [];

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
            if (logLevel >= LogLevel.Error && exception is not null)
                Errors.Add(exception);
            else if (logLevel == LogLevel.Warning && exception is not null)
                Warnings.Add(exception);
        }
    }
}
