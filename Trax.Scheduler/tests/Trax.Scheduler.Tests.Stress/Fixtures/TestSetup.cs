using System.Diagnostics;
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

[TestFixture]
[Category("Stress")]
[Ignore("Stress tests — run manually with: dotnet test --filter TestCategory=Stress")]
public abstract class TestSetup
{
    private ServiceProvider ServiceProvider { get; set; } = null!;

    protected IServiceScope Scope { get; private set; } = null!;

    protected IDataContext DataContext { get; private set; } = null!;

    /// <summary>
    /// Maximum time a single query should take under load. Tests fail if exceeded.
    /// </summary>
    protected static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

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
        Scope = ServiceProvider.CreateScope();
        DataContext = Scope.ServiceProvider.GetRequiredService<IDataContext>();
        await CleanupDatabase(DataContext);
    }

    [TearDown]
    public async Task TestTearDown()
    {
        if (DataContext is IDisposable disposable)
            disposable.Dispose();
        Scope.Dispose();
    }

    protected static async Task CleanupDatabase(IDataContext dataContext)
    {
        await dataContext.BackgroundJobs.ExecuteDeleteAsync();
        await dataContext.Logs.ExecuteDeleteAsync();
        await dataContext.WorkQueues.ExecuteDeleteAsync();
        await dataContext.DeadLetters.ExecuteDeleteAsync();
        await dataContext.Metadatas.ExecuteDeleteAsync();
        await dataContext
            .Manifests.Where(m => m.DependsOnManifestId != null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.DependsOnManifestId, (int?)null));
        await dataContext.Manifests.ExecuteDeleteAsync();
        await dataContext.ManifestGroups.ExecuteDeleteAsync();
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
        var sw = Stopwatch.StartNew();
        await action();
        sw.Stop();

        Assert.That(
            sw.Elapsed,
            Is.LessThan(limit),
            $"Query took {sw.Elapsed.TotalMilliseconds:F0}ms, exceeding {limit.TotalMilliseconds:F0}ms limit"
        );

        return sw.Elapsed;
    }

    #region Bulk Data Seeding

    protected async Task<ManifestGroup> SeedManifestGroup(string name = "stress-group")
    {
        var group = new ManifestGroup
        {
            Name = name,
            Priority = 0,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await DataContext.Track(group);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return group;
    }

    protected async Task<List<Manifest>> SeedManifests(int count, long groupId)
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
                    Properties = new StressTestInput { Value = $"manifest-{i}" },
                }
            );
            manifest.ExternalId = $"stress-{i}";
            manifest.ManifestGroupId = groupId;
            await DataContext.Track(manifest);
        }
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return await DataContext.Manifests.ToListAsync();
    }

    protected async Task SeedMetadata(
        List<Manifest> manifests,
        int perManifest,
        TrainState state,
        DateTime? startTime = null
    )
    {
        var baseTime = startTime ?? DateTime.UtcNow.AddHours(-1);

        for (int i = 0; i < manifests.Count; i++)
        {
            for (int j = 0; j < perManifest; j++)
            {
                var metadata = Metadata.Create(
                    new CreateMetadata
                    {
                        Name = typeof(StressTestTrain).FullName!,
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Input = new StressTestInput { Value = $"input-{i}-{j}" },
                        ManifestId = manifests[i].Id,
                    }
                );
                metadata.TrainState = state;
                metadata.StartTime = baseTime.AddMinutes(j);
                if (state is TrainState.Completed or TrainState.Failed or TrainState.Cancelled)
                    metadata.EndTime = metadata.StartTime.AddSeconds(30);
                await DataContext.Track(metadata);
            }
        }
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    protected async Task SeedWorkQueues(List<Manifest> manifests)
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
                }
            );
            await DataContext.Track(entry);
        }
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    protected async Task SeedDeadLetters(List<Manifest> manifests, int perManifest)
    {
        var loadedManifests = await DataContext.Manifests.ToListAsync();
        var manifestDict = loadedManifests.ToDictionary(m => m.Id);

        for (int i = 0; i < manifests.Count; i++)
        {
            for (int j = 0; j < perManifest; j++)
            {
                var deadLetter = DeadLetter.Create(
                    new CreateDeadLetter
                    {
                        Manifest = manifestDict[manifests[i].Id],
                        Reason = $"Test dead letter {i}-{j}",
                        RetryCount = 3,
                    }
                );
                await DataContext.Track(deadLetter);
            }
        }
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    protected async Task SeedBackgroundJobs(int count, long startMetadataId = 1)
    {
        for (int i = 0; i < count; i++)
        {
            var job = BackgroundJob.Create(
                new CreateBackgroundJob { MetadataId = startMetadataId + i }
            );
            await DataContext.Track(job);
        }
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    #endregion
}
