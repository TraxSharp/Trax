using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Tests.ArrayLogger.Services.ArrayLoggingProvider;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for DispatchJobsJunction failure handling.
/// Uses a custom IJobSubmitter that throws on EnqueueAsync to verify
/// that orphaned Pending metadata is immediately marked as Failed.
/// </summary>
/// <remarks>
/// This test class uses its own DI setup (not the shared TestSetup) because it needs
/// to register a FailingJobSubmitter instead of InMemoryJobSubmitter.
/// </remarks>
[TestFixture]
public class DispatchFailureHandlingTests
{
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;
    private IDataContext _dataContext = null!;
    private readonly LifecycleRecorder _lifecycle = new();

    [OneTimeSetUp]
    public async Task RunBeforeAnyTests()
    {
        var connectionString = TestPostgres.ConnectionString;

        var arrayLoggingProvider = new ArrayLoggingProvider();

        _serviceProvider = new ServiceCollection()
            .AddSingleton<ILoggerProvider>(arrayLoggingProvider)
            .AddSingleton<IArrayLoggingProvider>(arrayLoggingProvider)
            .AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug))
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .SetEffectLogLevel(LogLevel.Information)
                            .SaveTrainParameters()
                            .UsePostgres(connectionString)
                            .AddDataContextLogging(minimumLogLevel: LogLevel.Trace)
                            .AddJson()
                            .AddJunctionLogger(serializeJunctionData: true)
                            .AddLifecycleHook<RecordingLifecycleHook>()
                    )
                    .AddMediator(
                        typeof(AssemblyMarker).Assembly,
                        typeof(Scheduler.Trains.JobRunner.JobRunnerTrain).Assembly
                    )
                    .AddScheduler(scheduler =>
                        scheduler.OverrideSubmitter(s =>
                            s.AddScoped<IJobSubmitter, FailingJobSubmitter>()
                        )
                    )
            )
            .AddSingleton(_lifecycle)
            .AddScoped<IDataContext>(sp =>
            {
                var factory = sp.GetRequiredService<IDataContextProviderFactory>();
                return (IDataContext)factory.Create();
            })
            .BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests()
    {
        await _serviceProvider.DisposeAsync();
    }

    [SetUp]
    public async Task TestSetUp()
    {
        _scope = _serviceProvider.CreateScope();
        _dataContext = _scope.ServiceProvider.GetRequiredService<IDataContext>();
        await TestSetup.CleanupDatabase(_dataContext);
        _lifecycle.Clear();
    }

    [TearDown]
    public async Task TestTearDown()
    {
        if (_dataContext is IDisposable disposable)
            disposable.Dispose();
        _scope.Dispose();
    }

    [Test]
    public async Task Dispatch_WhenEnqueueFails_MetadataMarkedAsFailed()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var entry = await CreateAndSaveWorkQueueEntry(manifest);

        using var trainScope = _serviceProvider.CreateScope();
        var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();

        // Act — dispatch will call FailingJobSubmitter which throws
        await train.Run(Unit.Default);

        if (train is IDisposable d)
            d.Dispose();

        // Assert — Metadata should exist and be Failed
        _dataContext.Reset();
        var metadata = await _dataContext
            .Metadatas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ManifestId == manifest.Id);

        metadata.Should().NotBeNull("metadata should have been created before enqueue attempt");
        metadata!
            .TrainState.Should()
            .Be(TrainState.Failed, "metadata should be marked Failed on enqueue failure");
    }

    [Test]
    public async Task Dispatch_WhenEnqueueFails_MetadataHasEndTime()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        await CreateAndSaveWorkQueueEntry(manifest);

        using var trainScope = _serviceProvider.CreateScope();
        var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();

        // Act
        await train.Run(Unit.Default);

        if (train is IDisposable d)
            d.Dispose();

        // Assert
        _dataContext.Reset();
        var metadata = await _dataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.ManifestId == manifest.Id);

        metadata.EndTime.Should().NotBeNull("failed metadata should have an EndTime");
        metadata.EndTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task Dispatch_WhenEnqueueFails_MetadataHasExceptionDetails()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        await CreateAndSaveWorkQueueEntry(manifest);

        using var trainScope = _serviceProvider.CreateScope();
        var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();

        // Act
        await train.Run(Unit.Default);

        if (train is IDisposable d)
            d.Dispose();

        // Assert
        _dataContext.Reset();
        var metadata = await _dataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.ManifestId == manifest.Id);

        metadata.FailureReason.Should().Contain("Simulated enqueue failure");
    }

    [Test]
    public async Task Dispatch_WhenEnqueueFails_WorkQueueRequeuedForRetry()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var entry = await CreateAndSaveWorkQueueEntry(manifest);

        using var trainScope = _serviceProvider.CreateScope();
        var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();

        // Act
        await train.Run(Unit.Default);

        if (train is IDisposable d)
            d.Dispose();

        // Assert — with MaxDispatchAttempts > 0 (default: 5), the entry is requeued
        _dataContext.Reset();
        var updatedEntry = await _dataContext
            .WorkQueues.AsNoTracking()
            .FirstAsync(q => q.Id == entry.Id);

        updatedEntry.Status.Should().Be(WorkQueueStatus.Queued);
        updatedEntry.MetadataId.Should().BeNull();
        updatedEntry.DispatchedAt.Should().BeNull();
        updatedEntry.DispatchAttempts.Should().Be(1);
    }

    [Test]
    public async Task Dispatch_WhenEnqueueFails_OtherEntriesContinueDispatching()
    {
        // Arrange — multiple entries; each will fail but all should be attempted
        var manifest1 = await CreateAndSaveManifest(inputValue: "First");
        var entry1 = await CreateAndSaveWorkQueueEntry(manifest1);

        var manifest2 = await CreateAndSaveManifest(inputValue: "Second");
        var entry2 = await CreateAndSaveWorkQueueEntry(manifest2);

        var manifest3 = await CreateAndSaveManifest(inputValue: "Third");
        var entry3 = await CreateAndSaveWorkQueueEntry(manifest3);

        using var trainScope = _serviceProvider.CreateScope();
        var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();

        // Act
        await train.Run(Unit.Default);

        if (train is IDisposable d)
            d.Dispose();

        // Assert — all entries should have been attempted (metadata created for each)
        _dataContext.Reset();
        var metadataCount = await _dataContext
            .Metadatas.AsNoTracking()
            .Where(m =>
                new[] { manifest1.Id, manifest2.Id, manifest3.Id }.Contains(m.ManifestId!.Value)
            )
            .CountAsync();

        metadataCount
            .Should()
            .Be(3, "all entries should be attempted even when individual dispatches fail");

        // All metadata should be Failed
        var allMetadata = await _dataContext
            .Metadatas.AsNoTracking()
            .Where(m =>
                new[] { manifest1.Id, manifest2.Id, manifest3.Id }.Contains(m.ManifestId!.Value)
            )
            .ToListAsync();

        allMetadata
            .Should()
            .AllSatisfy(m =>
            {
                m.TrainState.Should().Be(TrainState.Failed);
                m.FailureReason.Should().Contain("Simulated enqueue failure");
            });
    }

    [Test]
    public async Task Dispatch_WhenEnqueueFails_DoesNotThrow()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        await CreateAndSaveWorkQueueEntry(manifest);

        using var trainScope = _serviceProvider.CreateScope();
        var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();

        // Act & Assert — the dispatcher should not throw; errors are handled gracefully
        var act = async () => await train.Run(Unit.Default);
        await act.Should().NotThrowAsync("dispatch failures should be handled gracefully");

        if (train is IDisposable d)
            d.Dispose();
    }

    [Test]
    public async Task Dispatch_WhenEnqueueFails_MetadataNotCountedAsActive()
    {
        // Arrange — Create and fail a dispatch, then verify the metadata is not Pending
        // (so it doesn't block MaxActiveJobs capacity)
        var manifest = await CreateAndSaveManifest();
        await CreateAndSaveWorkQueueEntry(manifest);

        using var trainScope = _serviceProvider.CreateScope();
        var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();

        // Act
        await train.Run(Unit.Default);

        if (train is IDisposable d)
            d.Dispose();

        // Assert — no Pending metadata should exist
        _dataContext.Reset();
        var pendingCount = await _dataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.ManifestId == manifest.Id && m.TrainState == TrainState.Pending)
            .CountAsync();

        pendingCount
            .Should()
            .Be(
                0,
                "failed dispatch should not leave orphaned Pending metadata that blocks capacity"
            );
    }

    #region Failed Deliveries And Retries

    [Test]
    public async Task Three_failed_dispatch_cycles_do_not_dead_letter_the_manifest_while_attempts_remain()
    {
        // Arrange - MaxRetries 3, MaxDispatchAttempts 5 (default)
        var manifest = await CreateAndSaveManifest();
        var entry = await CreateAndSaveWorkQueueEntry(manifest);

        // Act - three cycles in which the submitter cannot reach its runner
        for (var i = 0; i < 3; i++)
        {
            await RunDispatcherCycle();
            await ElapseDispatchBackoff();
            await RunManifestManagerCycle();
        }

        // Assert - the job has not failed, only three deliveries of it have
        _dataContext.Reset();
        var deadLetters = await _dataContext
            .DeadLetters.AsNoTracking()
            .CountAsync(d => d.ManifestId == manifest.Id);
        deadLetters.Should().Be(0, "dispatch attempts remain, so the job has not failed yet");

        var queued = await _dataContext.WorkQueues.AsNoTracking().FirstAsync(q => q.Id == entry.Id);
        queued.Status.Should().Be(WorkQueueStatus.Queued);
        queued.DispatchAttempts.Should().Be(3);

        var attempts = await _dataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.ManifestId == manifest.Id)
            .ToListAsync();
        attempts.Should().HaveCount(3);
        attempts
            .Should()
            .AllSatisfy(m =>
            {
                m.TrainState.Should().Be(TrainState.Failed, "each attempt is still recorded");
                m.FailureException.Should().Be(DispatchFailure.Requeued);
                m.FailureReason.Should().Contain("Simulated enqueue failure");
            });
    }

    [Test]
    public async Task A_requeued_entry_waits_out_a_backoff_before_its_next_dispatch()
    {
        var manifest = await CreateAndSaveManifest();
        var entry = await CreateAndSaveWorkQueueEntry(manifest);

        await RunDispatcherCycle();
        await RunDispatcherCycle();

        _dataContext.Reset();
        var queued = await _dataContext.WorkQueues.AsNoTracking().FirstAsync(q => q.Id == entry.Id);
        queued.DispatchAttempts.Should().Be(1, "the second cycle came before the backoff ended");
        queued.ScheduledAt.Should().BeAfter(DateTime.UtcNow);
        queued
            .ScheduledAt.Should()
            .BeCloseTo(DateTime.UtcNow + DispatchFailure.FirstBackoff, TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task The_attempt_that_exhausts_dispatch_attempts_counts_as_a_failure()
    {
        var config = _serviceProvider.GetRequiredService<SchedulerConfiguration>();
        var originalMax = config.MaxDispatchAttempts;
        config.MaxDispatchAttempts = 2;

        try
        {
            var manifest = await CreateAndSaveManifest();
            await CreateAndSaveWorkQueueEntry(manifest);

            await RunDispatcherCycle();
            await ElapseDispatchBackoff();
            await RunDispatcherCycle();

            _dataContext.Reset();
            var attempts = await _dataContext
                .Metadatas.AsNoTracking()
                .Where(m => m.ManifestId == manifest.Id)
                .OrderBy(m => m.Id)
                .ToListAsync();

            attempts.Should().HaveCount(2);
            attempts[0].FailureException.Should().Be(DispatchFailure.Requeued);
            attempts[1]
                .FailureException.Should()
                .Be(nameof(HttpRequestException), "the last attempt is the job's failure");
        }
        finally
        {
            config.MaxDispatchAttempts = originalMax;
        }
    }

    [Test]
    public async Task A_run_that_fails_dispatch_for_good_publishes_its_failure_once()
    {
        var config = _serviceProvider.GetRequiredService<SchedulerConfiguration>();
        var originalMax = config.MaxDispatchAttempts;
        config.MaxDispatchAttempts = 2;

        try
        {
            var manifest = await CreateAndSaveManifest();
            var entry = await CreateAndSaveWorkQueueEntry(manifest);

            await RunDispatcherCycle();
            _lifecycle
                .Events.Should()
                .BeEmpty("a requeued attempt is not the run's outcome: the entry runs again");

            await ElapseDispatchBackoff();
            await RunDispatcherCycle();

            _dataContext.Reset();
            var failed = await _dataContext
                .Metadatas.AsNoTracking()
                .Where(m => m.ManifestId == manifest.Id)
                .OrderByDescending(m => m.Id)
                .FirstAsync();
            _lifecycle
                .Events.Should()
                .Equal(
                    ("Failed", failed.Id, entry.ExternalId, TrainState.Failed),
                    ("StateChanged", failed.Id, entry.ExternalId, TrainState.Failed)
                );
        }
        finally
        {
            config.MaxDispatchAttempts = originalMax;
        }
    }

    [Test]
    public async Task A_dispatch_failure_with_requeue_off_publishes_the_failure()
    {
        var config = _serviceProvider.GetRequiredService<SchedulerConfiguration>();
        var originalMax = config.MaxDispatchAttempts;
        config.MaxDispatchAttempts = 0;

        try
        {
            var manifest = await CreateAndSaveManifest();
            var entry = await CreateAndSaveWorkQueueEntry(manifest);

            await RunDispatcherCycle();

            _lifecycle
                .Events.Select(e => (e.Event, e.ExternalId, e.State))
                .Should()
                .Equal(
                    ("Failed", entry.ExternalId, TrainState.Failed),
                    ("StateChanged", entry.ExternalId, TrainState.Failed)
                );
        }
        finally
        {
            config.MaxDispatchAttempts = originalMax;
        }
    }

    [Test]
    public async Task An_entry_whose_input_cannot_be_read_publishes_its_failed_run()
    {
        var manifest = await CreateAndSaveManifest();
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName!,
                Input = """{"value":{"not":"a string"}}""",
                InputTypeName = typeof(SchedulerTestInput).AssemblyQualifiedName,
                ManifestId = manifest.Id,
            }
        );
        await _dataContext.Track(entry);
        await _dataContext.SaveChanges(CancellationToken.None);
        _dataContext.Reset();

        await RunDispatcherCycle();

        _lifecycle
            .Events.Select(e => (e.Event, e.ExternalId, e.State))
            .Should()
            .Equal(
                ("Failed", entry.ExternalId, TrainState.Failed),
                ("StateChanged", entry.ExternalId, TrainState.Failed)
            );
    }

    [Test]
    public async Task A_dispatch_failure_is_published_without_the_runners_detail()
    {
        var config = _serviceProvider.GetRequiredService<SchedulerConfiguration>();
        var originalMax = config.MaxDispatchAttempts;
        config.MaxDispatchAttempts = 0;

        try
        {
            var manifest = await CreateAndSaveManifest();
            await CreateAndSaveWorkQueueEntry(manifest);

            await RunDispatcherCycle();

            _dataContext.Reset();
            var row = await _dataContext
                .Metadatas.AsNoTracking()
                .FirstAsync(m => m.ManifestId == manifest.Id);
            row.FailureReason.Should()
                .Contain("Simulated enqueue failure", "the row keeps the detail for operators");

            _lifecycle.Failures.Should().NotBeEmpty();
            _lifecycle
                .Failures.Should()
                .AllSatisfy(f =>
                {
                    f.FailureException.Should()
                        .Be("DispatchFailed")
                        .And.NotBe("TrainException", "a subscriber's mask keys on it");
                    f.FailureReason.Should().NotContain("Simulated enqueue failure");
                    f.FailureReason.Should().NotContain("remote worker");
                    f.StackTrace.Should().BeNull();
                    (f.ExceptionMessage ?? "").Should().NotContain("Simulated enqueue failure");
                });
        }
        finally
        {
            config.MaxDispatchAttempts = originalMax;
        }
    }

    [Test]
    public async Task An_unreadable_input_is_published_without_the_type_or_serializer_detail()
    {
        var manifest = await CreateAndSaveManifest();
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName!,
                Input = """{"value":{"not":"a string"}}""",
                InputTypeName = typeof(SchedulerTestInput).AssemblyQualifiedName,
                ManifestId = manifest.Id,
            }
        );
        await _dataContext.Track(entry);
        await _dataContext.SaveChanges(CancellationToken.None);
        _dataContext.Reset();

        await RunDispatcherCycle();

        _dataContext.Reset();
        var row = await _dataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.ManifestId == manifest.Id);
        row.FailureReason.Should().Contain(typeof(SchedulerTestInput).AssemblyQualifiedName!);

        _lifecycle.Failures.Should().NotBeEmpty();
        _lifecycle
            .Failures.Should()
            .AllSatisfy(f =>
            {
                f.FailureException.Should().Be("DispatchFailed");
                f.FailureReason.Should().NotContain(nameof(SchedulerTestInput));
                f.FailureReason.Should().NotContain("JSON");
                (f.ExceptionMessage ?? "").Should().NotContain(nameof(SchedulerTestInput));
            });
    }

    [Test]
    public async Task A_hook_that_hangs_on_a_dispatch_failure_does_not_stall_dispatch()
    {
        var config = _serviceProvider.GetRequiredService<SchedulerConfiguration>();
        var originalMax = config.MaxDispatchAttempts;
        var originalTimeout = Scheduler
            .Trains
            .JobDispatcher
            .Junctions
            .DispatchJobsJunction
            .FailurePublishTimeout;
        config.MaxDispatchAttempts = 0;
        Scheduler.Trains.JobDispatcher.Junctions.DispatchJobsJunction.FailurePublishTimeout =
            TimeSpan.FromMilliseconds(500);
        _lifecycle.HangOnFailed = true;

        try
        {
            var manifest1 = await CreateAndSaveManifest(inputValue: "First");
            await CreateAndSaveWorkQueueEntry(manifest1);
            var manifest2 = await CreateAndSaveManifest(inputValue: "Second");
            await CreateAndSaveWorkQueueEntry(manifest2);

            // A bound, not a wait: a hook that never returns would otherwise hold the cycle forever.
            var cycle = () => RunDispatcherCycle().WaitAsync(TimeSpan.FromSeconds(20));

            await cycle
                .Should()
                .NotThrowAsync("a hook that never returns is abandoned after the timeout");

            _dataContext.Reset();
            var failed = await _dataContext
                .Metadatas.AsNoTracking()
                .Where(m => m.ManifestId == manifest1.Id || m.ManifestId == manifest2.Id)
                .CountAsync(m => m.TrainState == TrainState.Failed);
            failed.Should().Be(2, "the entry behind the hung publish is still dispatched");
        }
        finally
        {
            _lifecycle.HangOnFailed = false;
            Scheduler.Trains.JobDispatcher.Junctions.DispatchJobsJunction.FailurePublishTimeout =
                originalTimeout;
            config.MaxDispatchAttempts = originalMax;
        }
    }

    private async Task RunDispatcherCycle()
    {
        using var trainScope = _serviceProvider.CreateScope();
        var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();
        await train.Run(Unit.Default);
    }

    private async Task RunManifestManagerCycle()
    {
        using var trainScope = _serviceProvider.CreateScope();
        var train = trainScope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
        await train.Run(Unit.Default);
    }

    #endregion

    #region Requeue Behavior

    [Test]
    public async Task Dispatch_WhenEnqueueFails_DispatchAttemptsIncremented()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        var entry = await CreateAndSaveWorkQueueEntry(manifest);

        // Act — run dispatcher twice, both times will fail
        for (var i = 0; i < 2; i++)
        {
            using var trainScope = _serviceProvider.CreateScope();
            var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();
            await train.Run(Unit.Default);
            if (train is IDisposable d)
                d.Dispose();
            await ElapseDispatchBackoff();
        }

        // Assert — dispatch_attempts should be 2 after two failed attempts
        _dataContext.Reset();
        var updatedEntry = await _dataContext
            .WorkQueues.AsNoTracking()
            .FirstAsync(q => q.Id == entry.Id);

        updatedEntry.DispatchAttempts.Should().Be(2);
        updatedEntry.Status.Should().Be(WorkQueueStatus.Queued);
    }

    [Test]
    public async Task Dispatch_WhenEnqueueFails_CreatesNewMetadataEachAttempt()
    {
        // Arrange
        var manifest = await CreateAndSaveManifest();
        await CreateAndSaveWorkQueueEntry(manifest);

        // Act — run dispatcher twice
        for (var i = 0; i < 2; i++)
        {
            using var trainScope = _serviceProvider.CreateScope();
            var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();
            await train.Run(Unit.Default);
            if (train is IDisposable d)
                d.Dispose();
            await ElapseDispatchBackoff();
        }

        // Assert — should have 2 separate Failed metadata rows (one per attempt)
        _dataContext.Reset();
        var metadataRecords = await _dataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.ManifestId == manifest.Id)
            .ToListAsync();

        metadataRecords.Should().HaveCount(2);
        metadataRecords.Should().AllSatisfy(m => m.TrainState.Should().Be(TrainState.Failed));
    }

    [Test]
    public async Task Dispatch_WhenMaxDispatchAttemptsExhausted_StaysDispatched()
    {
        // Arrange — set MaxDispatchAttempts to 2 via the configuration
        var config = _serviceProvider.GetRequiredService<SchedulerConfiguration>();
        var originalMax = config.MaxDispatchAttempts;
        config.MaxDispatchAttempts = 2;

        try
        {
            var manifest = await CreateAndSaveManifest();
            var entry = await CreateAndSaveWorkQueueEntry(manifest);

            // Act — run dispatcher 3 times (exceeds max of 2)
            for (var i = 0; i < 3; i++)
            {
                using var trainScope = _serviceProvider.CreateScope();
                var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();
                await train.Run(Unit.Default);
                if (train is IDisposable d)
                    d.Dispose();
                await ElapseDispatchBackoff();
            }

            // Assert — after exhausting attempts, entry stays Dispatched
            _dataContext.Reset();
            var updatedEntry = await _dataContext
                .WorkQueues.AsNoTracking()
                .FirstAsync(q => q.Id == entry.Id);

            updatedEntry.DispatchAttempts.Should().Be(2);
            updatedEntry.Status.Should().Be(WorkQueueStatus.Dispatched);

            // Should have exactly 2 metadata rows (not 3, because third attempt wasn't dispatched)
            var metadataCount = await _dataContext
                .Metadatas.AsNoTracking()
                .Where(m => m.ManifestId == manifest.Id)
                .CountAsync();
            metadataCount.Should().Be(2);
        }
        finally
        {
            config.MaxDispatchAttempts = originalMax;
        }
    }

    [Test]
    public async Task Dispatch_WhenMaxDispatchAttemptsZero_DoesNotRequeue()
    {
        // Arrange — disable requeue (pre-1.2.0 behavior)
        var config = _serviceProvider.GetRequiredService<SchedulerConfiguration>();
        var originalMax = config.MaxDispatchAttempts;
        config.MaxDispatchAttempts = 0;

        try
        {
            var manifest = await CreateAndSaveManifest();
            var entry = await CreateAndSaveWorkQueueEntry(manifest);

            using var trainScope = _serviceProvider.CreateScope();
            var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();
            await train.Run(Unit.Default);
            if (train is IDisposable d)
                d.Dispose();

            // Assert — entry stays Dispatched (no requeue)
            _dataContext.Reset();
            var updatedEntry = await _dataContext
                .WorkQueues.AsNoTracking()
                .FirstAsync(q => q.Id == entry.Id);

            updatedEntry.Status.Should().Be(WorkQueueStatus.Dispatched);
            updatedEntry.MetadataId.Should().NotBeNull();
            updatedEntry.DispatchAttempts.Should().Be(0);
        }
        finally
        {
            config.MaxDispatchAttempts = originalMax;
        }
    }

    #endregion

    #region Helper Methods

    private async Task<Manifest> CreateAndSaveManifest(string inputValue = "TestValue")
    {
        var group = await TestSetup.CreateAndSaveManifestGroup(
            _dataContext,
            name: $"group-{Guid.NewGuid():N}"
        );

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = inputValue },
            }
        );
        manifest.ManifestGroupId = group.Id;

        await _dataContext.Track(manifest);
        await _dataContext.SaveChanges(CancellationToken.None);
        _dataContext.Reset();

        return manifest;
    }

    /// <summary>
    /// Stands in for time passing: a requeued entry waits out its dispatch backoff before the
    /// next cycle can pick it up.
    /// </summary>
    private async Task ElapseDispatchBackoff()
    {
        _dataContext.Reset();
        await _dataContext
            .WorkQueues.Where(q => q.ScheduledAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.ScheduledAt, (DateTime?)null));
    }

    private async Task<WorkQueue> CreateAndSaveWorkQueueEntry(Manifest manifest)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName!,
                Input = manifest.Properties,
                InputTypeName = typeof(SchedulerTestInput).AssemblyQualifiedName,
                ManifestId = manifest.Id,
            }
        );

        await _dataContext.Track(entry);
        await _dataContext.SaveChanges(CancellationToken.None);
        _dataContext.Reset();

        return entry;
    }

    #endregion

    #region Lifecycle Recording

    /// <summary>
    /// The lifecycle events of the scheduled train's runs, in order, across every scope. The
    /// dispatcher and manifest manager are trains too, and their own events are left out.
    /// </summary>
    private sealed class LifecycleRecorder
    {
        private readonly List<(
            string Event,
            long Id,
            string ExternalId,
            TrainState State
        )> _events = [];

        public IReadOnlyList<(string Event, long Id, string ExternalId, TrainState State)> Events
        {
            get
            {
                lock (_events)
                    return _events.ToList();
            }
        }

        public void Add(string name, Metadata metadata)
        {
            if (metadata.Name != typeof(SchedulerTestTrain).FullName)
                return;
            lock (_events)
                _events.Add((name, metadata.Id, metadata.ExternalId, metadata.TrainState));
        }

        private readonly List<(
            string? FailureException,
            string? FailureReason,
            string? StackTrace,
            string? ExceptionMessage
        )> _failures = [];

        /// <summary>
        /// What each failure event of the scheduled train's runs carried, as a subscriber sees it.
        /// </summary>
        public IReadOnlyList<(
            string? FailureException,
            string? FailureReason,
            string? StackTrace,
            string? ExceptionMessage
        )> Failures
        {
            get
            {
                lock (_failures)
                    return _failures.ToList();
            }
        }

        /// <summary>When set, <c>OnFailed</c> never returns and ignores its token.</summary>
        public volatile bool HangOnFailed;

        public void AddFailure(Metadata metadata, Exception? exception)
        {
            if (
                metadata.Name != typeof(SchedulerTestTrain).FullName
                || metadata.TrainState != TrainState.Failed
            )
                return;
            lock (_failures)
                _failures.Add(
                    (
                        metadata.FailureException,
                        metadata.FailureReason,
                        metadata.StackTrace,
                        exception?.Message
                    )
                );
        }

        public void Clear()
        {
            lock (_events)
                _events.Clear();
            lock (_failures)
                _failures.Clear();
        }
    }

    private sealed class RecordingLifecycleHook(LifecycleRecorder recorder) : ITrainLifecycleHook
    {
        public Task OnStarted(Metadata metadata, CancellationToken ct) =>
            Record("Started", metadata);

        public Task OnCompleted(Metadata metadata, CancellationToken ct) =>
            Record("Completed", metadata);

        public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
        {
            recorder.AddFailure(metadata, exception);
            if (recorder.HangOnFailed && metadata.Name == typeof(SchedulerTestTrain).FullName)
                return new TaskCompletionSource().Task;
            return Record("Failed", metadata);
        }

        public Task OnCancelled(Metadata metadata, CancellationToken ct) =>
            Record("Cancelled", metadata);

        public Task OnStateChanged(Metadata metadata, CancellationToken ct)
        {
            recorder.AddFailure(metadata, null);
            return Record("StateChanged", metadata);
        }

        private Task Record(string name, Metadata metadata)
        {
            recorder.Add(name, metadata);
            return Task.CompletedTask;
        }
    }

    #endregion

    #region Failing Job Submitter

    /// <summary>
    /// A job submitter that always throws, simulating a remote worker failure
    /// (network timeout, HTTP 5xx, Lambda unreachable, etc.).
    /// </summary>
    private class FailingJobSubmitter : IJobSubmitter
    {
        public Task<string> EnqueueAsync(long metadataId) =>
            throw new HttpRequestException("Simulated enqueue failure: remote worker unreachable");

        public Task<string> EnqueueAsync(long metadataId, object input) =>
            throw new HttpRequestException("Simulated enqueue failure: remote worker unreachable");

        public Task<string> EnqueueAsync(long metadataId, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Simulated enqueue failure: remote worker unreachable");

        public Task<string> EnqueueAsync(
            long metadataId,
            object input,
            CancellationToken cancellationToken
        ) => throw new HttpRequestException("Simulated enqueue failure: remote worker unreachable");
    }

    #endregion
}
