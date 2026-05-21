using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Effect.Attributes;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.DormantDependentContext;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Targeted coverage for branches in <see cref="SchedulerConfigurationBuilder.Build"/> that
/// aren't reached by the headline scheduler tests: manifest-group cycle detection, the
/// [TraxRemote] attribute discovery path, the optional MetadataCleanup registration, and the
/// scoped factory for IDormantDependentContext.
/// </summary>
[TestFixture]
public class SchedulerBuilderBuildCoverageTests
{
    private static readonly string PostgresConnection =
        "Host=localhost;Port=5432;Database=trax_scheduler_builder_validation;Username=trax;Password=trax123";

    #region ValidateNoCyclicGroupDependencies

    [Test]
    public void Build_WhenManifestGroups_FormACycle_ThrowsWithCycleMembers()
    {
        // g1 -> g2 -> g1 via Schedule("A", group=g1).Include("B", group=g2).ThenInclude("C", group=g1).
        // The cycle is at the group level; manifest-level edges are A->B (g1->g2) and B->C (g2->g1).
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler =>
                    {
                        scheduler.UseInMemoryWorkers();
                        scheduler
                            .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                                "A",
                                new SchedulerTestInput(),
                                Every.Minutes(5),
                                opts => opts.Group("g1")
                            )
                            .Include<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                                "B",
                                new SchedulerTestInput(),
                                opts => opts.Group("g2")
                            )
                            .ThenInclude<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                                "C",
                                new SchedulerTestInput(),
                                opts => opts.Group("g1")
                            );
                        return scheduler;
                    })
            );

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Circular dependency detected among manifest groups*")
            .WithMessage("*g1*")
            .WithMessage("*g2*");
    }

    [Test]
    public void Build_WhenManifestEdges_StayWithinASingleGroup_DoesNotThrow()
    {
        // All three manifests live in the same group g1: edges A->B and B->C are filtered out
        // because they collapse to g1->g1 (filtered by `e.From != e.To`). No cycle to detect.
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(AssemblyMarker).Assembly)
                    .AddScheduler(scheduler =>
                    {
                        scheduler.UseInMemoryWorkers();
                        scheduler
                            .Schedule<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                                "A",
                                new SchedulerTestInput(),
                                Every.Minutes(5),
                                opts => opts.Group("g1")
                            )
                            .Include<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                                "B",
                                new SchedulerTestInput(),
                                opts => opts.Group("g1")
                            )
                            .ThenInclude<ISchedulerTestTrain, SchedulerTestInput, Unit>(
                                "C",
                                new SchedulerTestInput(),
                                opts => opts.Group("g1")
                            );
                        return scheduler;
                    })
            );

        act.Should()
            .NotThrow("intra-group manifest edges collapse to self-edges and are filtered out");
    }

    #endregion

    #region MetadataCleanup optional registration

    [Test]
    public void Build_WhenAddMetadataCleanup_IsCalled_RegistersMetadataCleanupPollingService()
    {
        // Hits the `if (_configuration.MetadataCleanup is not null)` branch in Build().
        // Needs Postgres because the conditional is nested under `HasDatabaseProvider`.
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(PostgresConnection))
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(scheduler =>
                {
                    scheduler.AddMetadataCleanup(cleanup =>
                        cleanup.RetentionPeriod = TimeSpan.FromDays(7)
                    );
                    return scheduler;
                })
        );

        // The configuration carries the MetadataCleanup record once the builder applies it.
        using var sp = services.BuildServiceProvider();
        var configuration = sp.GetRequiredService<SchedulerConfiguration>();
        configuration
            .MetadataCleanup.Should()
            .NotBeNull(
                "AddMetadataCleanup() must populate SchedulerConfiguration.MetadataCleanup; "
                    + "this drives the registration of MetadataCleanupPollingService inside Build()"
            );
    }

    #endregion

    #region [TraxRemote] attribute discovery

    [Test]
    public void Build_WhenAssemblyContains_TraxRemoteTrain_RegistersItForAttributeRouting()
    {
        // RegisterRoutedSubmitters() scans the DI graph for [TraxRemote]-attributed trains
        // and adds them to the routing configuration so the dispatcher routes them through
        // the first registered remote submitter. Exercising this needs an actual routed
        // submitter PLUS a train carrying the attribute.
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(typeof(SchedulerBuilderBuildCoverageTests).Assembly)
                .AddScheduler(scheduler =>
                    scheduler.UseRemoteWorkers(
                        o => o.BaseUrl = "http://endpoint",
                        // Need at least one ForTrain so a RoutedSubmitterRegistration is created
                        // (without one, RegisterRoutedSubmitters early-returns and the
                        // attribute-discovery loop never runs).
                        routing => routing.ForTrain<INonRemoteCoverageTrain>()
                    )
                )
        );

        using var sp = services.BuildServiceProvider();
        var routing = sp.GetRequiredService<JobSubmitterRoutingConfiguration>();

        // The [TraxRemote]-marked train must have been discovered and registered as an
        // attribute-routed train. GetSubmitterType returns the submitter for it via the
        // attribute fallback once the default has been set.
        var attributeSubmitter = routing.GetSubmitterType(typeof(IRemoteCoverageTrain).FullName!);
        attributeSubmitter
            .Should()
            .NotBeNull(
                "RegisterRoutedSubmitters must discover [TraxRemote] trains and route them "
                    + "through the first registered remote submitter"
            );
    }

    #endregion

    #region IDormantDependentContext scoped factory

    [Test]
    public void Build_RegistersIDormantDependentContext_AsForwardingScoped()
    {
        // The Build() body registers a scoped DormantDependentContext concrete and forwards
        // IDormantDependentContext to the same instance via a factory lambda. The lambda body
        // is only exercised when something resolves IDormantDependentContext from a scope.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler()
        );

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var concrete = scope.ServiceProvider.GetRequiredService<DormantDependentContext>();
        var via_interface = scope.ServiceProvider.GetRequiredService<IDormantDependentContext>();

        via_interface
            .Should()
            .BeSameAs(
                concrete,
                "the IDormantDependentContext factory must forward to the same scoped instance "
                    + "as the concrete DormantDependentContext, so user steps and the scheduler "
                    + "see the same context within a request scope"
            );
    }

    #endregion
}

#region Fakes

/// <summary>
/// Train carrying [TraxRemote] so the scheduler builder's attribute-discovery loop has a hit.
/// </summary>
[TraxRemote]
internal class RemoteCoverageTrain : ServiceTrain<RemoteCoverageInput, Unit>, IRemoteCoverageTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(RemoteCoverageInput input) =>
        Activate(input, Unit.Default).Resolve();
}

internal record RemoteCoverageInput : IManifestProperties { }

internal interface IRemoteCoverageTrain : IServiceTrain<RemoteCoverageInput, Unit> { }

/// <summary>
/// Train NOT carrying [TraxRemote], used only to provide a ForTrain target so a
/// RoutedSubmitterRegistration is created and the attribute-discovery loop runs.
/// </summary>
internal class NonRemoteCoverageTrain
    : ServiceTrain<NonRemoteCoverageInput, Unit>,
        INonRemoteCoverageTrain
{
    protected override async Task<Either<Exception, Unit>> RunInternal(
        NonRemoteCoverageInput input
    ) => Activate(input, Unit.Default).Resolve();
}

internal record NonRemoteCoverageInput : IManifestProperties { }

internal interface INonRemoteCoverageTrain : IServiceTrain<NonRemoteCoverageInput, Unit> { }

#endregion
