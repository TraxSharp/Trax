using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectProvider;
using Trax.Mediator.Services.Principal;
using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Recovery.Faults;
using Trax.Samples.Recovery.Records;
using Trax.Samples.Recovery.Trains.Refund;
using Trax.Samples.Recovery.Trains.StartRun;
using Trax.Samples.Recovery.Trains.StartRun.Junctions;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// A start arms the run's crash before it schedules the manifest, since the dispatcher may start the first run before
/// the start sees it queued, and a start that fails leaves nothing armed.
/// </summary>
[TestFixture]
public class StartRunTests : RecoveryTestFixture
{
    [Test]
    public async Task A_start_whose_first_run_is_not_seen_leaves_nothing_armed()
    {
        var services = SharedRecoverySetup.Factory.Services;
        var dataContexts = services.GetRequiredService<IDataContextProviderFactory>();
        var faults = services.GetRequiredService<FaultInjector>();
        var before = await LastManifestIdAsync(dataContexts);

        using var scope = services.CreateScope();
        var start = new ScheduleDemoRun(
            scope.ServiceProvider.GetRequiredService<ITraxScheduler>(),
            services.GetRequiredService<CaseFiles>(),
            faults,
            new Operator(),
            // The manifest is scheduled for real; only the wait for its first run gives up.
            new NeverSeen()
        );

        var starting = () =>
            start.Run(
                new StartRunInput
                {
                    Scenario = Scenario.Refund,
                    OrderId = "A-1001",
                    CrashOnce = true,
                }
            );

        await starting.Should().ThrowAsync<TimeoutException>();
        await using var context = await dataContexts.CreateDbContextAsync(CancellationToken.None);
        var scheduled = await context
            .Manifests.AsNoTracking()
            .Where(m => m.Id > before && m.ExternalId.StartsWith("recovery-"))
            .Select(m => m.ExternalId)
            .SingleAsync();
        faults
            .IsArmed(scheduled["recovery-".Length..])
            .Should()
            .BeFalse("a start that did not see its first run disarms the crash it armed");
    }

    [Test]
    public async Task The_crash_is_armed_when_the_manifest_is_scheduled()
    {
        var services = SharedRecoverySetup.Factory.Services;
        var faults = services.GetRequiredService<FaultInjector>();
        var scheduler = DispatchProxy.Create<ITraxScheduler, SchedulingSeen>();
        var seen = (SchedulingSeen)(object)scheduler;
        seen.Faults = faults;
        var start = new ScheduleDemoRun(
            scheduler,
            services.GetRequiredService<CaseFiles>(),
            faults,
            new Operator(),
            new NeverSeen()
        );

        var starting = () =>
            start.Run(
                new StartRunInput
                {
                    Scenario = Scenario.Refund,
                    OrderId = "A-1001",
                    CrashOnce = true,
                }
            );

        await starting.Should().ThrowAsync<SchedulingSeen.Stopped>();
        seen.RunId.Should().NotBeNull("the start schedules the manifest");
        seen.ArmedWhenScheduled.Should()
            .BeTrue(
                "the dispatcher may start the first run as soon as the manifest is scheduled, so the crash is armed by then"
            );
        faults.IsArmed(seen.RunId!).Should().BeFalse("the start that failed disarms its crash");
    }

    [Test]
    public async Task A_crash_armed_at_the_start_fires_when_no_step_waits()
    {
        var pace = SharedRecoverySetup.Factory.Services.GetRequiredService<DemoPace>();
        var stepDelay = pace.StepDelay;
        pace.StepDelay = TimeSpan.Zero;
        try
        {
            await Run.StartAsync("REFUND", crashOnce: true, orderId: "A-1001");

            var first = await Run.FollowAttemptAsync(1);
            (await Run.WaitForEndAsync(first))
                .Should()
                .Be("FAILED", "the crash is armed before the dispatcher can reach it");
            var second = await Run.FollowAttemptAsync(2);
            (await Run.WaitForEndAsync(second)).Should().Be("COMPLETED");
        }
        finally
        {
            pace.StepDelay = stepDelay;
        }
    }

    private static async Task<long> LastManifestIdAsync(IDataContextProviderFactory dataContexts)
    {
        await using var context = await dataContexts.CreateDbContextAsync(CancellationToken.None);
        return await context.Manifests.AsNoTracking().MaxAsync(m => (long?)m.Id) ?? 0;
    }

    private sealed class Operator : ICurrentPrincipalProvider
    {
        public string? GetCurrentPrincipalId() => "TraxApiKey:operator";
    }

    // Stands in for the scheduler: when the manifest is scheduled it notes whether the run's crash is armed at that
    // moment, then fails the start, so nothing is scheduled for real.
    public class SchedulingSeen : DispatchProxy
    {
        public FaultInjector Faults { get; set; } = null!;
        public string? RunId { get; private set; }
        public bool ArmedWhenScheduled { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (
                targetMethod?.Name != nameof(ITraxScheduler.ScheduleOnceAsync)
                || args?.OfType<RefundInput>().FirstOrDefault() is not { } input
            )
                throw new NotSupportedException(targetMethod?.Name);
            RunId = input.RunId;
            ArmedWhenScheduled = Faults.IsArmed(input.RunId);
            return Task.FromException<Trax.Effect.Models.Manifest.Manifest>(new Stopped());
        }

        public sealed class Stopped : Exception;
    }

    // Stands in for the scheduler never queueing the first run within the start's wait.
    private sealed class NeverSeen : IDataContextProviderFactory
    {
        public Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken) =>
            throw new TimeoutException("The scheduler did not queue the run.");

        public IEffectProvider Create() =>
            throw new TimeoutException("The scheduler did not queue the run.");
    }
}
