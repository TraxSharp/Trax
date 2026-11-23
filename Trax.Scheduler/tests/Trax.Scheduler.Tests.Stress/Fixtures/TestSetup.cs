using System.Diagnostics;
using System.Globalization;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.BackgroundJob.DTOs;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Tests.ArrayLogger.Services.ArrayLoggingProvider;
using Trax.Scheduler.Tests.Stress.Fakes.Trains;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Stress.Fixtures;

/// <summary>
/// Base for the stress fixtures.
/// </summary>
/// <remarks>
/// Every concrete fixture carries <c>[Explicit]</c>, so the suite never runs in a normal
/// <c>dotnet test</c> and runs when selected:
/// <code>dotnet test --filter TestCategory=Stress</code>
/// The attribute goes on the concrete fixture, not here: NUnit does not inherit
/// <c>[Explicit]</c> or <c>[Ignore]</c> from a base class, so one placed on this class skips
/// nothing.
/// </remarks>
[TestFixture]
[Category("Stress")]
public abstract class TestSetup
{
    private ServiceProvider ServiceProvider
    {
        get => field.NotNull(nameof(ServiceProvider));
        set;
    }

    protected AsyncServiceScope Scope { get; private set; }

    protected IDataContext DataContext
    {
        get => field.NotNull(nameof(DataContext));
        private set;
    }

