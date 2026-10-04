using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.RecordedDecision;
using Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory;
using Trax.Effect.Utils;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// <see cref="IOperationsService.RequeueExecutionAsync"/>, the one path the GraphQL
/// <c>requeueExecution</c> mutation and the dashboard's Re-queue button both take. It reads the
/// run, refuses a saved input that is not the input the run had, and queues the same train with
/// that input. The entry names the run as the one whose decisions it replays only when the run
/// recorded decisions, and nothing a caller passes can set that link.
///
/// <para>A host whose <c>ITrainExecutionService</c> cannot carry the link is misconfigured,
/// which <c>docs/adr/0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md</c>
/// says is logged and thrown rather than reported as a refusal.</para>
/// </summary>
[Property(
    "adr",
    "docs/adr/0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md"
)]
[TestFixture]
public class OperationsServiceRequeueTests : TestSetup
{
    private IOperationsService _operations = null!;

    [SetUp]
    public void GetService()
    {
        _operations = Scope.ServiceProvider.GetRequiredService<IOperationsService>();
    }

    private async Task<long> SeedRunAsync(
        string? input,
        string? trainName = null,
        long? replayDecisionsOf = null
    )
    {
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = trainName ?? typeof(ISchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ReplayDecisionsOf = replayDecisionsOf,
            }
        );
        run.Input = input;
        await DataContext.Track(run);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return run.Id;
    }

    private async Task RecordDecisionAsync(long metadataId)
    {
        DataContext.RecordedDecisions.Add(
            new RecordedDecision
            {
                MetadataId = metadataId,
                QuestionKey = "Route",
                Occurrence = 0,
                Fingerprint = new string('0', 64),
                Kind = "choice",
                Question = "{}",
                Answer = "\"Express\"",
                Routes = """[{"track": "Express", "fallback_reason": null}]""",
                Decider = "TestDecider",
                DecidedAt = DateTime.UtcNow,
            }
        );
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    [Test]
    public async Task A_run_that_recorded_decisions_is_requeued_to_replay_them()
    {
        var source = await SeedRunAsync("""{"Value":"again"}""");
        await RecordDecisionAsync(source);

        var result = await _operations.RequeueExecutionAsync(source, CancellationToken.None);

        result.Success.Should().BeTrue(result.Message);
        var entry = DataContext.WorkQueues.AsNoTracking().Single();
        result.Id.Should().Be(entry.Id);
        entry.TrainName.Should().Be(typeof(ISchedulerTestTrain).FullName);
        entry.Input.Should().Contain("again");
        entry
            .ReplayDecisionsOf.Should()
            .Be(source, "a re-queue repeats the run, so it takes the tracks the run took");
    }

    [Test]
    public async Task A_run_that_recorded_no_decisions_is_requeued_as_an_ordinary_enqueue()
    {
        var source = await SeedRunAsync("""{"Value":"again"}""");
        // Another run's decisions are not this run's.
        await RecordDecisionAsync(await SeedRunAsync("""{"Value":"other"}"""));

        var result = await _operations.RequeueExecutionAsync(source, CancellationToken.None);

        result.Success.Should().BeTrue(result.Message);
        DataContext
            .WorkQueues.AsNoTracking()
            .Single()
            .ReplayDecisionsOf.Should()
            .BeNull("there is nothing to replay, so the entry is the one a plain enqueue writes");
    }

    [Test]
    public async Task A_requeue_that_recorded_nothing_is_requeued_to_replay_what_it_replayed()
    {
        // A requeue of a requeue: the second run failed before its first question, so it recorded
        // nothing, but the answers of the run it replayed are still the ones to repeat. The new
        // entry names the second run, and the replay follows its link back to the first.
        var first = await SeedRunAsync("""{"Value":"again"}""");
        await RecordDecisionAsync(first);
        var second = await SeedRunAsync("""{"Value":"again"}""", replayDecisionsOf: first);

        var result = await _operations.RequeueExecutionAsync(second, CancellationToken.None);

        result.Success.Should().BeTrue(result.Message);
        DataContext
            .WorkQueues.AsNoTracking()
            .Single()
            .ReplayDecisionsOf.Should()
            .Be(second, "the run being requeued replayed decisions, so it has some to replay");
    }

    [Test]
    public async Task A_run_whose_train_is_no_longer_registered_is_refused_naming_the_train()
    {
        var source = await SeedRunAsync("""{"Value":"again"}""", "Some.Retired.ITrain");

        var result = await _operations.RequeueExecutionAsync(source, CancellationToken.None);

        result.Success.Should().BeFalse();
        result
            .Message.Should()
            .Be(
                $"Train Some.Retired.ITrain is no longer registered, so execution {source} "
                    + "cannot be re-queued."
            );
        DataContext.WorkQueues.AsNoTracking().Should().BeEmpty();
    }

    [Test]
    public async Task A_run_whose_saved_input_no_longer_reads_as_its_type_is_refused_as_the_runs_input()
    {
        // Saved when the input's Value was an object; the type now says it is a string. The
        // caller of a requeue supplied no InputJson, so the refusal names the run's saved input.
        var source = await SeedRunAsync("""{"Value":{"was":"an object"}}""");

        var result = await _operations.RequeueExecutionAsync(source, CancellationToken.None);

        result.Success.Should().BeFalse();
        result
            .Message.Should()
            .StartWith(
                $"The saved input of run {source} no longer reads as "
                    + $"{typeof(SchedulerTestInput).FullName}: "
            )
            .And.NotContain("InputJson");
        DataContext.WorkQueues.AsNoTracking().Should().BeEmpty();
    }

    [Test]
    public async Task An_operations_service_that_predates_requeue_says_so_naming_itself()
    {
        // The GraphQL error filter masks this exception for the client, as it masks every
        // exception it does not curate; the message is for the host's log.
        IOperationsService predates = new PredatesRequeue();

        var act = async () => await predates.RequeueExecutionAsync(1, CancellationToken.None);

        await act.Should()
            .ThrowAsync<NotSupportedException>()
            .WithMessage("PredatesRequeue does not implement RequeueExecutionAsync*");
    }

    /// <summary>
    /// An operations service written before RequeueExecutionAsync existed: it implements the
    /// members the interface requires and inherits the default for the rest.
    /// </summary>
    private sealed class PredatesRequeue : IOperationsService
    {
        public Task<OperationResult> QueueTrainAsync(QueueTrainInput input, CancellationToken ct) =>
            throw new InvalidOperationException();

        public Task<OperationResult> CancelWorkQueueEntryAsync(long id, CancellationToken ct) =>
            throw new InvalidOperationException();

        public Task<OperationResult> UpdateManifestGroupAsync(
            long id,
            UpdateManifestGroupInput input,
            CancellationToken ct
        ) => throw new InvalidOperationException();

        public Task<ManifestGroupDependencyGraph?> GetManifestGroupDependencyGraphAsync(
            long groupId,
            CancellationToken ct
        ) => throw new InvalidOperationException();

        public Task<ManifestGroupDependencyGraph> GetGlobalManifestGroupGraphAsync(
            CancellationToken ct
        ) => throw new InvalidOperationException();

        public Task<DashboardMetrics> GetDashboardMetricsAsync(
            MetricsRange range,
            bool hideAdminTrains,
            CancellationToken ct
        ) => throw new InvalidOperationException();

        public ServerMetrics GetServerMetrics() => throw new InvalidOperationException();

        public SchedulerConfigSnapshot GetSchedulerConfig() =>
            throw new InvalidOperationException();

        public Task<OperationResult> UpdateSchedulerConfigAsync(
            UpdateSchedulerConfigInput input,
            CancellationToken ct
        ) => throw new InvalidOperationException();
    }

    /// <summary>The input as SaveTrainParameters() saves it on a run, through the host's own effect.</summary>
    private async Task<string> SavedFormOfAsync(object input)
    {
        using var effect = Scope
            .ServiceProvider.GetRequiredService<ParameterEffectProviderFactory>()
            .Create();
        var metadata = new Metadata { Name = "saved-input" };
        metadata.SetInputObject(input);
        await effect.Track(metadata);
        await effect.SaveChanges(CancellationToken.None);
        return metadata.Input!;
    }

    [Test]
    public async Task A_run_is_requeued_with_the_input_it_saved_lists_and_shared_objects_included()
    {
        var shared = new SavedInputPlace { City = "Lyon", Zip = 69001 };
        var original = new SavedInputShapes
        {
            Counts = [3, 1, 4],
            Tags = ["alpha", "beta"],
            Home = shared,
            Billing = shared,
        };
        var saved = await SavedFormOfAsync(original);
        saved.Should().Contain("\"$values\"").And.Contain("\"$ref\"");
        var source = await SeedRunAsync(saved, typeof(ISavedInputShapesTrain).FullName);

        var result = await _operations.RequeueExecutionAsync(source, CancellationToken.None);

        result.Success.Should().BeTrue(result.Message);
        var entry = DataContext.WorkQueues.AsNoTracking().Single();
        var queued = JsonSerializer.Deserialize<SavedInputShapes>(
            entry.Input!,
            TraxJsonSerializationOptions.ManifestProperties
        )!;
        queued.Counts.Should().Equal(3, 1, 4);
        queued.Tags.Should().Equal("alpha", "beta");
        queued.Home.Should().Be(shared);
        queued
            .Billing.Should()
            .Be(shared, "the second copy of a shared object is saved as a $ref to the first");
    }

    [Test]
    public async Task A_run_whose_saved_input_repeats_an_object_is_not_requeued_with_defaults()
    {
        // No list, so nothing refuses the saved form outright: read as a caller's input, the $ref
        // standing in for the second copy reads back as an object at its defaults.
        var shared = new SavedInputPlace { City = "Nice", Zip = 6000 };
        var saved = await SavedFormOfAsync(
            new SavedInputShapes { Home = shared, Billing = shared }
        );
        var source = await SeedRunAsync(saved, typeof(ISavedInputShapesTrain).FullName);

        var result = await _operations.RequeueExecutionAsync(source, CancellationToken.None);

        result.Success.Should().BeTrue(result.Message);
        var entry = DataContext.WorkQueues.AsNoTracking().Single();
        JsonSerializer
            .Deserialize<SavedInputShapes>(
                entry.Input!,
                TraxJsonSerializationOptions.ManifestProperties
            )!
            .Billing.Should()
            .Be(shared);
    }

    [Test]
    public async Task An_unknown_run_is_reported_and_nothing_is_queued()
    {
        var result = await _operations.RequeueExecutionAsync(999_999_999, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("Execution 999999999 not found.");
        DataContext.WorkQueues.AsNoTracking().Should().BeEmpty();
    }

    [TestCase(null, "has no saved input to re-queue it with")]
    [TestCase("""{"_truncated": true, "_maxBytes": 1024}""", "was too large to save in full")]
    [TestCase(
        """{"_unserializable": true, "_error": "NotSupportedException"}""",
        "recorded as a _unserializable placeholder"
    )]
    [TestCase("""{"Value": "kept", "Token": {"_redacted": true}}""", "masked by [TraxSensitive]")]
    public async Task A_saved_input_that_is_not_the_input_the_run_had_is_refused(
        string? savedInput,
        string reason
    )
    {
        var source = await SeedRunAsync(savedInput);
        await RecordDecisionAsync(source);

        var result = await _operations.RequeueExecutionAsync(source, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().StartWith($"Execution {source}").And.Contain(reason);
        DataContext.WorkQueues.AsNoTracking().Should().BeEmpty();
    }

    [Test]
    public void The_queue_input_a_caller_supplies_has_no_replay_link()
    {
        // The replay link is set only by RequeueExecutionAsync, to the run being re-queued. A
        // caller-supplied queue input that carried it could point a run at any other run.
        typeof(QueueTrainInput)
            .GetProperties()
            .Select(p => p.Name)
            .Should()
            .NotContain(n => n.Contains("Replay", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task A_requeue_that_replays_through_an_execution_service_without_the_overload_is_a_misconfiguration()
    {
        var source = await SeedRunAsync("""{"Value":"again"}""");
        await RecordDecisionAsync(source);
        var logger = new CapturingLogger();
        var service = ServiceOver(new PredatesReplay(Execution), logger);

        var act = async () => await service.RequeueExecutionAsync(source, CancellationToken.None);

        var thrown = await act.Should()
            .ThrowAsync<DecisionReplayNotSupportedException>(
                "a host whose execution service cannot carry the replay link is misconfigured, "
                    + "which is not an answer about this re-queue (see "
                    + "docs/adr/0004-an-enqueue-refusal-is-a-result-an-infrastructure-failure-is-thrown.md)"
            );
        thrown.Which.ImplementationType.Should().Be(typeof(PredatesReplay));
        logger
            .Errors.Should()
            .ContainSingle("the misconfiguration is logged where an operator can see it")
            .Which.Should()
            .BeSameAs(thrown.Which);
        DataContext.WorkQueues.AsNoTracking().Should().BeEmpty();
    }

    [Test]
    public async Task A_requeue_with_nothing_to_replay_works_through_an_execution_service_without_the_overload()
    {
        var source = await SeedRunAsync("""{"Value":"again"}""");
        var service = ServiceOver(new PredatesReplay(Execution), new CapturingLogger());

        var result = await service.RequeueExecutionAsync(source, CancellationToken.None);

        result.Success.Should().BeTrue(result.Message);
        DataContext.WorkQueues.AsNoTracking().Single().ReplayDecisionsOf.Should().BeNull();
    }

    private ITrainExecutionService Execution =>
        Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

    private OperationsService ServiceOver(
        ITrainExecutionService execution,
        ILogger<OperationsService> logger
    ) =>
        new(
            Scope.ServiceProvider.GetRequiredService<ITrainDiscoveryService>(),
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>(),
            Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>(),
            execution,
            logger: logger
        );

    /// <summary>
    /// A decorator written before the options overload existed: it forwards the members it knows
    /// and inherits the interface's default for the overload that carries the replay link.
    /// </summary>
    private sealed class PredatesReplay(ITrainExecutionService inner) : ITrainExecutionService
    {
        public Task<QueueTrainResult> QueueAsync(
            string trainName,
            string? inputJson,
            int priority = 0,
            DateTime? scheduledAt = null,
            CancellationToken ct = default
        ) => inner.QueueAsync(trainName, inputJson, priority, scheduledAt, ct);

        public Task<RunTrainResult> RunAsync(
            string trainName,
            string inputJson,
            CancellationToken ct = default
        ) => inner.RunAsync(trainName, inputJson, ct);

        public Task<PreparedTrain> PrepareAsync(
            string trainName,
            string? inputJson,
            CancellationToken ct = default
        ) => inner.PrepareAsync(trainName, inputJson, ct);
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
