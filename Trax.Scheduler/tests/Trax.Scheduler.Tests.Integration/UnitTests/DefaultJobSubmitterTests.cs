using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobDispatcherPollingService;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.ManifestManagerPollingService;
using Trax.Scheduler.Services.MetadataCleanupPollingService;
using Trax.Scheduler.Services.SchedulerStartupService;
using Trax.Scheduler.Trains.JobRunner;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Proves that the scheduler selects the correct IJobSubmitter implementation
/// based on the configured data provider (sane defaults).
/// </summary>
[TestFixture]
public class DefaultJobSubmitterTests
{
    private static readonly string ConnectionString =
        "Host=localhost;Port=5432;Database=trax_scheduler_tests;Username=trax;Password=trax123";

    /// <summary>
    /// Inspects the service collection to find which implementation type was registered
    /// for the given service type. This avoids needing to resolve dependencies that
    /// require real infrastructure (e.g., PostgresJobSubmitter needs a live DB connection).
    /// </summary>
    private static Type? GetRegisteredImplementationType<TService>(IServiceCollection services) =>
        services.LastOrDefault(d => d.ServiceType == typeof(TService))?.ImplementationType;

    #region InMemory Default (no database provider)

    [Test]
    public void AddScheduler_WithInMemory_RegistersInMemoryJobSubmitter()
    {
        // Arrange & Act — UseInMemory() provides a data provider but not a database provider
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler()
            )
            .BuildServiceProvider();

        // Assert — resolve the actual instance (InMemory has no infrastructure deps)
        using var scope = provider.CreateScope();
        var submitter = scope.ServiceProvider.GetService<IJobSubmitter>();
        submitter.Should().NotBeNull();
        submitter.Should().BeOfType<InMemoryJobSubmitter>();
    }

    [Test]
    public void AddScheduler_WithInMemory_RegistersJobRunnerTrain()
    {
        // InMemoryJobSubmitter depends on IJobRunnerTrain — verify it's registered automatically
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler()
            )
            .BuildServiceProvider();

        // Assert
        using var scope = provider.CreateScope();
        var runner = scope.ServiceProvider.GetService<IJobRunnerTrain>();
        runner.Should().NotBeNull();
    }

    #endregion

    #region Postgres Default (database provider configured)

    [Test]
    public void AddScheduler_WithPostgres_RegistersPostgresJobSubmitter()
    {
        // Arrange — UsePostgres() sets HasDatabaseProvider = true
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(ConnectionString))
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler()
        );

        // Assert — check the registration (can't resolve without a live DB)
        GetRegisteredImplementationType<IJobSubmitter>(services)
            .Should()
            .Be(typeof(PostgresJobSubmitter));
    }

    [Test]
    public void AddScheduler_WithPostgresAndSchedulerConfig_RegistersPostgresJobSubmitter()
    {
        // Even with scheduler configuration, Postgres default should apply
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(ConnectionString))
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(scheduler =>
                    scheduler.PollingInterval(TimeSpan.FromSeconds(10)).MaxActiveJobs(5)
                )
        );

        // Assert
        GetRegisteredImplementationType<IJobSubmitter>(services)
            .Should()
            .Be(typeof(PostgresJobSubmitter));
    }

    #endregion

    #region Explicit override takes priority

    [Test]
    public void AddScheduler_WithOverrideSubmitter_IgnoresPostgresDefault()
    {
        // Even with Postgres configured, OverrideSubmitter should win
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(ConnectionString))
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(scheduler =>
                    scheduler.OverrideSubmitter(s =>
                        s.AddScoped<IJobSubmitter, InMemoryJobSubmitter>()
                    )
                )
        );

        // Assert — OverrideSubmitter's registration should be the last one
        GetRegisteredImplementationType<IJobSubmitter>(services)
            .Should()
            .Be(typeof(InMemoryJobSubmitter));
    }

    [Test]
    public void AddScheduler_WithPostgresDefault_RegistersPostgresJobSubmitter()
    {
        // Local workers are the default when Postgres is configured — no explicit call needed
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(ConnectionString))
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler()
        );

        // Assert
        GetRegisteredImplementationType<IJobSubmitter>(services)
            .Should()
            .Be(typeof(PostgresJobSubmitter));
    }

    #endregion

    #region Polling service registration (conditional on HasDatabaseProvider)

    private static bool HasHostedService<T>(IServiceCollection services) =>
        services.Any(d =>
            d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(T)
        );

    [Test]
    public void AddScheduler_WithInMemory_RegistersManifestManagerButNotDispatcher()
    {
        // InMemory registers ManifestManagerPollingService (drives scheduling) but not
        // JobDispatcherPollingService (uses FOR UPDATE SKIP LOCKED) or MetadataCleanup
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler()
        );

        HasHostedService<ManifestManagerPollingService>(services).Should().BeTrue();
        HasHostedService<JobDispatcherPollingService>(services).Should().BeFalse();
        HasHostedService<MetadataCleanupPollingService>(services).Should().BeFalse();
    }

    [Test]
    public void AddScheduler_WithInMemory_RegistersStartupService()
    {
        // SchedulerStartupService should still be registered (seeds manifests)
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler()
        );

        HasHostedService<SchedulerStartupService>(services).Should().BeTrue();
    }

    [Test]
    public void AddScheduler_WithPostgres_RegistersPollingServices()
    {
        // Postgres supports FOR UPDATE SKIP LOCKED — polling services should be registered
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(ConnectionString))
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler()
        );

        HasHostedService<ManifestManagerPollingService>(services).Should().BeTrue();
        HasHostedService<JobDispatcherPollingService>(services).Should().BeTrue();
        HasHostedService<SchedulerStartupService>(services).Should().BeTrue();
    }

    [Test]
    public void AddScheduler_WithPostgresAndMetadataCleanup_RegistersCleanupPollingService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(ConnectionString))
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(scheduler => scheduler.AddMetadataCleanup())
        );

        HasHostedService<MetadataCleanupPollingService>(services).Should().BeTrue();
    }

    [Test]
    public void AddScheduler_WithInMemoryAndMetadataCleanup_DoesNotRegisterCleanupPollingService()
    {
        // MetadataCleanup uses ExecuteDeleteAsync — not supported by InMemory
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(scheduler => scheduler.AddMetadataCleanup())
        );

        HasHostedService<MetadataCleanupPollingService>(services).Should().BeFalse();
    }

    #endregion

    #region ManifestManager train registration (conditional on HasDatabaseProvider)

    [Test]
    public void AddScheduler_WithInMemory_RegistersInMemoryManifestManagerTrain()
    {
        // InMemory uses a simplified train that skips Postgres-specific steps
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler()
            )
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        var train = scope.ServiceProvider.GetService<IManifestManagerTrain>();
        train.Should().NotBeNull();
        train.Should().BeOfType<InMemoryManifestManagerTrain>();
    }

    [Test]
    public void AddScheduler_WithPostgres_RegistersManifestManagerTrain()
    {
        // AddScopedTraxRoute registers the concrete type directly — check that registration
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(ConnectionString))
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler()
        );

        services
            .Any(d => d.ServiceType == typeof(ManifestManagerTrain))
            .Should()
            .BeTrue("Postgres should register ManifestManagerTrain");
        services
            .Any(d => d.ServiceType == typeof(InMemoryManifestManagerTrain))
            .Should()
            .BeFalse("Postgres should not register InMemoryManifestManagerTrain");
    }

    #endregion
}
