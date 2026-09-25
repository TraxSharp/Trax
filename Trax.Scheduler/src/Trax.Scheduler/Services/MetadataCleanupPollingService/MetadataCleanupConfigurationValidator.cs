using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;

namespace Trax.Scheduler.Services.MetadataCleanupPollingService;

/// <summary>
/// Refuses at startup a metadata cleanup configuration whose per-train retentions contradict
/// each other, before the first sweep runs.
/// </summary>
/// <remarks>
/// <see cref="MetadataCleanupConfiguration.AddTrainType(string, TimeSpan)"/> catches the same
/// name declared twice, but it cannot catch a train declared once under its interface name and
/// again under its class name: those are unrelated strings until
/// <see cref="ITrainDiscoveryService"/> relates them, and it is not resolvable while the builder
/// is still running.
///
/// <para>So the check moves here, where discovery exists and the host has not yet served
/// anything. The sweep itself keeps the longest of two conflicting retentions and logs a warning,
/// which is the safe answer for a background loop that must not stop; this is the loud one, for
/// the case where somebody can still fix it.</para>
///
/// <para>Registered immediately before <see cref="MetadataCleanupPollingService"/>, because
/// .NET starts hosted services in registration order and that service sweeps as soon as it
/// starts.</para>
/// </remarks>
internal class MetadataCleanupConfigurationValidator(
    IServiceProvider serviceProvider,
    SchedulerConfiguration configuration
) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cleanupConfig = configuration.MetadataCleanup;
        if (cleanupConfig is null)
            return Task.CompletedTask;

        using var scope = serviceProvider.CreateScope();
        var discoveryService = scope.ServiceProvider.GetService<ITrainDiscoveryService>();

        MetadataRetentionPlan.Build(cleanupConfig, discoveryService, out var conflicts);

        if (conflicts.Count == 0)
            return Task.CompletedTask;

        throw new InvalidOperationException(
            "Metadata cleanup has conflicting retention periods for the same train:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, conflicts.Select(c => "  " + c.ToString()))
                + Environment.NewLine
                + "Declare each train once, under either its interface name or its class name."
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
