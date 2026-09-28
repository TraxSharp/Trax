using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using Trax.Effect.Attributes;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Running a train through the operations surface: the one run path the dashboard's Run dialog
/// and an API run operation share, so the two cannot drift in how they authorize, read the
/// input, route the job or clean up after a failed submit.
///
/// <para>Enforces <c>Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md</c>:
/// the run logic lives here, not in a surface.</para>
///
/// <para>Enforces <c>docs/adr/0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md</c>
/// for a run: bad input is a failed result, and a submit failure is thrown.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[Property(
    "adr",
    "docs/adr/0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md"
)]
[TestFixture]
public class OperationsServiceRunTests
{
    private ServiceProvider _provider = null!;
    private IJobSubmitter _submitter = null!;
    private CapturingLogger _logger = null!;
    private OperationsService _service = null!;
    private ITrainAuthorizationService? _authorization;

    public record ProbeInput
    {
        public int CustomerId { get; init; }
    }

    public interface IProbeTrain;

    public interface IGuardedTrain;

    public interface IRoutedTrain;

    [SetUp]
    public void SetUp()
    {
        _submitter = Substitute.For<IJobSubmitter>();
        _submitter
            .EnqueueAsync(Arg.Any<long>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns("job-1");
        _authorization = null;
        _logger = new CapturingLogger();
        Build();
    }

    [TearDown]
    public async Task TearDown() => await _provider.DisposeAsync();

    private void Build(Action<IServiceCollection>? configure = null)
    {
        _provider?.Dispose();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax => trax.AddEffects(effects => effects.UseInMemory()));
        services.AddSingleton(_submitter);
        services.AddSingleton<ITrustedExecutionScope, TrustedExecutionScope>();
        if (_authorization is not null)
            services.AddSingleton(_authorization);
        configure?.Invoke(services);
        _provider = services.BuildServiceProvider();

        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery
            .DiscoverTrains()
            .Returns([
                Registration(typeof(IProbeTrain), guarded: false),
                Registration(typeof(IGuardedTrain), guarded: true),
                Registration(typeof(IRoutedTrain), guarded: false),
            ]);

        _service = new OperationsService(
            discovery,
            _provider.GetRequiredService<IDataContextProviderFactory>(),
            new SchedulerConfiguration(),
            Substitute.For<ITrainExecutionService>(),
            _provider,
            logger: _logger
        );
    }

    private static TrainRegistration Registration(Type serviceType, bool guarded) =>
        new()
        {
            ServiceType = serviceType,
            ImplementationType = serviceType,
            InputType = typeof(ProbeInput),
            OutputType = typeof(object),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = serviceType.FullName!,
            ImplementationTypeName = serviceType.FullName!,
            InputTypeName = typeof(ProbeInput).FullName!,
            OutputTypeName = typeof(object).FullName!,
            RequiredPolicies = [],
            RequiredRoles = [],
            HasAuthorizeAttribute = guarded,
            IsQuery = false,
            IsMutation = true,
            IsBroadcastEnabled = false,
            IsRemote = false,
            GraphQLOperations = GraphQLOperation.Run,
        };

    private Task<OperationResult> Run(Type train, string? json, CancellationToken ct = default) =>
        _service.RunTrainAsync(new RunTrainInput(train.FullName!, json), ct);

    private async Task<List<Trax.Effect.Models.Metadata.Metadata>> Runs()
    {
        using var context = (IDataContext)
            _provider.GetRequiredService<IDataContextProviderFactory>().Create();
        return await context.Metadatas.AsNoTracking().ToListAsync();
    }

