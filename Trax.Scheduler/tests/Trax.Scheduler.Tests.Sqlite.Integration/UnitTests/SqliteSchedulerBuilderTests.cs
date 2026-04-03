using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Sqlite.Integration.UnitTests;

[TestFixture]
public class SqliteSchedulerBuilderTests
{
    private string _dbPath = null!;

    [SetUp]
    public void SetUp()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_builder_test_{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
        var walPath = _dbPath + "-wal";
        if (File.Exists(walPath))
            File.Delete(walPath);
        var shmPath = _dbPath + "-shm";
        if (File.Exists(shmPath))
            File.Delete(shmPath);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(x => x.SetMinimumLevel(LogLevel.Warning));
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseSqlite($"Data Source={_dbPath}"))
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(scheduler => scheduler.UseInMemoryWorkers())
        );
        return services.BuildServiceProvider();
    }

    #region Service Registration

    [Test]
    public void AddScheduler_WithSqlite_RegistersFullManifestManagerTrain()
    {
        // Arrange
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        // Act
        var train = scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();

        // Assert
        train.GetType().Name.Should().Be("ManifestManagerTrain");
    }

    [Test]
    public void AddScheduler_WithSqlite_RegistersJobDispatcherPollingService()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        // Assert
        hostedServices
            .Should()
            .Contain(
                s => s.GetType().Name == "JobDispatcherPollingService",
                "SQLite is a database provider, so the full scheduler pipeline should be registered"
            );
    }

    [Test]
    public void HasDatabaseProvider_WithSqlite_IsTrue()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act
        var config = provider.GetRequiredService<SchedulerConfiguration>();

        // Assert
        config.HasDatabaseProvider.Should().BeTrue();
    }

    #endregion
}
