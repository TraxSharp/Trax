using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StepProvider.Logging.Extensions;
using Trax.Effect.Tests.ArrayLogger.Services.ArrayLoggingProvider;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Tests.Integration.Examples.Trains;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.TaskServerExecutor;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for the MaxActiveJobs configuration that limits the total number
/// of active (Pending + InProgress Metadata) jobs dispatched by the JobDispatcher.
/// </summary>
/// <remarks>
/// These tests verify that the JobDispatcher enforces MaxActiveJobs at dispatch time:
/// - Only Pending + InProgress Metadata count toward the limit
/// - Queued WorkQueue entries are the buffer — they do NOT count toward the limit
/// - The JobDispatcher dispatches up to (MaxActiveJobs - activeCount) entries per cycle
/// - Completed and Failed Metadata do not count toward the limit
/// </remarks>
[TestFixture]
public class MaxActiveJobsTests
{
    private const int MaxActiveJobsLimit = 5;

    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;
    private IJobDispatcherTrain _train = null!;
    private IDataContext _dataContext = null!;

    [OneTimeSetUp]
    public async Task RunBeforeAnyTests()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
        var connectionString = configuration.GetRequiredSection("Configuration")[
            "DatabaseConnectionString"
        ]!;

        var arrayLoggingProvider = new ArrayLoggingProvider();