    [Test]
    public async Task A_run_writes_a_pending_row_and_submits_it_with_the_input()
    {
        object? submitted = null;
        _submitter
            .EnqueueAsync(
                Arg.Any<long>(),
                Arg.Do<object>(input => submitted = input),
                Arg.Any<CancellationToken>()
            )
            .Returns("job-1");

        var result = await Run(typeof(IProbeTrain), "{\"customerId\":7}");

        result.Success.Should().BeTrue(result.Message);
        var run = (await Runs()).Should().ContainSingle().Subject;
        result.Id.Should().Be(run.Id, "the id is the run's metadata id, not a work queue id");
        run.Name.Should().Be(typeof(IProbeTrain).FullName);
        run.TrainState.Should().Be(TrainState.Pending);
        submitted.Should().BeEquivalentTo(new ProbeInput { CustomerId = 7 });
        await _submitter
            .Received(1)
            .EnqueueAsync(run.Id, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task A_blank_input_is_read_as_an_empty_object_as_queueing_reads_it()
    {
        var result = await Run(typeof(IProbeTrain), "  ");

        result.Success.Should().BeTrue(result.Message);
        await _submitter
            .Received(1)
            .EnqueueAsync(
                Arg.Any<long>(),
                Arg.Is<object>(input => input is ProbeInput),
                Arg.Any<CancellationToken>()
            );
    }

    [TestCase("{\"customerId\":7}", TestName = "Input_in_camel_case_is_read")]
    [TestCase("{\"CustomerId\":7}", TestName = "Input_in_pascal_case_is_read")]
    [TestCase("{\"CUSTOMERID\":7}", TestName = "Input_in_any_case_is_read")]
    public async Task Input_property_names_match_whatever_their_case(string json)
    {
        object? submitted = null;
        _submitter
            .EnqueueAsync(
                Arg.Any<long>(),
                Arg.Do<object>(input => submitted = input),
                Arg.Any<CancellationToken>()
            )
            .Returns("job-1");

        var result = await Run(typeof(IProbeTrain), json);

        result.Success.Should().BeTrue(result.Message);
        submitted
            .Should()
            .BeEquivalentTo(
                new ProbeInput { CustomerId = 7 },
                "a run reads a caller's input the way the mediator reads a queued one (docs/0023)"
            );
    }

    [TestCase("{\"customerId\":1,\"customerId\":2}", TestName = "A_repeated_property_is_refused")]
    [TestCase(
        "{\"customerId\":1,\"CustomerId\":2}",
        TestName = "A_property_repeated_in_another_case_is_refused"
    )]
    public async Task A_property_given_twice_is_a_failed_result(string json)
    {
        var result = await Run(typeof(IProbeTrain), json);

        result
            .Success.Should()
            .BeFalse("an ambiguous input is refused, never resolved to its last value (docs/0023)");
        result.Message.Should().StartWith("Invalid InputJson");
        (await Runs()).Should().BeEmpty();
        _submitter.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task The_callers_token_reaches_the_submitter()
    {
        using var cts = new CancellationTokenSource();

        await Run(typeof(IProbeTrain), "{}", cts.Token);

        await _submitter.Received(1).EnqueueAsync(Arg.Any<long>(), Arg.Any<object>(), cts.Token);
    }

    [Test]
    public async Task A_failed_submit_marks_the_run_failed_and_is_thrown()
    {
        var failure = new InvalidOperationException("the worker queue is unreachable");
        _submitter
            .EnqueueAsync(Arg.Any<long>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw failure);

        var act = async () => await Run(typeof(IProbeTrain), "{}");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should()
            .BeSameAs(failure, "a submit failure is the server's, not a refusal (scheduler/0004)");

        var run = (await Runs()).Should().ContainSingle().Subject;
        run.TrainState.Should()
            .Be(TrainState.Failed, "no job exists to move it out of Pending, so it is failed now");
        run.EndTime.Should().NotBeNull();
        run.FailureReason.Should().Contain("the worker queue is unreachable");
        _logger.Errors.Should().ContainSingle().Which.Should().BeSameAs(failure);
    }

    [Test]
    public async Task Malformed_input_is_a_failed_result_and_writes_no_run()
    {
        var result = await Run(typeof(IProbeTrain), "{not json");

        result.Success.Should().BeFalse();
        result.Message.Should().StartWith("Invalid InputJson");
        (await Runs()).Should().BeEmpty();
        _submitter.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task A_json_null_is_a_failed_result()
    {
        var result = await Run(typeof(IProbeTrain), "null");

        result.Success.Should().BeFalse();
        result.Message.Should().StartWith("Invalid InputJson");
        (await Runs()).Should().BeEmpty();
    }

    [Test]
    public async Task An_unknown_train_is_a_failed_result()
    {
        var result = await _service.RunTrainAsync(
            new RunTrainInput("No.Such.ITrain", "{}"),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Unknown train");
        (await Runs()).Should().BeEmpty();
    }

    [Test]
    public async Task A_missing_train_name_is_a_failed_result()
    {
        var result = await _service.RunTrainAsync(
            new RunTrainInput(" ", "{}"),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Be("TrainName is required.");
    }

    [Test]
    public async Task An_oversized_input_is_a_failed_result_and_writes_no_run()
    {
        var huge = "{\"customerId\":1,\"pad\":\"" + new string('x', 300_000) + "\"}";

        var result = await Run(typeof(IProbeTrain), huge);

        result.Success.Should().BeFalse();
        (await Runs()).Should().BeEmpty();
        _submitter.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task A_caller_the_enforcer_refuses_is_thrown_before_the_input_is_read()
    {
        _authorization = Substitute.For<ITrainAuthorizationService>();
        _authorization
            .AuthorizeAsync(Arg.Any<TrainRegistration>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new UnauthorizedAccessException("Not authorized."));
        Build();

        var act = async () => await Run(typeof(IGuardedTrain), "{not json");

        await act.Should()
            .ThrowAsync<UnauthorizedAccessException>(
                "authorization comes first, so a caller who may not run the train learns "
                    + "nothing about its input from a parse error"
            );
        (await Runs()).Should().BeEmpty();
        _submitter.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task A_guarded_train_with_no_enforcer_is_a_server_fault_not_a_refusal()
    {
        var act = async () => await Run(typeof(IGuardedTrain), "{}");

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("ITrainAuthorizationService");
        (await Runs()).Should().BeEmpty();
    }

    [Test]
    public async Task A_guarded_train_runs_inside_a_trusted_scope_with_no_enforcer()
    {
        var trusted = _provider.GetRequiredService<ITrustedExecutionScope>();
        OperationResult result;

        using (((TrustedExecutionScope)trusted).BeginTrusted("dashboard"))
            result = await Run(typeof(IGuardedTrain), "{}");

        result.Success.Should().BeTrue(result.Message);
        (await Runs()).Should().ContainSingle();
    }

    [Test]
    public async Task A_routed_train_goes_to_its_own_submitter()
    {
        var routed = new RecordingSubmitter();
        Build(services =>
        {
            var routing = new JobSubmitterRoutingConfiguration();
            routing.AddRoute(typeof(IRoutedTrain).FullName!, typeof(RecordingSubmitter));
            services.AddSingleton(routing);
            services.AddSingleton(routed);
        });

        var result = await Run(typeof(IRoutedTrain), "{}");

        result.Success.Should().BeTrue(result.Message);
        routed.Submitted.Should().ContainSingle().Which.Should().Be(result.Id!.Value);
        _submitter
            .ReceivedCalls()
            .Should()
            .BeEmpty("the job dispatcher would send this train to its routed submitter too");
    }

    [Test]
    public async Task A_service_built_without_a_provider_refuses_to_run()
    {
        var service = new OperationsService(
            Substitute.For<ITrainDiscoveryService>(),
            _provider.GetRequiredService<IDataContextProviderFactory>(),
            new SchedulerConfiguration(),
            Substitute.For<ITrainExecutionService>()
        );

        var act = async () =>
            await service.RunTrainAsync(
                new RunTrainInput(typeof(IProbeTrain).FullName!),
                CancellationToken.None
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    public sealed class RecordingSubmitter : IJobSubmitter
    {
        public List<long> Submitted { get; } = [];

        public Task<string> EnqueueAsync(long metadataId) => Record(metadataId);

        public Task<string> EnqueueAsync(long metadataId, object input) => Record(metadataId);

        private Task<string> Record(long metadataId)
        {
            Submitted.Add(metadataId);
            return Task.FromResult($"routed-{metadataId}");
        }
    }

    private sealed class CapturingLogger : ILogger<OperationsService>
    {
        public List<Exception> Errors { get; } = [];

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
        }
    }
}
