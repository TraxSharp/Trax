using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class SchedulerConfigurationBuilderSettingsTests
{
    private SchedulerConfiguration ResolveConfiguration(
        Action<SchedulerConfigurationBuilder> configure
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTraxEffects(options =>
            options
                .AddServiceTrainBus(assemblies: [typeof(AssemblyMarker).Assembly])
                .AddScheduler(scheduler =>
                {
                    scheduler.UseInMemoryTaskServer();
                    configure(scheduler);
                })
        );
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<SchedulerConfiguration>();
    }

    #region Polling Intervals

    [Test]
    public void PollingInterval_SetsBothIntervals()
    {
        // Act
        var config = ResolveConfiguration(b => b.PollingInterval(TimeSpan.FromSeconds(10)));

        // Assert
        config.ManifestManagerPollingInterval.Should().Be(TimeSpan.FromSeconds(10));
        config.JobDispatcherPollingInterval.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Test]
    public void ManifestManagerPollingInterval_SetsOnlyManifestManager()
    {
        // Act
        var config = ResolveConfiguration(b =>
            b.ManifestManagerPollingInterval(TimeSpan.FromSeconds(15))
        );

        // Assert
        config.ManifestManagerPollingInterval.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Test]
    public void JobDispatcherPollingInterval_SetsOnlyJobDispatcher()
    {
        // Act
        var config = ResolveConfiguration(b =>
            b.JobDispatcherPollingInterval(TimeSpan.FromSeconds(3))
        );

        // Assert
        config.JobDispatcherPollingInterval.Should().Be(TimeSpan.FromSeconds(3));
    }

    #endregion

    #region Job Limits

    [Test]
    public void MaxActiveJobs_SetsValue()
    {
        // Act
        var config = ResolveConfiguration(b => b.MaxActiveJobs(50));

        // Assert
        config.MaxActiveJobs.Should().Be(50);
    }

    [Test]
    public void MaxActiveJobs_Null_DisablesLimit()
    {
        // Act
        var config = ResolveConfiguration(b => b.MaxActiveJobs(null));

        // Assert
        config.MaxActiveJobs.Should().BeNull();
    }

    [Test]
    public void DependentPriorityBoost_SetsValue()
    {
        // Act
        var config = ResolveConfiguration(b => b.DependentPriorityBoost(8));

        // Assert
        config.DependentPriorityBoost.Should().Be(8);
    }

    #endregion

    #region Retry Settings

    [Test]
    public void DefaultMaxRetries_SetsValue()
    {
        // Act
        var config = ResolveConfiguration(b => b.DefaultMaxRetries(5));

        // Assert
        config.DefaultMaxRetries.Should().Be(5);
    }

    [Test]
    public void DefaultRetryDelay_SetsValue()
    {
        // Act
        var config = ResolveConfiguration(b => b.DefaultRetryDelay(TimeSpan.FromMinutes(10)));

        // Assert
        config.DefaultRetryDelay.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Test]
    public void RetryBackoffMultiplier_SetsValue()
    {
        // Act
        var config = ResolveConfiguration(b => b.RetryBackoffMultiplier(3.0));

        // Assert
        config.RetryBackoffMultiplier.Should().Be(3.0);
    }

    [Test]
    public void MaxRetryDelay_SetsValue()
    {
        // Act
        var config = ResolveConfiguration(b => b.MaxRetryDelay(TimeSpan.FromHours(2)));

        // Assert
        config.MaxRetryDelay.Should().Be(TimeSpan.FromHours(2));
    }

    #endregion

    #region Timeout

    [Test]
    public void DefaultJobTimeout_SetsValue()
    {
        // Act
        var config = ResolveConfiguration(b => b.DefaultJobTimeout(TimeSpan.FromMinutes(30)));

        // Assert
        config.DefaultJobTimeout.Should().Be(TimeSpan.FromMinutes(30));
    }

    #endregion

    #region Recovery

    [Test]
    public void RecoverStuckJobsOnStartup_False_SetsValue()
    {
        // Act
        var config = ResolveConfiguration(b => b.RecoverStuckJobsOnStartup(false));

        // Assert
        config.RecoverStuckJobsOnStartup.Should().BeFalse();
    }

    #endregion
}
