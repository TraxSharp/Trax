using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HostRow = Trax.Effect.Models.HostHeartbeat.HostHeartbeat;

namespace Trax.Effect.Data.Services.HostHeartbeats;

/// <summary>
/// Beats once when the host starts and then every <see cref="HostRow.Interval"/> until it stops,
/// whether or not the host has work. A failed beat is logged and the next one tries again: the
/// heartbeat never stops a host, and a host that cannot write its row shows as offline, which is
/// what an operator needs to see.
/// </summary>
internal sealed class HostHeartbeatService(
    HostHeartbeatWriter writer,
    TimeProvider clock,
    ILogger<HostHeartbeatService>? logger = null
) : BackgroundService
{
    private bool _lastBeatFailed;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(HostRow.Interval, clock);
        do
        {
            await BeatOnce(stoppingToken);
        } while (await WaitForNextBeat(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextBeat(
        PeriodicTimer timer,
        CancellationToken stoppingToken
    )
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task BeatOnce(CancellationToken stoppingToken)
    {
        try
        {
            await writer.Beat(stoppingToken);
            _lastBeatFailed = false;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            // Warn once per run of failures, so a host on a database without the table (one that
            // skips migrations) does not log the same warning every 15 seconds.
            if (!_lastBeatFailed)
                logger?.LogWarning(
                    ex,
                    "The host heartbeat could not be written; this host shows as offline until a beat succeeds"
                );
            else
                logger?.LogDebug(ex, "The host heartbeat could not be written");
            _lastBeatFailed = true;
        }
    }

    /// <summary>
    /// Registers the heartbeat on a host with a data provider. Each provider calls it, once however
    /// many times it is asked.
    /// </summary>
    internal static void Register(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<HostHeartbeatWriter>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, HostHeartbeatService>()
        );
    }
}
