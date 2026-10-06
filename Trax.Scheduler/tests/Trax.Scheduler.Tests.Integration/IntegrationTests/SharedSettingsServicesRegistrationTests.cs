using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trax.Scheduler.Services.DeadLetterRequeue;
using Trax.Scheduler.Services.Effects;
using Trax.Scheduler.Services.LogLevels;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// <c>AddScheduler</c> registers the effects and log-level services and the requeue-all jobs the
/// dashboard and the GraphQL API share, since both surfaces require the scheduler, and registers
/// the log-level overrides on the logger filter options exactly once.
///
/// <para>Enforces <c>Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[TestFixture]
public class SharedSettingsServicesRegistrationTests : TestSetup
{
    [Test]
    public void AddScheduler_registers_the_effects_and_log_level_services()
    {
        var effects = Scope.ServiceProvider.GetRequiredService<IEffectSettingsService>();
        var levels = Scope.ServiceProvider.GetRequiredService<ILogLevelService>();

        effects.IsAvailable.Should().BeTrue("AddEffects registers the effect registry");
        effects.GetEffects().Should().NotBeEmpty();
        levels.Should().NotBeNull();
        Scope
            .ServiceProvider.GetServices<IPostConfigureOptions<LoggerFilterOptions>>()
            .OfType<LogLevelOverrides>()
            .Should()
            .ContainSingle();
    }

    [Test]
    public void AddScheduler_registers_one_requeue_all_job_list_for_the_node()
    {
        using var other = Scope.ServiceProvider.CreateScope();

        var jobs = Scope.ServiceProvider.GetRequiredService<IDeadLetterRequeueJobs>();

        jobs.Should().BeOfType<DeadLetterRequeueJobs>();
        other
            .ServiceProvider.GetRequiredService<IDeadLetterRequeueJobs>()
            .Should()
            .BeSameAs(jobs, "a job started from one request is read from another");
    }
}
