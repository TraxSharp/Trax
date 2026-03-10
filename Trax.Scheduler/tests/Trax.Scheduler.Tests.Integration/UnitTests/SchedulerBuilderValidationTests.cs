using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
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

    #region UseLocalWorkers requires UsePostgres

    [Test]
    public void UseLocalWorkers_WithoutAnyDataProvider_ThrowsWithHelpfulMessage()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects()
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler => scheduler.UseLocalWorkers())
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*UseLocalWorkers()*")
            .WithMessage("*UsePostgres()*");
    }

    [Test]
    public void UseLocalWorkers_WithInMemory_ThrowsWithHelpfulMessage()
    {
        // UseInMemory does NOT set HasDatabaseProvider — only UsePostgres does.
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler => scheduler.UseLocalWorkers())
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*UseLocalWorkers()*")
            .WithMessage("*UsePostgres()*");
    }

    [Test]
    public void UseLocalWorkers_WithPostgres_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UsePostgres(ConnectionString))
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler => scheduler.UseLocalWorkers())
            );

        act.Should().NotThrow();
    }

    [Test]
    public void UseLocalWorkers_ErrorMessage_ContainsCodeExample()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects()
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler => scheduler.UseLocalWorkers())
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*services.AddTrax*")
            .WithMessage("*.AddEffects*")
            .WithMessage("*.UsePostgres(connectionString)*")
            .WithMessage("*.AddScheduler*");
    }

    #endregion

    #region Other submitters do not require Postgres

    [Test]
    public void UseRemoteWorkers_WithoutPostgres_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects()
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler.UseRemoteWorkers(o => o.BaseUrl = "http://localhost:5000")
                    )
            );

        act.Should().NotThrow();
    }

    [Test]
    public void OverrideSubmitter_WithoutPostgres_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects()
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler.OverrideSubmitter(s =>
                            s.AddScoped<IJobSubmitter, InMemoryJobSubmitter>()
                        )
                    )
            );

        act.Should().NotThrow();
    }

    [Test]
    public void ParameterlessAddScheduler_WithoutPostgres_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects().AddMediator(typeof(AssemblyMarker).Assembly).AddScheduler()
            );

        act.Should().NotThrow();
    }

    #endregion
}
