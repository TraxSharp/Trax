using System.Text.Json;
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
using Trax.Effect.Utils;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Tests.Integration.Examples.Trains;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for per-group MaxActiveJobs enforcement in the JobDispatcher.
/// These tests verify the starvation fix: when a high-priority group hits its per-group cap,
/// lower-priority groups can still dispatch (continue instead of break).
/// </summary>
[TestFixture]
public class ManifestGroupMaxActiveJobsTests
{
    private const int GlobalMaxActiveJobs = 10;

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
                            typeof(JobRunnerTrain).Assembly,
                        ]
                    )
                    .SetEffectLogLevel(LogLevel.Information)
                    .SaveTrainParameters()
                    .AddPostgresEffect(connectionString)
                    .AddEffectDataContextLogging(minimumLogLevel: LogLevel.Trace)
                    .AddJsonEffect()
                    .AddStepLogger(serializeStepData: true)
                    .AddScheduler(scheduler =>
                        scheduler.UseInMemoryWorkers().MaxActiveJobs(GlobalMaxActiveJobs)
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

    #region Per-Group MaxActiveJobs Tests

    [Test]
    public async Task Run_GroupAtMaxActiveJobs_SkipsGroupEntries()
    {
        // Arrange - Group A has MaxActiveJobs=2, already has 2 active jobs
        var groupA = await CreateAndSaveManifestGroup("group-a", maxActiveJobs: 2, priority: 10);

        // Create 2 active jobs for group A
        for (var i = 0; i < 2; i++)
        {
            var activeManifest = await CreateAndSaveManifest(groupA, inputValue: $"Active_A_{i}");
            await CreateAndSaveMetadata(activeManifest, TrainState.Pending);
        }

        // Create a queued entry for group A - should NOT be dispatched
        var queuedManifest = await CreateAndSaveManifest(groupA, inputValue: "Queued_A");
        var queuedEntry = await CreateAndSaveWorkQueueEntry(queuedManifest);

        // Act
        await _train.Run(Unit.Default);

        // Assert - Entry should remain queued since group is at capacity
        _dataContext.Reset();
        var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == queuedEntry.Id);
        updated
            .Status.Should()
            .Be(WorkQueueStatus.Queued, "group A is at its MaxActiveJobs limit of 2");
    }

    [Test]
    public async Task Run_GroupAtMaxActiveJobs_OtherGroupsStillDispatch()
    {
        // Arrange - Group A at capacity, Group B has room
        // This is THE starvation fix test
        var groupA = await CreateAndSaveManifestGroup("group-a", maxActiveJobs: 2, priority: 24);
        var groupB = await CreateAndSaveManifestGroup("group-b", maxActiveJobs: null, priority: 0);

        // Fill group A to capacity
        for (var i = 0; i < 2; i++)
        {
            var activeManifest = await CreateAndSaveManifest(groupA, inputValue: $"Active_A_{i}");
            await CreateAndSaveMetadata(activeManifest, TrainState.Pending);
        }

        // Queue entries for both groups
        var groupAManifest = await CreateAndSaveManifest(groupA, inputValue: "Queued_A");
        var groupAEntry = await CreateAndSaveWorkQueueEntry(groupAManifest);

        var groupBManifest = await CreateAndSaveManifest(groupB, inputValue: "Queued_B");
        var groupBEntry = await CreateAndSaveWorkQueueEntry(groupBManifest);

        // Act
        await _train.Run(Unit.Default);

        // Assert - Group A entry stays queued, Group B entry gets dispatched
        _dataContext.Reset();

        var updatedA = await _dataContext.WorkQueues.FirstAsync(q => q.Id == groupAEntry.Id);
        updatedA.Status.Should().Be(WorkQueueStatus.Queued, "group A is at its per-group limit");

        var updatedB = await _dataContext.WorkQueues.FirstAsync(q => q.Id == groupBEntry.Id);
        updatedB
            .Status.Should()
            .Be(WorkQueueStatus.Dispatched, "group B has no per-group limit and should dispatch");
    }

    [Test]
    public async Task Run_GroupMaxActiveJobsNull_NoPerGroupLimit()
    {
        // Arrange - Group with no per-group limit
        var group = await CreateAndSaveManifestGroup("unlimited-group", maxActiveJobs: null);

        // Create many active jobs for this group
        for (var i = 0; i < 5; i++)
        {
            var manifest = await CreateAndSaveManifest(group, inputValue: $"Active_{i}");
            await CreateAndSaveMetadata(manifest, TrainState.Pending);
        }

        // Queue more entries - should be dispatched (up to global limit)
        var queuedManifest = await CreateAndSaveManifest(group, inputValue: "Queued");
        var queuedEntry = await CreateAndSaveWorkQueueEntry(queuedManifest);

        // Act
        await _train.Run(Unit.Default);

        // Assert - Should be dispatched since no per-group limit
        _dataContext.Reset();
        var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == queuedEntry.Id);
        updated
            .Status.Should()
            .Be(WorkQueueStatus.Dispatched, "null MaxActiveJobs means no per-group limit");
    }

    [Test]
    public async Task Run_GlobalAndGroupLimits_BothEnforced()
    {
        // Arrange - Global limit = 10, Group A limit = 3
        // Group A has 0 active, queues 5 entries
        // Only 3 should dispatch (per-group limit)
        var groupA = await CreateAndSaveManifestGroup("group-a", maxActiveJobs: 3, priority: 10);

        var entries = new List<WorkQueue>();
        for (var i = 0; i < 5; i++)
        {
            var manifest = await CreateAndSaveManifest(groupA, inputValue: $"Queued_{i}");
            entries.Add(await CreateAndSaveWorkQueueEntry(manifest));
        }

        // Act
        await _train.Run(Unit.Default);

        // Assert - Exactly 3 dispatched (per-group limit)
        _dataContext.Reset();
        var dispatchedCount = 0;
        foreach (var entry in entries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            if (updated.Status == WorkQueueStatus.Dispatched)
                dispatchedCount++;
        }

        dispatchedCount.Should().Be(3, "per-group MaxActiveJobs of 3 should limit dispatch");
    }

    [Test]
    public async Task Run_MultipleGroupsWithLimits_FairDispatch()
    {
        // Arrange - Group A (limit=2, priority=24), Group B (limit=3, priority=0)
        // Both have enough queued entries. Both should get dispatched up to their limits.
        var groupA = await CreateAndSaveManifestGroup("group-a", maxActiveJobs: 2, priority: 24);
        var groupB = await CreateAndSaveManifestGroup("group-b", maxActiveJobs: 3, priority: 0);

        var groupAEntries = new List<WorkQueue>();
        for (var i = 0; i < 4; i++)
        {
            var manifest = await CreateAndSaveManifest(groupA, inputValue: $"A_{i}");
            groupAEntries.Add(await CreateAndSaveWorkQueueEntry(manifest));
        }

        var groupBEntries = new List<WorkQueue>();
        for (var i = 0; i < 5; i++)
        {
            var manifest = await CreateAndSaveManifest(groupB, inputValue: $"B_{i}");
            groupBEntries.Add(await CreateAndSaveWorkQueueEntry(manifest));
        }

        // Act
        await _train.Run(Unit.Default);

        // Assert - Group A: 2 dispatched, Group B: 3 dispatched
        _dataContext.Reset();

        var groupADispatched = 0;
        foreach (var entry in groupAEntries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            if (updated.Status == WorkQueueStatus.Dispatched)
                groupADispatched++;
        }
        groupADispatched.Should().Be(2, "group A has MaxActiveJobs=2");

        var groupBDispatched = 0;
        foreach (var entry in groupBEntries)
        {
            var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
            if (updated.Status == WorkQueueStatus.Dispatched)
                groupBDispatched++;
        }
        groupBDispatched.Should().Be(3, "group B has MaxActiveJobs=3");
    }

    [Test]
    public async Task Run_GroupAtCapacity_HigherPriorityEntriesStillSkipped()
    {
        // Arrange - Group at capacity, even high-priority entries should be skipped
        var group = await CreateAndSaveManifestGroup("full-group", maxActiveJobs: 2, priority: 31);

        // Fill to capacity
        for (var i = 0; i < 2; i++)
        {
            var activeManifest = await CreateAndSaveManifest(group, inputValue: $"Active_{i}");
            await CreateAndSaveMetadata(activeManifest, TrainState.Pending);
        }

        // Queue a high-priority entry for the full group
        var manifest = await CreateAndSaveManifest(group, inputValue: "HighPriority");
        var entry = await CreateAndSaveWorkQueueEntry(manifest, priority: 31);

        // Act
        await _train.Run(Unit.Default);

        // Assert - Should remain queued despite high priority
        _dataContext.Reset();
        var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
        updated
            .Status.Should()
            .Be(
                WorkQueueStatus.Queued,
                "per-group limit prevents dispatch even for high-priority entries"
            );
    }

    #endregion

    #region Group Priority Ordering Tests

    [Test]
    public async Task Run_DispatchesHigherGroupPriorityFirst()
    {
        // Arrange - Two groups with different priorities, global limit restricts to 1
        // Set up with global limit tight enough that ordering matters
        var lowGroup = await CreateAndSaveManifestGroup("low-group", priority: 0);
        var highGroup = await CreateAndSaveManifestGroup("high-group", priority: 24);

        // Create 9 active jobs to leave only 1 slot (global limit = 10)
        var fillerGroup = await CreateAndSaveManifestGroup("filler-group");
        for (var i = 0; i < GlobalMaxActiveJobs - 1; i++)
        {
            var filler = await CreateAndSaveManifest(fillerGroup, inputValue: $"Filler_{i}");
            await CreateAndSaveMetadata(filler, TrainState.Pending);
        }

        // Queue one entry for each group - low priority first (earlier CreatedAt)
        var lowManifest = await CreateAndSaveManifest(lowGroup, inputValue: "Low");
        var lowEntry = await CreateAndSaveWorkQueueEntry(lowManifest);

        await Task.Delay(50);

        var highManifest = await CreateAndSaveManifest(highGroup, inputValue: "High");
        var highEntry = await CreateAndSaveWorkQueueEntry(highManifest);

        // Act
        await _train.Run(Unit.Default);

        // Assert - High priority group's entry should be dispatched
        _dataContext.Reset();

        var updatedHigh = await _dataContext.WorkQueues.FirstAsync(q => q.Id == highEntry.Id);
        updatedHigh
            .Status.Should()
            .Be(WorkQueueStatus.Dispatched, "higher group priority should be dispatched first");

        var updatedLow = await _dataContext.WorkQueues.FirstAsync(q => q.Id == lowEntry.Id);
        updatedLow
            .Status.Should()
            .Be(
                WorkQueueStatus.Queued,
                "lower group priority should remain queued when only 1 slot available"
            );
    }

    #endregion

    #region Disabled Group Tests

    [Test]
    public async Task Run_DisabledGroup_EntriesNotDispatched()
    {
        // Arrange - Disabled group with queued entries
        var disabledGroup = await CreateAndSaveManifestGroup("disabled-group", isEnabled: false);

        var manifest = await CreateAndSaveManifest(disabledGroup, inputValue: "Disabled");
        var entry = await CreateAndSaveWorkQueueEntry(manifest);

        // Act
        await _train.Run(Unit.Default);

        // Assert - Entry should remain queued (LoadQueuedJobsStep filters disabled groups)
        _dataContext.Reset();
        var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == entry.Id);
        updated
            .Status.Should()
            .Be(WorkQueueStatus.Queued, "entries from disabled groups should not be dispatched");
    }

    [Test]
    public async Task Run_DisabledGroup_DoesNotBlockOtherGroups()
    {
        // Arrange - Disabled group and enabled group
        var disabledGroup = await CreateAndSaveManifestGroup(
            "disabled-group",
            isEnabled: false,
            priority: 31
        );
        var enabledGroup = await CreateAndSaveManifestGroup(
            "enabled-group",
            isEnabled: true,
            priority: 0
        );

        var disabledManifest = await CreateAndSaveManifest(disabledGroup, inputValue: "Disabled");
        var disabledEntry = await CreateAndSaveWorkQueueEntry(disabledManifest);

        var enabledManifest = await CreateAndSaveManifest(enabledGroup, inputValue: "Enabled");
        var enabledEntry = await CreateAndSaveWorkQueueEntry(enabledManifest);

        // Act
        await _train.Run(Unit.Default);

        // Assert - Disabled group's entry stays queued, enabled group's entry dispatches
        _dataContext.Reset();

        var updatedDisabled = await _dataContext.WorkQueues.FirstAsync(q =>
            q.Id == disabledEntry.Id
        );
        updatedDisabled.Status.Should().Be(WorkQueueStatus.Queued);

        var updatedEnabled = await _dataContext.WorkQueues.FirstAsync(q => q.Id == enabledEntry.Id);
        updatedEnabled.Status.Should().Be(WorkQueueStatus.Dispatched);
    }

    #endregion

    #region Manual Queue (No Manifest) Tests

    [Test]
    public async Task Run_ManualEntry_RespectsGlobalMaxActiveJobs()
    {
        // Arrange - Fill global capacity, then queue a manual entry
        var fillerGroup = await CreateAndSaveManifestGroup("filler-group");
        for (var i = 0; i < GlobalMaxActiveJobs; i++)
        {
            var filler = await CreateAndSaveManifest(fillerGroup, inputValue: $"Filler_{i}");
            await CreateAndSaveMetadata(filler, TrainState.Pending);
        }

        var manualEntry = await CreateAndSaveManualWorkQueueEntry();

        // Act
        await _train.Run(Unit.Default);

        // Assert - Manual entry should remain queued (global limit reached)
        _dataContext.Reset();
        var updated = await _dataContext.WorkQueues.FirstAsync(q => q.Id == manualEntry.Id);
        updated
            .Status.Should()
            .Be(
                WorkQueueStatus.Queued,
                "manual entries should still respect global MaxActiveJobs limit"
            );
    }

    [Test]
    public async Task Run_ManualEntry_BypassesPerGroupLimits()
    {
        // Arrange - A group at per-group capacity + a manual entry with no manifest
        var group = await CreateAndSaveManifestGroup("full-group", maxActiveJobs: 1, priority: 31);
        var activeManifest = await CreateAndSaveManifest(group, inputValue: "Active");
        await CreateAndSaveMetadata(activeManifest, TrainState.Pending);

        // Group entry should be skipped (at capacity)
        var groupManifest = await CreateAndSaveManifest(group, inputValue: "Queued_Group");
        var groupEntry = await CreateAndSaveWorkQueueEntry(groupManifest);

        // Manual entry should dispatch (no group association)
        var manualEntry = await CreateAndSaveManualWorkQueueEntry();

        // Act
        await _train.Run(Unit.Default);

        // Assert
        _dataContext.Reset();

        var updatedGroup = await _dataContext.WorkQueues.FirstAsync(q => q.Id == groupEntry.Id);
        updatedGroup
            .Status.Should()
            .Be(WorkQueueStatus.Queued, "group entry should be blocked by per-group limit");

        var updatedManual = await _dataContext.WorkQueues.FirstAsync(q => q.Id == manualEntry.Id);
        updatedManual
            .Status.Should()
            .Be(
                WorkQueueStatus.Dispatched,
                "manual entry has no manifest and bypasses per-group limits"
            );
    }

    #endregion

    #region Helper Methods

    private async Task<ManifestGroup> CreateAndSaveManifestGroup(
        string name,
        int? maxActiveJobs = null,
        int priority = 0,
        bool isEnabled = true
    )
    {
        return await TestSetup.CreateAndSaveManifestGroup(
            _dataContext,
            name: name,
            maxActiveJobs: maxActiveJobs,
            priority: priority,
            isEnabled: isEnabled
        );
    }

    private async Task<Manifest> CreateAndSaveManifest(
        ManifestGroup group,
        string inputValue = "TestValue"
    )
    {
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

    private async Task<WorkQueue> CreateAndSaveManualWorkQueueEntry(
        string inputValue = "ManualTestValue",
        int priority = 0
    )
    {
        var serializedInput = JsonSerializer.Serialize(
            new SchedulerTestInput { Value = inputValue },
            TraxJsonSerializationOptions.ManifestProperties
        );

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(SchedulerTestTrain).FullName!,
                Input = serializedInput,
                InputTypeName = typeof(SchedulerTestInput).AssemblyQualifiedName,
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
