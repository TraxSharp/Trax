using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// Singleton bounded channel that buffers audit entries between the listener
/// (producer) and the background writer (consumer). Drops new entries when
/// full, logs a throttled warning, and emits the <c>trax.audit.dropped</c>
/// meter counter, which also counts every entry the writer accepted and could
/// not write.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed class TraxAuditChannel : IDisposable
{
    /// <summary>Diagnostic meter name.</summary>
    public const string MeterName = "Trax.Audit";

    /// <summary>
    /// Counter name for dropped audit entries: refused by a full channel, refused by the sink
    /// after every retry, or unwritten when shutdown ran out of time. Alert on non-zero values.
    /// </summary>
    public const string DroppedCounterName = "trax.audit.dropped";

    private readonly Channel<TraxAuditEntry> _channel;
    private readonly ILogger<TraxAuditChannel> _logger;
    private readonly Meter _meter;
    private readonly Counter<long> _droppedCounter;
    private long _totalDropped;
    private long _lastWarnedAt;

    /// <summary>Creates the channel using the configured capacity.</summary>
    public TraxAuditChannel(IOptions<TraxAuditOptions> options, ILogger<TraxAuditChannel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        _channel = Channel.CreateBounded<TraxAuditEntry>(
            new BoundedChannelOptions(options.Value.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            }
        );

        _meter = new Meter(MeterName);
        // OpenTelemetry naming: a dotted lowercase name, a curly-brace annotation for a unit
        // that is a count of things, and no "_total" suffix (exporters add their own).
        _droppedCounter = _meter.CreateCounter<long>(
            DroppedCounterName,
            unit: "{entry}",
            description: "Audit entries that were accepted or offered and never reached the sink."
        );
    }

    /// <summary>
    /// Attempts to enqueue an audit entry without blocking. Returns <c>true</c>
    /// when accepted, <c>false</c> when dropped (the channel is full, or the writer has
    /// stopped accepting entries for shutdown). A drop is counted like every other one.
    /// </summary>
    internal bool TryEnqueue(TraxAuditEntry entry)
    {
        if (_channel.Writer.TryWrite(entry))
            return true;

        RecordDropped(1, "Trax audit channel refused an entry (full or shutting down).");
        return false;
    }

    /// <summary>
    /// Counts <paramref name="count"/> entries that will never reach the sink: the channel
    /// refused them, the sink refused their batch after every retry, or shutdown ran out of
    /// time before they were written. Every drop path goes through here, so
    /// <c>trax.audit.dropped</c> and <see cref="TotalDropped"/> count each lost entry once.
    /// </summary>
    internal void RecordDropped(int count, string reason)
    {
        if (count <= 0)
            return;

        _droppedCounter.Add(count);
        var total = Interlocked.Add(ref _totalDropped, count);
        var now = Environment.TickCount64;
        var lastWarn = Interlocked.Read(ref _lastWarnedAt);
        if (now - lastWarn >= 5_000)
        {
            if (Interlocked.CompareExchange(ref _lastWarnedAt, now, lastWarn) == lastWarn)
            {
                _logger.LogWarning(
                    "{Reason} {DroppedTotal} audit entries dropped since process start.",
                    reason,
                    total
                );
            }
        }
    }

    /// <summary>Consumer read stream. Only the writer service reads from this.</summary>
    internal ChannelReader<TraxAuditEntry> Reader => _channel.Reader;

    /// <summary>
    /// Signals no more entries will be enqueued (used on shutdown). An entry offered after
    /// this is refused and counted as dropped.
    /// </summary>
    internal void Complete() => _channel.Writer.TryComplete();

    /// <summary>Observed total dropped count since process start. For tests and diagnostics.</summary>
    public long TotalDropped => Interlocked.Read(ref _totalDropped);

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
