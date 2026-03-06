using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class WorkerExtensionsTests
{
    private ServiceProvider BuildWorkerProvider(Action<LocalWorkerOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(_ => { }).AddMediator(typeof(AssemblyMarker).Assembly)
        );
        services.AddTraxWorker(configure);
        return services.BuildServiceProvider();
    }

    #region AddTraxWorker - Registers JobRunner Pipeline

    [Test]
    public void AddTraxWorker_RegistersSchedulerConfiguration()
    {
        using var provider = BuildWorkerProvider();

        var config = provider.GetService<SchedulerConfiguration>();
        config.Should().NotBeNull();
    }

    [Test]
    public void AddTraxWorker_RegistersCancellationRegistry()
    {
        using var provider = BuildWorkerProvider();

        var registry = provider.GetService<ICancellationRegistry>();
        registry.Should().NotBeNull();
    }

    [Test]
    public void AddTraxWorker_RegistersJobRunnerTrain()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(_ => { }).AddMediator(typeof(AssemblyMarker).Assembly)
        );
        services.AddTraxWorker();

        services
            .Should()
            .Contain(d =>
                d.ServiceType == typeof(IJobRunnerTrain) && d.Lifetime == ServiceLifetime.Scoped
            );
    }

    #endregion

    #region AddTraxWorker - Worker-Specific Registration

    [Test]
    public void AddTraxWorker_RegistersLocalWorkerOptions()
    {
        using var provider = BuildWorkerProvider();

        var options = provider.GetService<LocalWorkerOptions>();
        options.Should().NotBeNull();
    }

    [Test]
    public void AddTraxWorker_DefaultWorkerCount_IsProcessorCount()
    {
        using var provider = BuildWorkerProvider();

        var options = provider.GetRequiredService<LocalWorkerOptions>();
        options.WorkerCount.Should().Be(Environment.ProcessorCount);
    }

    [Test]
    public void AddTraxWorker_CustomWorkerCount_IsApplied()
    {
        using var provider = BuildWorkerProvider(opts => opts.WorkerCount = 4);

        var options = provider.GetRequiredService<LocalWorkerOptions>();
        options.WorkerCount.Should().Be(4);
    }

    [Test]
    public void AddTraxWorker_CustomPollingInterval_IsApplied()
    {
        using var provider = BuildWorkerProvider(opts =>
            opts.PollingInterval = TimeSpan.FromSeconds(5)
        );

        var options = provider.GetRequiredService<LocalWorkerOptions>();
        options.PollingInterval.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Test]
    public void AddTraxWorker_CustomVisibilityTimeout_IsApplied()
    {
        using var provider = BuildWorkerProvider(opts =>
            opts.VisibilityTimeout = TimeSpan.FromMinutes(15)
        );

        var options = provider.GetRequiredService<LocalWorkerOptions>();
        options.VisibilityTimeout.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Test]
    public void AddTraxWorker_CustomShutdownTimeout_IsApplied()
    {
        using var provider = BuildWorkerProvider(opts =>
            opts.ShutdownTimeout = TimeSpan.FromMinutes(2)
        );

        var options = provider.GetRequiredService<LocalWorkerOptions>();
        options.ShutdownTimeout.Should().Be(TimeSpan.FromMinutes(2));
    }

    #endregion

    #region AddTraxWorker - Registers LocalWorkerService as Hosted Service

    [Test]
    public void AddTraxWorker_RegistersHostedService()
    {
        using var provider = BuildWorkerProvider();

        var hostedServices = provider.GetServices<IHostedService>();
        hostedServices
            .Should()
            .Contain(s =>
                s.GetType() == typeof(Scheduler.Services.LocalWorkerService.LocalWorkerService)
            );
    }

    #endregion

    #region AddTraxWorker - No Configure Callback

    [Test]
    public void AddTraxWorker_NullConfigure_UsesDefaults()
    {
        using var provider = BuildWorkerProvider(null);

        var options = provider.GetRequiredService<LocalWorkerOptions>();
        options.WorkerCount.Should().Be(Environment.ProcessorCount);
        options.PollingInterval.Should().Be(TimeSpan.FromSeconds(1));
        options.VisibilityTimeout.Should().Be(TimeSpan.FromMinutes(30));
        options.ShutdownTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    #endregion
}
