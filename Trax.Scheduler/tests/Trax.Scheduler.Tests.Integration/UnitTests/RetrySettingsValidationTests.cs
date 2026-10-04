using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// The retry count refuses a negative value in both options types, and the failure count window
/// is positive wherever it is set: the builder and the operations patch.
/// </summary>
[TestFixture]
public class RetrySettingsValidationTests
{
    [Test]
    public void ScheduleOptions_MaxRetries_refuses_a_negative_count()
    {
        var act = () => new ScheduleOptions().MaxRetries(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void ScheduleOptions_MaxRetries_accepts_zero()
    {
        new ScheduleOptions().MaxRetries(0).ToManifestOptions().MaxRetries.Should().Be(0);
    }

    [Test]
    public void ManifestOptions_MaxRetries_refuses_a_negative_count()
    {
        var act = () => new ManifestOptions { MaxRetries = -1 };

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void FailureCountWindow_defaults_to_24_hours()
    {
        ResolveConfiguration(_ => { }).FailureCountWindow.Should().Be(TimeSpan.FromHours(24));
    }

    [Test]
    public void FailureCountWindow_is_set_by_the_builder()
    {
        ResolveConfiguration(b => b.FailureCountWindow(TimeSpan.FromDays(7)))
            .FailureCountWindow.Should()
            .Be(TimeSpan.FromDays(7));
    }

    [TestCase(0)]
    [TestCase(-60)]
    public void FailureCountWindow_refuses_a_window_that_is_not_positive(int seconds)
    {
        var act = () =>
            ResolveConfiguration(b => b.FailureCountWindow(TimeSpan.FromSeconds(seconds)));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void A_scheduler_config_patch_refuses_a_zero_failure_count_window()
    {
        var problem = OperationsService.ValidateSchedulerConfigPatch(
            new UpdateSchedulerConfigInput { FailureCountWindow = TimeSpan.Zero }
        );

        problem.Should().Contain(nameof(UpdateSchedulerConfigInput.FailureCountWindow));
    }

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
}