    /// <summary>
    /// Maximum time a single query should take under load. Tests fail if exceeded.
    /// </summary>
    protected static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    [OneTimeSetUp]
    public async Task RunBeforeAnyTests()
    {
        var connectionString = TestPostgres.ConnectionString;

        var arrayLoggingProvider = new ArrayLoggingProvider();

        ServiceProvider = new ServiceCollection()
            .AddSingleton<ILoggerProvider>(arrayLoggingProvider)
            .AddSingleton<IArrayLoggingProvider>(arrayLoggingProvider)
            .AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Warning))
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .SetEffectLogLevel(LogLevel.Warning)
                            .SaveTrainParameters()
                            .UsePostgres(connectionString)
                            .AddJson()
                            .AddJunctionLogger()
                    )
                    .AddMediator(typeof(StressTestTrain).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler => scheduler.UseInMemoryWorkers())
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
        await ServiceProvider.DisposeAsync();
    }

    [SetUp]
    public virtual async Task TestSetUp()
    {
        Scope = ServiceProvider.CreateAsyncScope();
        DataContext = Scope.ServiceProvider.GetRequiredService<IDataContext>();
        await CleanupDatabase(DataContext);
    }

    [TearDown]
    public async Task TestTearDown()
    {
        if (DataContext is IDisposable disposable)
            disposable.Dispose();
        await Scope.DisposeAsync().ConfigureAwait(false);
    }

    protected static async Task CleanupDatabase(IDataContext dataContext)
    {
        await dataContext.BackgroundJobs.ExecuteDeleteAsync().ConfigureAwait(false);
        await dataContext.Logs.ExecuteDeleteAsync().ConfigureAwait(false);
        await dataContext.WorkQueues.ExecuteDeleteAsync().ConfigureAwait(false);
        await dataContext.DeadLetters.ExecuteDeleteAsync().ConfigureAwait(false);
        await dataContext.Metadatas.ExecuteDeleteAsync().ConfigureAwait(false);
        await dataContext
            .Manifests.Where(m => m.DependsOnManifestId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.DependsOnManifestId, (int?)null))
            .ConfigureAwait(false);
        await dataContext.Manifests.ExecuteDeleteAsync().ConfigureAwait(false);
        await dataContext.ManifestGroups.ExecuteDeleteAsync().ConfigureAwait(false);
        dataContext.Reset();
    }

    /// <summary>
    /// Times an async action and asserts it completes within the query timeout.
    /// Returns the elapsed time for logging.
    /// </summary>
    protected static async Task<TimeSpan> AssertCompletesWithin(
        Func<Task> action,
        TimeSpan? timeout = null
    )
    {
        var limit = timeout ?? QueryTimeout;
        var swStarted = TimeProvider.System.GetTimestamp();
        await action().ConfigureAwait(false);
        var swElapsed = TimeProvider.System.GetElapsedTime(swStarted);

        swElapsed
            .Should()
            .BeLessThan(
                limit,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"query took {swElapsed.TotalMilliseconds:F0}ms, exceeding {limit.TotalMilliseconds:F0}ms limit"
                )
            );

        return swElapsed;
    }

    #region Bulk Data Seeding

    protected async Task<ManifestGroup> SeedManifestGroup(string name = "stress-group")
    {
        var group = new ManifestGroup
        {
            Name = name,
            Priority = 0,
            IsEnabled = true,
            CreatedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
            UpdatedAt = TimeProvider.System.GetUtcNow().UtcDateTime,
        };
        await DataContext.Track(group).ConfigureAwait(false);
        await DataContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        DataContext.Reset();
        return group;
    }

    protected async Task<IList<Manifest>> SeedManifests(int count, long groupId)
    {
        var manifests = new List<Manifest>(count);
        for (int i = 0; i < count; i++)
        {
            var manifest = Manifest.Create(
                new CreateManifest
                {
                    Name = typeof(StressTestTrain),
                    IsEnabled = true,
                    ScheduleType = ScheduleType.Interval,
                    IntervalSeconds = 60,
                    MaxRetries = 3,
                    Properties = new StressTestInput
                    {
                        Value = string.Create(CultureInfo.InvariantCulture, $"manifest-{i}"),
                    },
                }
            );
            manifest.ExternalId = string.Create(CultureInfo.InvariantCulture, $"stress-{i}");
            manifest.ManifestGroupId = groupId;
            await DataContext.Track(manifest).ConfigureAwait(false);
        }
        await DataContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        DataContext.Reset();

        return await DataContext.Manifests.ToListAsync().ConfigureAwait(false);
    }

    protected async Task SeedMetadata(
        IList<Manifest> manifests,
        int perManifest,
        TrainState state,
        DateTime? startTime = null
    )
    {
        var baseTime = startTime ?? TimeProvider.System.GetUtcNow().UtcDateTime.AddHours(-1);

        for (int i = 0; i < manifests.Count; i++)
        {
            for (int j = 0; j < perManifest; j++)
            {
                var metadata = Metadata.Create(
                    new CreateMetadata
                    {
                        Name = typeof(StressTestTrain).FullName.NotNull(),
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Input = new StressTestInput
                        {
                            Value = string.Create(CultureInfo.InvariantCulture, $"input-{i}-{j}"),
                        },
                        ManifestId = manifests[i].Id,
                    },
                    TimeProvider.System
                );
                metadata.TrainState = state;
                metadata.StartTime = baseTime.AddMinutes(j);
                if (state is TrainState.Completed or TrainState.Failed or TrainState.Cancelled)
                    metadata.EndTime = metadata.StartTime.AddSeconds(30);
                await DataContext.Track(metadata).ConfigureAwait(false);
            }
        }
        await DataContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        DataContext.Reset();
    }

    protected async Task SeedWorkQueues(IList<Manifest> manifests)
    {
        // Only one queued entry per manifest (unique constraint ix_work_queue_unique_queued_manifest)
        for (int i = 0; i < manifests.Count; i++)
        {
            var entry = WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = manifests[i].Name,
                    Input = manifests[i].Properties,
                    InputTypeName = manifests[i].PropertyTypeName,
                    ManifestId = manifests[i].Id,
                },
                TimeProvider.System
            );
            await DataContext.Track(entry).ConfigureAwait(false);
        }
        await DataContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        DataContext.Reset();
    }

    protected async Task SeedDeadLetters(IList<Manifest> manifests, int perManifest)
    {
        var loadedManifests = await DataContext.Manifests.ToListAsync().ConfigureAwait(false);
        var manifestDict = loadedManifests.ToDictionary(m => m.Id);

        for (int i = 0; i < manifests.Count; i++)
        {
            for (int j = 0; j < perManifest; j++)
            {
                var deadLetter = DeadLetter.Create(
                    new CreateDeadLetter
                    {
                        Manifest = manifestDict[manifests[i].Id],
                        Reason = string.Create(
                            CultureInfo.InvariantCulture,
                            $"Test dead letter {i}-{j}"
                        ),
                        RetryCount = 3,
                    },
                    TimeProvider.System
                );
                await DataContext.Track(deadLetter).ConfigureAwait(false);
            }
        }
        await DataContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        DataContext.Reset();
    }

    protected async Task SeedBackgroundJobs(int count, long startMetadataId = 1)
    {
        for (int i = 0; i < count; i++)
        {
            var job = BackgroundJob.Create(
                new CreateBackgroundJob { MetadataId = startMetadataId + i },
                TimeProvider.System
            );
            await DataContext.Track(job).ConfigureAwait(false);
        }
        await DataContext.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        DataContext.Reset();
    }

    #endregion
}