        _serviceProvider = new ServiceCollection()
            .AddSingleton<ILoggerProvider>(arrayLoggingProvider)
            .AddSingleton<IArrayLoggingProvider>(arrayLoggingProvider)
            .AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug))
            .AddTraxEffects(options =>
                options
                    .AddServiceTrainBus(
                        assemblies:
                        [
                            typeof(AssemblyMarker).Assembly,
                            typeof(TaskServerExecutorTrain).Assembly,
                        ]
                    )
                    .SetEffectLogLevel(LogLevel.Information)
                    .SaveTrainParameters()
                    .AddPostgresEffect(connectionString)
                    .AddEffectDataContextLogging(minimumLogLevel: LogLevel.Trace)
                    .AddJsonEffect()
                    .AddStepLogger(serializeStepData: true)
                    .AddScheduler(scheduler =>
                        scheduler.UseInMemoryTaskServer().MaxActiveJobs(MaxActiveJobsLimit)
                    )
            )
            .AddScoped<IDataContext>(sp =>
            {
                var factory = sp.GetRequiredService<IDataContextProviderFactory>();
                return (IDataContext)factory.Create();
            })
            .BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests() => await _serviceProvider.DisposeAsync();

    [SetUp]
    public async Task TestSetUp()
    {
        _scope = _serviceProvider.CreateScope();
        _train = _scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();
        _dataContext = _scope.ServiceProvider.GetRequiredService<IDataContext>();

        await TestSetup.CleanupDatabase(_dataContext);
    }

    [TearDown]
    public async Task TestTearDown()
    {
        if (_train is IDisposable disposable)
            disposable.Dispose();

        if (_dataContext is IDisposable dataContextDisposable)
            dataContextDisposable.Dispose();

        _scope.Dispose();
    }

    #region Basic MaxActiveJobs Limit Tests

    [Test]
    public async Task Run_WhenTotalActiveJobsAtLimit_DoesNotDispatchNewJobs()
    {
        // Arrange - Create active metadata at the limit
        for (var i = 0; i < MaxActiveJobsLimit; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Active_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.Pending);
        }

        // Create queued work queue entries that should NOT be dispatched
        // Each entry needs its own manifest (unique partial index: one Queued per manifest)
        var queuedEntries = new List<WorkQueue>();
        for (var i = 0; i < 3; i++)
        {
            var queueManifest = await CreateAndSaveManifest(inputValue: $"Queued_{i}");
            queuedEntries.Add(await CreateAndSaveWorkQueueEntry(queueManifest));
        }

        // Act
        await _train.Run(Unit.Default);

        // Assert - No entries should be dispatched
        _dataContext.Reset();
        foreach (var entry in queuedEntries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            updated.Status.Should().Be(WorkQueueStatus.Queued, "active jobs are at limit");
        }
    }

    [Test]
    public async Task Run_WhenTotalActiveJobsBelowLimit_DispatchesJobs()
    {
        // Arrange - 3 active metadata, limit is 5 → 2 slots available
        for (var i = 0; i < 3; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Active_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.Pending);
        }

        // Create 3 queued entries — only 2 should be dispatched
        var entries = new List<WorkQueue>();
        for (var i = 0; i < 3; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Queued_{i}");
            entries.Add(await CreateAndSaveWorkQueueEntry(manifest));
        }

        // Act
        await _train.Run(Unit.Default);

        // Assert - Exactly 2 dispatched (5 - 3 = 2 slots)
        _dataContext.Reset();
        var dispatchedCount = 0;
        foreach (var entry in entries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            if (updated.Status == WorkQueueStatus.Dispatched)
                dispatchedCount++;
        }

        dispatchedCount.Should().Be(2, "there were 2 available slots (limit 5 - 3 active)");
    }

    [Test]
    public async Task Run_WhenExactlyOneSlotAvailable_DispatchesExactlyOneJob()
    {
        // Arrange - Fill all but one slot
        for (var i = 0; i < MaxActiveJobsLimit - 1; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Active_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.Pending);
        }

        // Create multiple queued entries
        var entries = new List<WorkQueue>();
        for (var i = 0; i < 3; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Queued_{i}");
            entries.Add(await CreateAndSaveWorkQueueEntry(manifest));
        }

        // Act
        await _train.Run(Unit.Default);

        // Assert - Exactly 1 dispatched
        _dataContext.Reset();
        var dispatchedCount = 0;
        foreach (var entry in entries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            if (updated.Status == WorkQueueStatus.Dispatched)
                dispatchedCount++;
        }

        dispatchedCount.Should().Be(1, "only one slot was available");
    }

    #endregion

    #region Pending + InProgress Combined Limit Tests

    [Test]
    public async Task Run_CountsBothPendingAndInProgressTowardLimit()
    {
        // Arrange - Mix of Pending and InProgress filling the limit
        var pendingCount = 2;
        var inProgressCount = MaxActiveJobsLimit - pendingCount;

        for (var i = 0; i < pendingCount; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Pending_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.Pending);
        }

        for (var i = 0; i < inProgressCount; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"InProgress_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.InProgress);
        }

        // Create queued entries (each needs its own manifest — unique partial index)
        var entries = new List<WorkQueue>();
        for (var i = 0; i < 2; i++)
        {
            var queueManifest = await CreateAndSaveManifest(inputValue: $"Queued_{i}");
            entries.Add(await CreateAndSaveWorkQueueEntry(queueManifest));
        }

        // Act
        await _train.Run(Unit.Default);

        // Assert - No entries dispatched
        _dataContext.Reset();
        foreach (var entry in entries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            updated
                .Status.Should()
                .Be(
                    WorkQueueStatus.Queued,
                    $"Pending ({pendingCount}) + InProgress ({inProgressCount}) = {MaxActiveJobsLimit} is at limit"
                );
        }
    }

    [Test]
    public async Task Run_OnlyInProgressJobs_CountsTowardLimit()
    {
        // Arrange - Fill limit with only InProgress jobs
        for (var i = 0; i < MaxActiveJobsLimit; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"InProgress_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.InProgress);
        }

        var entries = new List<WorkQueue>();
        for (var i = 0; i < 2; i++)
        {
            var queueManifest = await CreateAndSaveManifest(inputValue: $"Queued_{i}");
            entries.Add(await CreateAndSaveWorkQueueEntry(queueManifest));
        }

        // Act
        await _train.Run(Unit.Default);

        // Assert
        _dataContext.Reset();
        foreach (var entry in entries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            updated
                .Status.Should()
                .Be(WorkQueueStatus.Queued, "InProgress jobs count toward the limit");
        }
    }

    [Test]
    public async Task Run_CompletedJobsDoNotCountTowardLimit()
    {
        // Arrange - Create many completed jobs — these should NOT count
        for (var i = 0; i < MaxActiveJobsLimit + 5; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Completed_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.Completed);
        }

        // Create queued entries — all should be dispatched since no active jobs
        var entries = new List<WorkQueue>();
        for (var i = 0; i < 3; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Queued_{i}");
            entries.Add(await CreateAndSaveWorkQueueEntry(manifest));
        }

        // Act
        await _train.Run(Unit.Default);

        // Assert - All 3 dispatched
        _dataContext.Reset();
        foreach (var entry in entries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            updated
                .Status.Should()
                .Be(
                    WorkQueueStatus.Dispatched,
                    "Completed jobs do not count toward MaxActiveJobs limit"
                );
        }
    }

    [Test]
    public async Task Run_FailedJobsDoNotCountTowardLimit()
    {
        // Arrange - Create many failed jobs — these should NOT count
        for (var i = 0; i < MaxActiveJobsLimit + 5; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Failed_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.Failed);
        }

        // Create queued entries — all should be dispatched
        var entries = new List<WorkQueue>();
        for (var i = 0; i < 3; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Queued_{i}");
            entries.Add(await CreateAndSaveWorkQueueEntry(manifest));
        }

        // Act
        await _train.Run(Unit.Default);

        // Assert - All 3 dispatched
        _dataContext.Reset();
        foreach (var entry in entries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            updated
                .Status.Should()
                .Be(
                    WorkQueueStatus.Dispatched,
                    "Failed jobs do not count toward MaxActiveJobs limit"
                );
        }
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task Run_WhenNoActiveJobs_DispatchesUpToLimit()
    {
        // Arrange - No active jobs, more queued entries than the limit
        var entries = new List<WorkQueue>();
        for (var i = 0; i < MaxActiveJobsLimit + 3; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Queued_{i}");
            entries.Add(await CreateAndSaveWorkQueueEntry(manifest));
        }

        // Act
        await _train.Run(Unit.Default);

        // Assert - Exactly MaxActiveJobsLimit dispatched
        _dataContext.Reset();
        var dispatchedCount = 0;
        foreach (var entry in entries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            if (updated.Status == WorkQueueStatus.Dispatched)
                dispatchedCount++;
        }

        dispatchedCount
            .Should()
            .Be(
                MaxActiveJobsLimit,
                $"should dispatch exactly {MaxActiveJobsLimit} entries (the limit)"
            );
    }

    [Test]
    public async Task Run_WhenOverLimit_DoesNotDispatchAny()
    {
        // Arrange - More active jobs than the limit (shouldn't normally happen, but test it)
        for (var i = 0; i < MaxActiveJobsLimit + 3; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Active_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.Pending);
        }

        var entries = new List<WorkQueue>();
        for (var i = 0; i < 3; i++)
        {
            var queueManifest = await CreateAndSaveManifest(inputValue: $"Queued_{i}");
            entries.Add(await CreateAndSaveWorkQueueEntry(queueManifest));
        }

        // Act
        await _train.Run(Unit.Default);

        // Assert
        _dataContext.Reset();
        foreach (var entry in entries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            updated.Status.Should().Be(WorkQueueStatus.Queued, "system is already over the limit");
        }
    }

    [Test]
    public async Task Run_WithZeroActiveJobs_DispatchesNormally()
    {
        // Arrange - No active jobs, one queued entry
        var manifest = await CreateAndSaveManifest(inputValue: "Queued");
        var entry = await CreateAndSaveWorkQueueEntry(manifest);

        // Act
        await _train.Run(Unit.Default);

        // Assert
        _dataContext.Reset();
        var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
        updated.Status.Should().Be(WorkQueueStatus.Dispatched, "there was capacity to dispatch");
    }

    #endregion

    #region Priority Ordering Tests

    [Test]
    public async Task Run_WhenOnlyOneSlot_HigherPriorityDispatchedFirst()
    {
        // Arrange - Fill all but one slot so only 1 entry can be dispatched
        for (var i = 0; i < MaxActiveJobsLimit - 1; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Active_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.Pending);
        }

        // Create a low-priority entry FIRST (earlier CreatedAt)
        var lowPriorityManifest = await CreateAndSaveManifest(inputValue: "LowPriority");
        var lowPriorityEntry = await CreateAndSaveWorkQueueEntry(lowPriorityManifest, priority: 5);

        // Small delay to guarantee CreatedAt ordering
        await Task.Delay(50);

        // Create a high-priority entry SECOND (later CreatedAt but higher priority)
        var highPriorityManifest = await CreateAndSaveManifest(inputValue: "HighPriority");
        var highPriorityEntry = await CreateAndSaveWorkQueueEntry(
            highPriorityManifest,
            priority: 20
        );

        // Act
        await _train.Run(Unit.Default);

        // Assert - The high-priority entry should be dispatched (not the low-priority one)
        _dataContext.Reset();

        var updatedHighPriority = await _dataContext.WorkQueues.FirstAsync(q =>
            q.Id == highPriorityEntry.Id
        );
        updatedHighPriority
            .Status.Should()
            .Be(
                WorkQueueStatus.Dispatched,
                "higher-priority entries should be dispatched before lower-priority ones"
            );

        var updatedLowPriority = await _dataContext.WorkQueues.FirstAsync(q =>
            q.Id == lowPriorityEntry.Id
        );
        updatedLowPriority
            .Status.Should()
            .Be(
                WorkQueueStatus.Queued,
                "low-priority entry should remain queued when high-priority entry takes the only slot"
            );
    }

    [Test]
    public async Task Run_WhenSamePriority_FIFOByCreatedAt()
    {
        // Arrange - Fill all but one slot so only 1 entry can be dispatched
        for (var i = 0; i < MaxActiveJobsLimit - 1; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Active_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.Pending);
        }

        // Create first entry (earlier CreatedAt, same priority)
        var firstManifest = await CreateAndSaveManifest(inputValue: "First");
        var firstEntry = await CreateAndSaveWorkQueueEntry(firstManifest, priority: 10);

        // Small delay to guarantee CreatedAt ordering
        await Task.Delay(50);

        // Create second entry (later CreatedAt, same priority)
        var secondManifest = await CreateAndSaveManifest(inputValue: "Second");
        var secondEntry = await CreateAndSaveWorkQueueEntry(secondManifest, priority: 10);

        // Act
        await _train.Run(Unit.Default);

        // Assert - The first entry (earlier CreatedAt) should be dispatched
        _dataContext.Reset();

        var updatedFirst = await _dataContext.WorkQueues.FirstAsync(q => q.Id == firstEntry.Id);
        updatedFirst
            .Status.Should()
            .Be(
                WorkQueueStatus.Dispatched,
                "with same priority, earlier CreatedAt should be dispatched first (FIFO)"
            );

        var updatedSecond = await _dataContext.WorkQueues.FirstAsync(q => q.Id == secondEntry.Id);
        updatedSecond
            .Status.Should()
            .Be(
                WorkQueueStatus.Queued,
                "later entry should remain queued when earlier entry with same priority takes the slot"
            );
    }

    #endregion

    #region Limit Boundary Tests

    [Test]
    public async Task Run_AtExactLimitMinusOne_DispatchesOneJob()
    {
        // Arrange - Exactly limit - 1 active jobs
        for (var i = 0; i < MaxActiveJobsLimit - 1; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Active_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.Pending);
        }

        // Create multiple queued entries
        var entries = new List<WorkQueue>();
        for (var i = 0; i < 3; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Queued_{i}");
            entries.Add(await CreateAndSaveWorkQueueEntry(manifest));
        }

        // Act
        await _train.Run(Unit.Default);

        // Assert - Exactly one dispatched
        _dataContext.Reset();
        var dispatchedCount = 0;
        foreach (var entry in entries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            if (updated.Status == WorkQueueStatus.Dispatched)
                dispatchedCount++;
        }

        dispatchedCount.Should().Be(1, "only one slot was available");
    }

    [Test]
    public async Task Run_WithMixedStatesAtBoundary_CorrectlyCalculatesLimit()
    {
        // Arrange - 2 Pending + 2 InProgress + 5 Completed (doesn't count) = 4 active
        // Should allow 1 dispatch

        for (var i = 0; i < 2; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Pending_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.Pending);
        }

        for (var i = 0; i < 2; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"InProgress_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.InProgress);
        }

        for (var i = 0; i < 5; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Completed_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.Completed);
        }

        // Create queued entries
        var entries = new List<WorkQueue>();
        for (var i = 0; i < 3; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Queued_{i}");
            entries.Add(await CreateAndSaveWorkQueueEntry(manifest));
        }

        // Act
        await _train.Run(Unit.Default);

        // Assert - Only 1 dispatched (4 active, limit 5)
        _dataContext.Reset();
        var dispatchedCount = 0;
        foreach (var entry in entries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            if (updated.Status == WorkQueueStatus.Dispatched)
                dispatchedCount++;
        }

        dispatchedCount.Should().Be(1, "4 active jobs means only 1 slot available");
    }

    #endregion

    #region Multiple Train Cycles

    [Test]
    public async Task Run_MultipleConsecutiveCycles_RespectsLimitEachTime()
    {
        // Arrange - Fill active jobs to limit
        var activeManifests = new List<Manifest>();
        for (var i = 0; i < MaxActiveJobsLimit; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Active_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.Pending);
            activeManifests.Add(manifest);
        }

        // Create queued entries
        var entries = new List<WorkQueue>();
        for (var i = 0; i < 5; i++)
        {
            var manifest = await CreateAndSaveManifest(inputValue: $"Queued_{i}");
            entries.Add(await CreateAndSaveWorkQueueEntry(manifest));
        }

        // Act - Run first cycle (at limit, nothing should dispatch)
        await _train.Run(Unit.Default);

        // Assert - No entries dispatched from first cycle
        _dataContext.Reset();
        foreach (var entry in entries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            updated.Status.Should().Be(WorkQueueStatus.Queued, "limit was reached in first cycle");
        }

        // Simulate completing 2 active jobs
        foreach (var activeManifest in activeManifests.Take(2))
        {
            var metadata = await _dataContext
                .Metadatas.Where(m => m.ManifestId == activeManifest.Id)
                .FirstAsync();
            metadata.TrainState = TrainState.Completed;
        }
        await _dataContext.SaveChanges(CancellationToken.None);
        _dataContext.Reset();

        // Create a fresh scope for the second cycle (scoped services return the same instance within a scope)
        if (_train is IDisposable disposable)
            disposable.Dispose();
        if (_dataContext is IDisposable dataContextDisposable)
            dataContextDisposable.Dispose();
        _scope.Dispose();

        _scope = _serviceProvider.CreateScope();
        _train = _scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();
        _dataContext = _scope.ServiceProvider.GetRequiredService<IDataContext>();

        // Act - Run second cycle (2 slots available)
        await _train.Run(Unit.Default);

        // Assert - 2 entries dispatched in second cycle
        _dataContext.Reset();
        var dispatchedCount = 0;
        foreach (var entry in entries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            if (updated.Status == WorkQueueStatus.Dispatched)
                dispatchedCount++;
        }

        dispatchedCount.Should().Be(2, "2 slots became available after jobs completed");
    }

    #endregion

    #region Null MaxActiveJobs (Unlimited)

    [Test]
    public async Task Run_WithNullMaxActiveJobs_DispatchesAll()
    {
        // Arrange - Create a separate ServiceProvider with MaxActiveJobs = null
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
        var connectionString = configuration.GetRequiredSection("Configuration")[
            "DatabaseConnectionString"
        ]!;

        var arrayLoggingProvider = new ArrayLoggingProvider();

        await using var unlimitedProvider = new ServiceCollection()
            .AddSingleton<ILoggerProvider>(arrayLoggingProvider)
            .AddSingleton<IArrayLoggingProvider>(arrayLoggingProvider)
            .AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug))
            .AddTraxEffects(options =>
                options
                    .AddServiceTrainBus(
                        assemblies:
                        [
                            typeof(AssemblyMarker).Assembly,
                            typeof(TaskServerExecutorTrain).Assembly,
                        ]
                    )
                    .SetEffectLogLevel(LogLevel.Information)
                    .SaveTrainParameters()
                    .AddPostgresEffect(connectionString)
                    .AddEffectDataContextLogging(minimumLogLevel: LogLevel.Trace)
                    .AddJsonEffect()
                    .AddStepLogger(serializeStepData: true)
                    .AddScheduler(scheduler =>
                        scheduler.UseInMemoryTaskServer().MaxActiveJobs(null)
                    )
            )
            .AddScoped<IDataContext>(sp =>
            {
                var factory = sp.GetRequiredService<IDataContextProviderFactory>();
                return (IDataContext)factory.Create();
            })
            .BuildServiceProvider();

        using var scope = unlimitedProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();
        var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();

        // Create many active metadata (well over the default limit of 10)
        for (var i = 0; i < 15; i++)
        {
            var activeGroup = await TestSetup.CreateAndSaveManifestGroup(
                dataContext,
                name: $"group-active-{Guid.NewGuid():N}"
            );

            var activeManifest = Manifest.Create(
                new CreateManifest
                {
                    Name = typeof(SchedulerTestTrain),
                    IsEnabled = true,
                    ScheduleType = ScheduleType.None,
                    MaxRetries = 3,
                    Properties = new SchedulerTestInput { Value = $"Active_{i}" },
                }
            );
            activeManifest.ManifestGroupId = activeGroup.Id;
            await dataContext.Track(activeManifest);
            await dataContext.SaveChanges(CancellationToken.None);

            var metadata = Metadata.Create(
                new CreateMetadata
                {
                    Name = typeof(SchedulerTestTrain).FullName!,
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = new SchedulerTestInput { Value = $"Active_{i}" },
                    ManifestId = activeManifest.Id,
                }
            );
            metadata.TrainState = TrainState.Pending;
            await dataContext.Track(metadata);
            await dataContext.SaveChanges(CancellationToken.None);
        }

        // Create queued entries
        var entryCount = 8;
        var entryIds = new List<long>();
        for (var i = 0; i < entryCount; i++)
        {
            var queuedGroup = await TestSetup.CreateAndSaveManifestGroup(
                dataContext,
                name: $"group-queued-{Guid.NewGuid():N}"
            );

            var manifest = Manifest.Create(
                new CreateManifest
                {
                    Name = typeof(SchedulerTestTrain),
                    IsEnabled = true,
                    ScheduleType = ScheduleType.None,
                    MaxRetries = 3,
                    Properties = new SchedulerTestInput { Value = $"Queued_{i}" },
                }
            );
            manifest.ManifestGroupId = queuedGroup.Id;
            await dataContext.Track(manifest);
            await dataContext.SaveChanges(CancellationToken.None);

            var entry = WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = typeof(SchedulerTestTrain).FullName!,
                    Input = manifest.Properties,
                    InputTypeName = typeof(SchedulerTestInput).AssemblyQualifiedName,
                    ManifestId = manifest.Id,
                }
            );
            await dataContext.Track(entry);
            await dataContext.SaveChanges(CancellationToken.None);
            entryIds.Add(entry.Id);
        }

        dataContext.Reset();

        // Act
        await train.Run(Unit.Default);

        // Assert - All entries should be dispatched (no limit)
        dataContext.Reset();
        var dispatchedCount = 0;
        foreach (var id in entryIds)
        {
            var updated = await dataContext.WorkQueues.FirstAsync(q => q.Id == id);
            if (updated.Status == WorkQueueStatus.Dispatched)
                dispatchedCount++;
        }

        dispatchedCount
            .Should()
            .Be(entryCount, "MaxActiveJobs is null so all entries should be dispatched");

        if (train is IDisposable d)
            d.Dispose();
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

    private async Task<Metadata> CreateAndSaveMetadata(Manifest manifest, TrainState state)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = manifest.GetProperties<SchedulerTestInput>(),
                ManifestId = manifest.Id,
            }
        );

        metadata.TrainState = state;

        await _dataContext.Track(metadata);
        await _dataContext.SaveChanges(CancellationToken.None);
        _dataContext.Reset();

        return metadata;
    }

    private async Task<WorkQueue> CreateAndSaveWorkQueueEntry(Manifest manifest, int priority = 0)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName!,
                Input = manifest.Properties,
                InputTypeName = typeof(SchedulerTestInput).AssemblyQualifiedName,
                ManifestId = manifest.Id,
                Priority = priority,
            }
        );

        await _dataContext.Track(entry);
        await _dataContext.SaveChanges(CancellationToken.None);
        _dataContext.Reset();

        return entry;
    }

    #endregion
}
