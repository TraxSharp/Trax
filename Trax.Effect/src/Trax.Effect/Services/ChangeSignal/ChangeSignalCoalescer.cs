using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.Services.ChangeSignal;

/// <summary>
/// <see cref="BackgroundService"/> that drains <see cref="TraxChangeSignal"/>, collapses a
/// burst of signals within <see cref="ChangeSignalOptions.CoalesceWindow"/> into the distinct
/// set of domains that changed, and hands that set to every registered
/// <see cref="IChangeSignalSink"/>. Collapsing at the source means a storm of writes (a
/// dispatch cycle touching thousands of work-queue rows) produces at most one refetch nudge
/// per window instead of a per-row firehose.
/// </summary>
public sealed class ChangeSignalCoalescer(
    TraxChangeSignal signal,
    IServiceProvider serviceProvider,
    ChangeSignalOptions options,
    TimeProvider timeProvider,
    ILogger<ChangeSignalCoalescer>? logger = null
) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainWindowAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Change-signal coalescer loop threw. Continuing.");
            }
        }
    }

    private async Task DrainWindowAsync(CancellationToken ct)
    {
        // Block until at least one signal is available, then open a coalesce window.
        if (!await signal.Reader.WaitToReadAsync(ct))
            return;

        var domains = new HashSet<ChangeDomain>();
        DrainAvailable(domains);

        var windowEnd = Task.Delay(options.CoalesceWindow, timeProvider, ct);
        while (!windowEnd.IsCompleted)
        {
            var readReady = signal.Reader.WaitToReadAsync(ct).AsTask();
            var winner = await Task.WhenAny(readReady, windowEnd);
            if (winner == windowEnd)
                break;

            // The channel completed (shutdown) — flush what we have and stop coalescing.
            if (!await readReady)
                break;

            DrainAvailable(domains);
        }

        if (domains.Count > 0)
            await FlushAsync(domains, ct);
    }

    private void DrainAvailable(HashSet<ChangeDomain> domains)
    {
        while (signal.Reader.TryRead(out var domain))
            domains.Add(domain);
    }

    private async Task FlushAsync(IReadOnlyCollection<ChangeDomain> domains, CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var sinks = scope.ServiceProvider.GetServices<IChangeSignalSink>();

        foreach (var sink in sinks)
        {
            try
            {
                await sink.FlushAsync(domains, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogError(
                    ex,
                    "Change-signal sink {Sink} threw while flushing {Count} domain(s).",
                    sink.GetType().Name,
                    domains.Count
                );
            }
        }
    }
}
