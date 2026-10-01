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
/// <para>
/// On shutdown the writer stops accepting entries, then writes every entry it already accepted,
/// for as long as the host allows: <see cref="StopAsync"/> runs until the channel is empty or
/// the token the host passes fires, which is <c>HostOptions.ShutdownTimeout</c> (30 seconds by
/// default). Anything still unwritten at that point, including a batch a sink is stuck on, is
/// counted in <c>trax.audit.dropped</c>. Every entry the listener offered is therefore either
/// handed to the sink or counted.
/// </para>
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

    /// <summary>
    /// Cancelled when shutdown runs out of time. Sink writes, retry backoff and channel reads
    /// observe this rather than the stopping token, because the stopping token fires as soon as
    /// shutdown begins and the drain has to outlive it.
    /// </summary>
    private readonly CancellationTokenSource _abort = new();

    /// <summary>
    /// Guards the channel reads and <see cref="_held"/>, so an abandoning <see cref="StopAsync"/>
    /// and the loop never both claim the same entry.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>Entries read from the channel and neither written nor counted as dropped.</summary>
    private int _held;

    /// <summary>Set once shutdown has given up and counted everything outstanding.</summary>
    private bool _abandoned;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ct = _abort.Token;
        var batch = new List<TraxAuditEntry>(_options.BatchSize);

        while (true)
        {
            try
            {
                if (!await DrainBatchAsync(batch, ct))
                    return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // StopAsync ran out of time and has counted what was outstanding.
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Trax audit writer loop threw. Dropping batch of {Count} and continuing.",
                    batch.Count
                );
                Resolve(batch, dropped: true, "Trax audit writer loop threw.");
            }
        }
    }

    /// <summary>
    /// Stops accepting entries, then waits for the loop to write every entry already accepted.
    /// When <paramref name="cancellationToken"/> fires first (the host's shutdown timeout), the
    /// writer gives up: the in-flight batch and everything left in the channel are counted as
    /// dropped, and this returns without waiting for a sink that does not observe cancellation.
    /// </summary>
    /// <remarks>
    /// The standard drain for a channel-fed <see cref="BackgroundService"/>: complete the writer
    /// side, let the reader run the channel dry, bound the wait with the token the host passes.
    /// </remarks>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        channel.Complete();

        var loop = ExecuteTask;
        if (loop is not null)
        {
            try
            {
                await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Abandon();
            }
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        // Not disposed: it has no timer, and a loop still blocked in a sink may observe it.
        _abort.Cancel();
        base.Dispose();
    }

    /// <summary>
    /// Counts every outstanding entry as dropped and cancels the loop's sink write, retry wait or
    /// read. Runs once, under the gate, so the loop cannot read another entry afterwards.
    /// </summary>
    private void Abandon()
    {
        int dropped;
        lock (_gate)
        {
            if (_abandoned)
                return;
            _abandoned = true;

            dropped = _held;
            _held = 0;
            while (channel.Reader.TryRead(out _))
                dropped++;
        }

        _abort.Cancel();
        logger.LogError(
            "Trax audit writer ran out of shutdown time with {Count} entries unwritten.",
            dropped
        );
        channel.RecordDropped(dropped, "Trax audit writer ran out of shutdown time.");
    }

    /// <summary>Reads one entry under the gate, unless shutdown has abandoned the drain.</summary>
    private bool TryTake(List<TraxAuditEntry> batch)
    {
        lock (_gate)
        {
            if (_abandoned || !channel.Reader.TryRead(out var entry))
                return false;

            batch.Add(entry);
            _held++;
            return true;
        }
    }

    /// <summary>
    /// Marks the batch written, or counts it as dropped, and clears it. Does nothing to the counts
    /// once shutdown has abandoned the drain, because <see cref="Abandon"/> already counted it.
    /// </summary>
    private void Resolve(List<TraxAuditEntry> batch, bool dropped, string reason)
    {
        var count = batch.Count;
        batch.Clear();
        lock (_gate)
        {
            if (_abandoned)
                return;
            _held -= count;
        }

        if (dropped)
            channel.RecordDropped(count, reason);
    }

    /// <summary>
    /// Fills one batch and hands it to the sink. Returns <c>false</c> once the channel is
    /// completed and empty, which ends the loop.
    /// </summary>
    private async Task<bool> DrainBatchAsync(List<TraxAuditEntry> batch, CancellationToken ct)
    {
        if (!await channel.Reader.WaitToReadAsync(ct))
            return false;

        using var flushCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        flushCts.CancelAfter(_options.FlushInterval);

        while (batch.Count < _options.BatchSize)
        {
            if (TryTake(batch))
                continue;

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
        {
            var written = await FlushAsync(batch, ct);
            Resolve(batch, dropped: !written, "Trax audit sink refused a batch after every retry.");
        }

        return true;
    }

    /// <summary>Returns <c>true</c> when the sink accepted the batch, <c>false</c> after the last retry.</summary>
    private async Task<bool> FlushAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var sink = scope.ServiceProvider.GetRequiredService<ITraxAuditSink>();
                await sink.WriteAsync(batch, ct);
                return true;
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
                await Task.Delay(delay, timeProvider, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Trax audit sink failed after {Max} retries. Dropping batch of {Count}.",
                    _options.MaxRetries,
                    batch.Count
                );
                return false;
            }
        }
    }
}
