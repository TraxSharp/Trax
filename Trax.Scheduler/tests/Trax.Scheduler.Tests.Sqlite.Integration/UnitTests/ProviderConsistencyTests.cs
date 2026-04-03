using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Sqlite.Integration.UnitTests;

[TestFixture]
public class ProviderConsistencyTests
{
    private ServiceProvider _sqliteProvider = null!;
    private string _dbPath = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_consistency_{Guid.NewGuid():N}.db");

        _sqliteProvider = new ServiceCollection()
            .AddLogging(x => x.SetMinimumLevel(LogLevel.Warning))
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseSqlite($"Data Source={_dbPath}"))
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler()
            )
            .BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        await _sqliteProvider.DisposeAsync();

        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        var walPath = _dbPath + "-wal";
        if (File.Exists(walPath))
            File.Delete(walPath);
        var shmPath = _dbPath + "-shm";
        if (File.Exists(shmPath))
            File.Delete(shmPath);
    }

    [Test]
    public void HasDatabaseProvider_Sqlite_True()
    {
        var config = _sqliteProvider.GetRequiredService<SchedulerConfiguration>();
        config.HasDatabaseProvider.Should().BeTrue();
    }

    [Test]
    public void FullManifestManagerTrain_RegisteredFor_Sqlite()
    {
        using var scope = _sqliteProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
        train.GetType().Name.Should().Be("ManifestManagerTrain");
    }

    [Test]
    public void ISqlDialect_RegisteredFor_Sqlite()
    {
        var dialect = _sqliteProvider.GetService<ISqlDialect>();
        dialect.Should().NotBeNull();
    }
}
