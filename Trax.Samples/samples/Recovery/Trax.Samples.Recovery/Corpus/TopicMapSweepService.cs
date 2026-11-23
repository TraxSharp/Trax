using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Samples.Recovery.Machines;

namespace Trax.Samples.Recovery.Corpus;

/// <summary>Runs <see cref="TopicMapSweeper"/> every <see cref="Interval"/>.</summary>
public sealed class TopicMapSweepService(
    TopicMapSweeper sweeper,
    TimeSpan interval,
    ILogger<TopicMapSweepService> logger
) : BackgroundService
{
    /// <summary>How long between sweeps.</summary>
    public TimeSpan Interval => interval;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var deleted = await sweeper.SweepAsync(stoppingToken).ConfigureAwait(false);
                if (deleted.Count > 0)
                    logger.LogInformation(
                        "Deleted the pairs of {Count} topic map(s) no draft points at",
                        deleted.Count
                    );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The database may be restarting; the next sweep tries again.
                logger.LogWarning(ex, "The topic map sweep failed");
            }
        }
    }
}
