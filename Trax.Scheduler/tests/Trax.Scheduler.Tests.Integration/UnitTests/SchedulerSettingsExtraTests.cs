using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Coverage for the SchedulerConfigurationBuilder Settings methods that the existing
/// SchedulerConfigurationBuilderSettingsTests doesn't reach: MaxConcurrentDispatch,
/// MaxDispatchAttempts, MaxWorkQueueEntriesPerCycle, StalePendingTimeout,
/// StaleInProgressTimeout, DefaultMisfirePolicy.
/// </summary>
[TestFixture]
public class SchedulerSettingsExtraTests
{
    private static SchedulerConfiguration ResolveConfiguration(
        Action<SchedulerConfigurationBuilder> configure
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(scheduler =>
                {
                    scheduler.UseInMemoryWorkers();
                    configure(scheduler);
                    return scheduler;
                })
        );
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<SchedulerConfiguration>();
    }

    [Test]
    public void MaxConcurrentDispatch_PositiveValue_StoresValue()
    {
        var config = ResolveConfiguration(b => b.MaxConcurrentDispatch(7));
        config.MaxConcurrentDispatch.Should().Be(7);
    }

    [Test]
    public void MaxConcurrentDispatch_ZeroOrNegative_FloorsAtOne()
    {
        var config = ResolveConfiguration(b => b.MaxConcurrentDispatch(0));
        config.MaxConcurrentDispatch.Should().Be(1);
    }

    [Test]
    public void MaxDispatchAttempts_PositiveValue_StoresValue()
    {
        var config = ResolveConfiguration(b => b.MaxDispatchAttempts(9));
        config.MaxDispatchAttempts.Should().Be(9);
    }

    [Test]
    public void MaxDispatchAttempts_Negative_FloorsAtZero()
    {
        var config = ResolveConfiguration(b => b.MaxDispatchAttempts(-3));
        config.MaxDispatchAttempts.Should().Be(0);
    }

    [Test]
    public void MaxWorkQueueEntriesPerCycle_PositiveValue_StoresValue()
    {
        var config = ResolveConfiguration(b => b.MaxWorkQueueEntriesPerCycle(150));
        config.MaxWorkQueueEntriesPerCycle.Should().Be(150);
    }

    [Test]
    public void MaxWorkQueueEntriesPerCycle_Null_DisablesLimit()
    {
        var config = ResolveConfiguration(b => b.MaxWorkQueueEntriesPerCycle(null));
        config.MaxWorkQueueEntriesPerCycle.Should().BeNull();
    }

    [Test]
    public void MaxWorkQueueEntriesPerCycle_Zero_FloorsAtOne()
    {
        var config = ResolveConfiguration(b => b.MaxWorkQueueEntriesPerCycle(0));
        config.MaxWorkQueueEntriesPerCycle.Should().Be(1);
    }

    [Test]
    public void StalePendingTimeout_AppliesValue()
    {
        var config = ResolveConfiguration(b => b.StalePendingTimeout(TimeSpan.FromMinutes(45)));
        config.StalePendingTimeout.Should().Be(TimeSpan.FromMinutes(45));
    }

    [Test]
    public void StaleInProgressTimeout_AppliesValue()
    {
        var config = ResolveConfiguration(b => b.StaleInProgressTimeout(TimeSpan.FromMinutes(120)));
        config.StaleInProgressTimeout.Should().Be(TimeSpan.FromMinutes(120));
    }

    [Test]
    public void DefaultMisfirePolicy_AppliesValue()
    {
        var config = ResolveConfiguration(b => b.DefaultMisfirePolicy(MisfirePolicy.DoNothing));
        config.DefaultMisfirePolicy.Should().Be(MisfirePolicy.DoNothing);
    }

    [Test]
    public void UseRemoteRun_RegistersRemoteRunOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(typeof(AssemblyMarker).Assembly)
                .AddScheduler(scheduler =>
                {
                    scheduler
                        .UseInMemoryWorkers()
                        .UseRemoteRun(opts => opts.BaseUrl = "https://run.example.com/");
                    return scheduler;
                })
        );
        using var provider = services.BuildServiceProvider();

        var opts = provider.GetService<RemoteRunOptions>();
        opts.Should().NotBeNull();
        opts!.BaseUrl.Should().Be("https://run.example.com/");
    }

    [Test]
    public void PruneOrphanedManifests_FlagSetsConfiguration()
    {
        var enabledConfig = ResolveConfiguration(b => b.PruneOrphanedManifests());
        var disabledConfig = ResolveConfiguration(b => b.PruneOrphanedManifests(false));

        enabledConfig.PruneOrphanedManifests.Should().BeTrue();
        disabledConfig.PruneOrphanedManifests.Should().BeFalse();
    }
}
