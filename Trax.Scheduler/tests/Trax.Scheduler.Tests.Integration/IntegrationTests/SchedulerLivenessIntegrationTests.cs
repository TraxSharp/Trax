using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobDispatcherPollingService;
using Trax.Scheduler.Services.SchedulerLiveness;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Proves the JobDispatcher polling service actually stamps the liveness monitor end to end,
/// so the health check reflects real dispatch activity rather than just process liveness.
/// </summary>
[TestFixture]
public class SchedulerLivenessIntegrationTests : TestSetup
{
    [Test]
    public async Task JobDispatcherPollingService_StampsLivenessMonitor_OnCycleCompletion()
    {
        var monitor = Scope.ServiceProvider.GetRequiredService<ISchedulerLivenessMonitor>();
        monitor
            .LastDispatchCompletedAt.Should()
            .BeNull("no dispatch cycle has run yet in this fixture");

        var config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
        var logger = Scope.ServiceProvider.GetRequiredService<
            ILogger<JobDispatcherPollingService>
        >();
        var service = new JobDispatcherPollingService(
            Scope.ServiceProvider,
            config,
            monitor,
            logger
        );

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        try
        {
            // The service runs one cycle immediately on start; an empty work queue is a valid
            // no-op cycle that still stamps liveness.
            var stamped = await WaitUntilAsync(
                () => monitor.LastDispatchCompletedAt is not null,
                TimeSpan.FromSeconds(15)
            );

            stamped
                .Should()
                .BeTrue(
                    "the dispatcher polling service should stamp liveness after completing a cycle"
                );
        }
        finally
        {
            await cts.CancelAsync();
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return true;
            // allowed-delay: poll interval while awaiting the completion signal, bounded by the
            // timeout above. Not a fixed wait for the work to finish.
            await Task.Delay(25);
        }
        return predicate();
    }
}
