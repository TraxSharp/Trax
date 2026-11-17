using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.MetadataCleanupPollingService;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Tests.Sqlite.Integration.IntegrationTests;

/// <summary>
/// The scheduler's own runs, the dispatcher's above all, are purged on a host that never asked
/// for metadata cleanup: they are written every poll, and nothing else would remove them.
/// </summary>
[TestFixture]
public class MetadataCleanupDefaultTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private string? _dbPath;

    [SetUp]
    public void SetUp() =>
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_cleanup_default_{Guid.NewGuid():N}.db");

    [TearDown]
    public void TearDown()
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(_dbPath.NotNull() + suffix))
                File.Delete(_dbPath.NotNull() + suffix);
    }

    [Test]
    public async Task Admin_runs_are_purged_without_opt_in()
    {
        var services = new ServiceCollection();
        services.AddLogging(x => x.SetMinimumLevel(LogLevel.Warning));
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseSqlite($"Data Source={_dbPath.NotNull()}"))
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(scheduler => scheduler)
        );
        await using var provider = services.BuildServiceProvider();

        var old = DateTime.UtcNow.AddHours(-2);
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IJobDispatcherTrain).FullName.NotNull(),
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        run.TrainState = TrainState.Completed;
        run.StartTime = old;
        run.EndTime = old;
        await using (var context = Context(provider))
        {
            await context.Track(run);
            await context.SaveChanges(CancellationToken.None);
        }

        var cleanup = provider
            .GetServices<IHostedService>()
            .OfType<MetadataCleanupPollingService>()
            .SingleOrDefault();
        cleanup.Should().NotBeNull("a scheduler on a database cleans up its own runs by default");

        await cleanup.NotNull().StartAsync(CancellationToken.None);
        try
        {
            await Gone(provider, run.Id).WaitAsync(Bound);
        }
        finally
        {
            await cleanup.StopAsync(CancellationToken.None);
        }
    }

    private static IDataContext Context(IServiceProvider provider) =>
        (IDataContext)provider.GetRequiredService<IDataContextProviderFactory>().Create();

    private static async Task Gone(IServiceProvider provider, long id)
    {
        while (true)
        {
            var context = Context(provider);
            await using (context.ConfigureAwait(false))
                if (
                    !await context
                        .Metadatas.AsNoTracking()
                        .AnyAsync(m => m.Id == id)
                        .ConfigureAwait(false)
                )
                    return;
            await Task.Delay(100).ConfigureAwait(false); // determinism: polling interval, bounded by the caller's WaitAsync
        }
    }
}
