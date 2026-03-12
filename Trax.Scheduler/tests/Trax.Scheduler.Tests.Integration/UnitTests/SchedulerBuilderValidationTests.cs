using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Validates that the scheduler builder catches misconfiguration at build time
/// with helpful error messages, rather than failing at runtime with cryptic DI errors.
/// </summary>
[TestFixture]
public class SchedulerBuilderValidationTests
{
    private static readonly string ConnectionString =
        "Host=localhost;Port=5432;Database=trax_scheduler_tests;Username=trax;Password=trax123";

    #region AddScheduler requires a data provider

    [Test]
    public void AddScheduler_WithoutDataProvider_ThrowsWithHelpfulMessage()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects().AddMediator(typeof(AssemblyMarker).Assembly).AddScheduler()
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AddScheduler()*")
            .WithMessage("*UsePostgres*")
            .WithMessage("*UseInMemory*");
    }

    [Test]
    public void AddScheduler_WithoutDataProvider_ErrorContainsCodeExample()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects().AddMediator(typeof(AssemblyMarker).Assembly).AddScheduler()
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*services.AddTrax*")
            .WithMessage("*.AddEffects*")
            .WithMessage("*.UsePostgres(connectionString)*")
            .WithMessage("*.AddScheduler*");
    }

    [Test]
    public void AddScheduler_WithInMemory_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler()
            );

        act.Should().NotThrow();
    }

    [Test]
    public void AddScheduler_WithPostgres_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UsePostgres(ConnectionString))
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler()
            );

        act.Should().NotThrow();
    }

    #endregion

    #region Other submitters require data provider but not Postgres

    [Test]
    public void UseRemoteWorkers_WithInMemory_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler.UseRemoteWorkers(
                            o => o.BaseUrl = "http://localhost:5000",
                            routing => routing.ForTrain<ITestTrain>()
                        )
                    )
            );

        act.Should().NotThrow();
    }

    [Test]
    public void OverrideSubmitter_WithInMemory_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler.OverrideSubmitter(s =>
                            s.AddScoped<IJobSubmitter, InMemoryJobSubmitter>()
                        )
                    )
            );

        act.Should().NotThrow();
    }

    #endregion

    #region Duplicate train routing validation

    [Test]
    public void UseRemoteWorkers_DuplicateTrainAcrossSubmitters_ThrowsAtBuildTime()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler
                            .UseRemoteWorkers(
                                o => o.BaseUrl = "http://endpoint-a",
                                routing => routing.ForTrain<ITestTrain>()
                            )
                            .UseRemoteWorkers(
                                o => o.BaseUrl = "http://endpoint-b",
                                routing => routing.ForTrain<ITestTrain>()
                            )
                    )
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*routed to multiple submitters*")
            .WithMessage("*ForTrain*");
    }

    [Test]
    public void UseRemoteWorkers_DifferentTrainsAcrossSubmitters_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler
                            .UseRemoteWorkers(
                                o => o.BaseUrl = "http://endpoint-a",
                                routing => routing.ForTrain<ITestTrain>()
                            )
                            .UseRemoteWorkers(
                                o => o.BaseUrl = "http://endpoint-b",
                                routing => routing.ForTrain<ITestTrainB>()
                            )
                    )
            );

        act.Should().NotThrow();
    }

    #endregion

    #region ConfigureLocalWorkers

    [Test]
    public void ConfigureLocalWorkers_WithPostgres_RegistersCustomOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(ConnectionString))
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(scheduler =>
                    scheduler.ConfigureLocalWorkers(opts => opts.WorkerCount = 8)
                )
        );

        var options = services.BuildServiceProvider().GetService<LocalWorkerOptions>();
        options.Should().NotBeNull();
        options!.WorkerCount.Should().Be(8);
    }

    #endregion
}

internal interface ITestTrain { }

internal interface ITestTrainB { }
