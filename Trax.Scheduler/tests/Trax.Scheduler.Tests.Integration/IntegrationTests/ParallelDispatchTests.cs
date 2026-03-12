using System.Diagnostics;
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
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Tests.ArrayLogger.Services.ArrayLoggingProvider;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for parallel dispatch in DispatchJobsStep.
/// Uses a DelayingJobSubmitter to simulate slow remote workers and verify
/// that MaxConcurrentDispatch controls intra-cycle parallelism.
/// </summary>
[TestFixture]
public class ParallelDispatchTests
{
    private static readonly string TestTrainName = typeof(SchedulerTestTrain).FullName!;

    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;
    private IDataContext _dataContext = null!;
    private DelayingJobSubmitter _submitter = null!;
    private SchedulerConfiguration _schedulerConfiguration = null!;

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

        _submitter = new DelayingJobSubmitter(TimeSpan.FromMilliseconds(200));
        var arrayLoggingProvider = new ArrayLoggingProvider();

        _serviceProvider = new ServiceCollection()
            .AddSingleton<ILoggerProvider>(arrayLoggingProvider)
            .AddSingleton<IArrayLoggingProvider>(arrayLoggingProvider)
            .AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug))
            .AddSingleton(_submitter)
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
                            s.AddScoped<IJobSubmitter>(sp =>
                                sp.GetRequiredService<DelayingJobSubmitter>()
                            )
                        )
                    )
            )
            .AddScoped<IDataContext>(sp =>
            {
                var factory = sp.GetRequiredService<IDataContextProviderFactory>();
                return (IDataContext)factory.Create();
            })
            .BuildServiceProvider();

        _schedulerConfiguration = _serviceProvider.GetRequiredService<SchedulerConfiguration>();
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
        _submitter.Reset();
    }

    [TearDown]
    public async Task TestTearDown()
    {
        // Reset to default after each test
        _schedulerConfiguration.MaxConcurrentDispatch = 1;

        if (_dataContext is IDisposable disposable)
            disposable.Dispose();
        _scope.Dispose();
    }

    #region Sequential Default

    [Test]
    public async Task Dispatch_WithDefaultMaxConcurrentDispatch_DispatchesSequentially()
    {
        // Arrange — default MaxConcurrentDispatch = 1
        var entries = await CreateWorkQueueEntries(3);

        using var trainScope = _serviceProvider.CreateScope();
        var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();

        // Act
        var sw = Stopwatch.StartNew();
        await train.Run(Unit.Default);
        sw.Stop();

        if (train is IDisposable d)
            d.Dispose();

        // Assert — sequential: 3 entries at 200ms each = ~600ms minimum
        sw.ElapsedMilliseconds.Should()
            .BeGreaterThanOrEqualTo(500, "sequential dispatch should take at least 3 * delay");
        _submitter
            .MaxObservedConcurrency.Should()
            .Be(1, "sequential dispatch should never have more than 1 concurrent call");
    }

    #endregion

    #region Parallel Dispatch

    [Test]
    public async Task Dispatch_WithMaxConcurrentDispatch3_DispatchesConcurrently()
    {
        // Arrange
        _schedulerConfiguration.MaxConcurrentDispatch = 3;
        var entries = await CreateWorkQueueEntries(3);

        using var trainScope = _serviceProvider.CreateScope();
        var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();

        // Act
        var sw = Stopwatch.StartNew();
        await train.Run(Unit.Default);
        sw.Stop();

        if (train is IDisposable d)
            d.Dispose();

        // Assert — parallel: 3 entries with concurrency 3 should complete in ~1 delay period
        sw.ElapsedMilliseconds.Should()
            .BeLessThan(
                500,
                "parallel dispatch of 3 entries with concurrency 3 should be faster than sequential"
            );

        // Verify all entries were dispatched
        _dataContext.Reset();
        var metadataCount = await _dataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.Name == TestTrainName)
            .CountAsync();
        metadataCount.Should().Be(3);
    }

    #endregion

    #region Concurrency Limit

    [Test]
    public async Task Dispatch_WithMaxConcurrentDispatch2_RespectsLimit()
    {
        // Arrange
        _schedulerConfiguration.MaxConcurrentDispatch = 2;
        var entries = await CreateWorkQueueEntries(5);

        using var trainScope = _serviceProvider.CreateScope();
        var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();

        // Act
        await train.Run(Unit.Default);

        if (train is IDisposable d)
            d.Dispose();

        // Assert — concurrency should never exceed the configured limit
        _submitter
            .MaxObservedConcurrency.Should()
            .BeLessThanOrEqualTo(2, "dispatch concurrency should respect MaxConcurrentDispatch");

        // All entries should still be dispatched
        _dataContext.Reset();
        var metadataCount = await _dataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.Name == TestTrainName)
            .CountAsync();
        metadataCount.Should().Be(5);
    }

    #endregion

    #region Partial Failure

    [Test]
    public async Task Dispatch_Parallel_PartialFailure_OtherEntriesSucceed()
    {
        // Arrange — configure submitter to fail on specific calls
        _schedulerConfiguration.MaxConcurrentDispatch = 3;
        _submitter.FailOnCallNumbers(2); // Fail only the 2nd call

        var entries = await CreateWorkQueueEntries(3);

        using var trainScope = _serviceProvider.CreateScope();
        var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();

        // Act
        await train.Run(Unit.Default);

        if (train is IDisposable d)
            d.Dispose();

        // Assert — 3 metadata records should exist (all created before enqueue)
        _dataContext.Reset();
        var allMetadata = await _dataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.Name == TestTrainName)
            .ToListAsync();
        allMetadata.Should().HaveCount(3);

        // 2 should be Pending (successful enqueue), 1 should be Failed
        var failedCount = allMetadata.Count(m => m.TrainState == TrainState.Failed);
        var pendingCount = allMetadata.Count(m => m.TrainState == TrainState.Pending);

        failedCount.Should().Be(1, "one enqueue failure should produce one Failed metadata");
        pendingCount.Should().Be(2, "successful enqueues should leave metadata in Pending state");
    }

    #endregion

    #region All Fail

    [Test]
    public async Task Dispatch_Parallel_AllFail_AllMetadataMarkedFailed()
    {
        // Arrange
        _schedulerConfiguration.MaxConcurrentDispatch = 3;
        _submitter.FailAll();

        var entries = await CreateWorkQueueEntries(3);

        using var trainScope = _serviceProvider.CreateScope();
        var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();

        // Act — should not throw
        var act = async () => await train.Run(Unit.Default);
        await act.Should().NotThrowAsync();

        if (train is IDisposable d)
            d.Dispose();

        // Assert — all metadata should be Failed
        _dataContext.Reset();
        var allMetadata = await _dataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.Name == TestTrainName)
            .ToListAsync();
        allMetadata.Should().HaveCount(3);
        allMetadata.Should().AllSatisfy(m => m.TrainState.Should().Be(TrainState.Failed));
    }

    #endregion

    #region No Duplicate Metadata

    [Test]
    public async Task Dispatch_Parallel_NoDuplicateMetadata()
    {
        // Arrange — high concurrency to stress FOR UPDATE SKIP LOCKED
        _schedulerConfiguration.MaxConcurrentDispatch = 5;
        var entries = await CreateWorkQueueEntries(5);

        using var trainScope = _serviceProvider.CreateScope();
        var train = trainScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();

        // Act
        await train.Run(Unit.Default);

        if (train is IDisposable d)
            d.Dispose();

        // Assert — exactly one Metadata per work queue entry
        _dataContext.Reset();
        var metadataCount = await _dataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.Name == TestTrainName)
            .CountAsync();
        metadataCount.Should().Be(5, "each work queue entry should produce exactly one Metadata");

        // Verify each work queue entry has a unique MetadataId
        var workQueues = await _dataContext
            .WorkQueues.AsNoTracking()
            .Where(q => q.Status == WorkQueueStatus.Dispatched)
            .ToListAsync();

        workQueues.Should().HaveCount(5);
        workQueues
            .Select(q => q.MetadataId)
            .Distinct()
            .Should()
            .HaveCount(5, "each dispatched entry should reference a unique Metadata");
    }

    #endregion

    #region Helper Methods

    private async Task<List<WorkQueue>> CreateWorkQueueEntries(int count)
    {
        var entries = new List<WorkQueue>();

        for (var i = 0; i < count; i++)
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
                    Properties = new SchedulerTestInput { Value = $"Entry-{i}" },
                }
            );
            manifest.ManifestGroupId = group.Id;

            await _dataContext.Track(manifest);
            await _dataContext.SaveChanges(CancellationToken.None);
            _dataContext.Reset();

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

            entries.Add(entry);
        }

        return entries;
    }

    #endregion

    #region Delaying Job Submitter

    /// <summary>
    /// A job submitter that introduces a configurable delay to simulate slow remote workers.
    /// Tracks maximum observed concurrency for assertions.
    /// </summary>
    internal class DelayingJobSubmitter(TimeSpan delay) : IJobSubmitter
    {
        private int _currentConcurrency;
        private int _maxObservedConcurrency;
        private int _callCount;
        private readonly System.Collections.Generic.HashSet<int> _failOnCallNumbers = [];
        private bool _failAll;

        public int MaxObservedConcurrency => _maxObservedConcurrency;

        public void Reset()
        {
            _currentConcurrency = 0;
            _maxObservedConcurrency = 0;
            _callCount = 0;
            _failOnCallNumbers.Clear();
            _failAll = false;
        }

        public void FailOnCallNumbers(params int[] callNumbers)
        {
            foreach (var n in callNumbers)
                _failOnCallNumbers.Add(n);
        }

        public void FailAll() => _failAll = true;

        public Task<string> EnqueueAsync(long metadataId) =>
            EnqueueAsync(metadataId, CancellationToken.None);

        public Task<string> EnqueueAsync(long metadataId, object input) =>
            EnqueueAsync(metadataId, input, CancellationToken.None);

        public async Task<string> EnqueueAsync(long metadataId, CancellationToken cancellationToken)
        {
            var callNumber = Interlocked.Increment(ref _callCount);
            var current = Interlocked.Increment(ref _currentConcurrency);

            // Track max concurrency
            int observed;
            do
            {
                observed = _maxObservedConcurrency;
            } while (
                current > observed
                && Interlocked.CompareExchange(ref _maxObservedConcurrency, current, observed)
                    != observed
            );

            try
            {
                await Task.Delay(delay, cancellationToken);

                if (_failAll || _failOnCallNumbers.Contains(callNumber))
                    throw new HttpRequestException(
                        "Simulated enqueue failure: remote worker unreachable"
                    );

                return $"delayed-{Guid.NewGuid():N}";
            }
            finally
            {
                Interlocked.Decrement(ref _currentConcurrency);
            }
        }

        public Task<string> EnqueueAsync(
            long metadataId,
            object input,
            CancellationToken cancellationToken
        ) => EnqueueAsync(metadataId, cancellationToken);
    }

    #endregion
}
