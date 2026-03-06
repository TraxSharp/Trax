using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.DormantDependentContext;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class JobRunnerExtensionsTests
{
    private ServiceCollection BuildJobRunnerServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(_ => { }).AddMediator(typeof(AssemblyMarker).Assembly)
        );
        services.AddTraxJobRunner();
        return services;
    }

    #region AddTraxJobRunner - Service Registration Tests

    [Test]
    public void AddTraxJobRunner_RegistersSchedulerConfiguration()
    {
        var services = BuildJobRunnerServices();
        using var provider = services.BuildServiceProvider();

        var config = provider.GetService<SchedulerConfiguration>();
        config.Should().NotBeNull();
    }

    [Test]
    public void AddTraxJobRunner_RegistersCancellationRegistry()
    {
        var services = BuildJobRunnerServices();
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetService<ICancellationRegistry>();
        registry.Should().NotBeNull();
        registry.Should().BeOfType<CancellationRegistry>();
    }

    [Test]
    public void AddTraxJobRunner_CancellationRegistryIsSingleton()
    {
        var services = BuildJobRunnerServices();
        using var provider = services.BuildServiceProvider();

        var registry1 = provider.GetService<ICancellationRegistry>();
        var registry2 = provider.GetService<ICancellationRegistry>();
        registry1.Should().BeSameAs(registry2);
    }

    [Test]
    public void AddTraxJobRunner_RegistersTraxScheduler()
    {
        var services = BuildJobRunnerServices();

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(ITraxScheduler)
                && d.ImplementationType == typeof(TraxScheduler)
                && d.Lifetime == ServiceLifetime.Scoped
            );
    }

    [Test]
    public void AddTraxJobRunner_RegistersDormantDependentContext()
    {
        var services = BuildJobRunnerServices();

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IDormantDependentContext)
                && d.Lifetime == ServiceLifetime.Scoped
            );
    }

    [Test]
    public void AddTraxJobRunner_RegistersDormantDependentContextConcrete()
    {
        var services = BuildJobRunnerServices();

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(DormantDependentContext)
                && d.Lifetime == ServiceLifetime.Scoped
            );
    }

    [Test]
    public void AddTraxJobRunner_RegistersJobRunnerTrain()
    {
        var services = BuildJobRunnerServices();

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IJobRunnerTrain) && d.Lifetime == ServiceLifetime.Scoped
            );
    }

    #endregion

    #region AddTraxJobRunner - Does NOT Register Scheduler Infrastructure

    [Test]
    public void AddTraxJobRunner_DoesNotRegisterLocalWorkerOptions()
    {
        var services = BuildJobRunnerServices();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetService<LocalWorkerOptions>();
        options.Should().BeNull();
    }

    [Test]
    public void AddTraxJobRunner_SchedulerConfigurationIsEmpty()
    {
        var services = BuildJobRunnerServices();
        using var provider = services.BuildServiceProvider();

        var config = provider.GetRequiredService<SchedulerConfiguration>();
        config.ManifestManagerEnabled.Should().BeTrue();
        config.MaxActiveJobs.Should().NotBeNull();
    }

    #endregion

    #region AddTraxJobRunner - Scoping Tests

    [Test]
    public void AddTraxJobRunner_TraxSchedulerIsScoped()
    {
        var services = BuildJobRunnerServices();

        var descriptor = services.First(d => d.ServiceType == typeof(ITraxScheduler));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Test]
    public void AddTraxJobRunner_JobRunnerTrainIsScoped()
    {
        var services = BuildJobRunnerServices();

        var descriptor = services.First(d => d.ServiceType == typeof(IJobRunnerTrain));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    #endregion
}
