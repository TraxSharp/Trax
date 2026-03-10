using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Trains.JobRunner;

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
    public void AddScheduler_WithoutDatabaseProvider_RegistersInMemoryJobSubmitter()
    {
        // Arrange & Act — AddEffects() with no data provider configured
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddTrax(trax =>
                trax.AddEffects().AddMediator(typeof(AssemblyMarker).Assembly).AddScheduler()
            )
            .BuildServiceProvider();

        // Assert — resolve the actual instance (InMemory has no infrastructure deps)
        using var scope = provider.CreateScope();
        var submitter = scope.ServiceProvider.GetService<IJobSubmitter>();
        submitter.Should().NotBeNull();
        submitter.Should().BeOfType<InMemoryJobSubmitter>();
    }

    [Test]
    public void AddScheduler_WithoutDatabaseProvider_RegistersJobRunnerTrain()
    {
        // InMemoryJobSubmitter depends on IJobRunnerTrain — verify it's registered automatically
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddTrax(trax =>
                trax.AddEffects().AddMediator(typeof(AssemblyMarker).Assembly).AddScheduler()
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
    public void AddScheduler_WithUseLocalWorkers_RegistersPostgresJobSubmitter()
    {
        // UseLocalWorkers explicitly sets _taskServerRegistration — should register
        // PostgresJobSubmitter via the UseLocalWorkers path
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(ConnectionString))
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(scheduler => scheduler.UseLocalWorkers())
        );

        // Assert
        GetRegisteredImplementationType<IJobSubmitter>(services)
            .Should()
            .Be(typeof(PostgresJobSubmitter));
    }

    #endregion
}
