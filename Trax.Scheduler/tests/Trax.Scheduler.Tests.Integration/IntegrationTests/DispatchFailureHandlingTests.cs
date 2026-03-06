using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StepProvider.Logging.Extensions;
using Trax.Effect.Tests.ArrayLogger.Services.ArrayLoggingProvider;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Tests.Integration.Examples.Trains;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for DispatchJobsStep failure handling.
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
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .SetEffectLogLevel(LogLevel.Information)
                            .SaveTrainParameters()
                            .UsePostgres(connectionString)
                            .AddDataContextLogging(minimumLogLevel: LogLevel.Trace)
                            .AddJson()
                            .AddStepLogger(serializeStepData: true)
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
    public async Task Dispatch_WhenEnqueueFails_WorkQueueStillDispatched()
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

        // Assert — the work_queue entry was committed as Dispatched before the enqueue failure
        _dataContext.Reset();
        var updatedEntry = await _dataContext
            .WorkQueues.AsNoTracking()
            .FirstAsync(q => q.Id == entry.Id);

        updatedEntry.Status.Should().Be(WorkQueueStatus.Dispatched);
        updatedEntry.MetadataId.Should().NotBeNull();
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
