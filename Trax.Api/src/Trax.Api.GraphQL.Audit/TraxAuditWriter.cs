using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// <see cref="BackgroundService"/> that drains <see cref="TraxAuditChannel"/>,
/// batches entries by <see cref="TraxAuditOptions.BatchSize"/> or
/// <see cref="TraxAuditOptions.FlushInterval"/>, and hands each batch to an
/// <see cref="ITraxAuditSink"/> with retry-and-drop semantics.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed class TraxAuditWriter(
    TraxAuditChannel channel,
    IServiceProvider serviceProvider,
    IOptions<TraxAuditOptions> options,
    TimeProvider timeProvider,
    ILogger<TraxAuditWriter> logger
) : BackgroundService
{
    private readonly TraxAuditOptions _options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<TraxAuditEntry>(_options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainBatchAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Trax audit writer loop threw. Continuing.");
                batch.Clear();
            }
        }
    }

    private async Task DrainBatchAsync(List<TraxAuditEntry> batch, CancellationToken ct)
    {
        if (!await channel.Reader.WaitToReadAsync(ct))
            return;

        batch.Clear();
        using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        flushCts.CancelAfter(_options.FlushInterval);

        while (batch.Count < _options.BatchSize)
        {
            if (channel.Reader.TryRead(out var entry))
            {
                batch.Add(entry);
                continue;
            }

            if (batch.Count == 0)
            {
                if (!await channel.Reader.WaitToReadAsync(ct))
                    break;
                continue;
            }

            try
            {
                if (!await channel.Reader.WaitToReadAsync(flushCts.Token))
                    break;
            }
            catch (OperationCanceledException)
                when (flushCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                break;
            }
        }

        if (batch.Count > 0)
            await FlushAsync(batch, ct);
    }

    private async Task FlushAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
    {
        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var sink = scope.ServiceProvider.GetRequiredService<ITraxAuditSink>();
                await sink.WriteAsync(batch, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _options.MaxRetries)
            {
                logger.LogWarning(
                    ex,
                    "Trax audit sink failed on attempt {Attempt}/{Max}. Retrying.",
                    attempt + 1,
                    _options.MaxRetries
                );
                var delay = TimeSpan.FromMilliseconds(
                    _options.RetryBackoff.TotalMilliseconds * Math.Pow(2, attempt)
                );
                try
                {
                    await Task.Delay(delay, timeProvider, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Trax audit sink failed after {Max} retries. Dropping batch of {Count}.",
                    _options.MaxRetries,
                    batch.Count
                );
                return;
            }
        }
    }
}
