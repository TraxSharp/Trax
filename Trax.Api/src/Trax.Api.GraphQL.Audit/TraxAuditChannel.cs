using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Trax.Api.GraphQL.Audit;

/// <summary>
/// Singleton bounded channel that buffers audit entries between the listener
/// (producer) and the background writer (consumer). Drops new entries when
/// full, logs a throttled warning, and emits the <c>trax.audit.dropped</c>
/// meter counter.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public sealed class TraxAuditChannel : IDisposable
{
    /// <summary>Diagnostic meter name.</summary>
    public const string MeterName = "Trax.Audit";

    /// <summary>Counter name for dropped audit entries. Alert on non-zero values.</summary>
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
        _droppedCounter = _meter.CreateCounter<long>(DroppedCounterName);
    }

    /// <summary>
    /// Attempts to enqueue an audit entry without blocking. Returns <c>true</c>
    /// when accepted, <c>false</c> when dropped (channel full). Drops increment
    /// the <c>trax.audit.dropped</c> meter and emit a throttled warning log.
    /// </summary>
    public bool TryEnqueue(TraxAuditEntry entry)
    {
        if (_channel.Writer.TryWrite(entry))
            return true;

        _droppedCounter.Add(1);
        var total = Interlocked.Increment(ref _totalDropped);
        var now = Environment.TickCount64;
        var lastWarn = Interlocked.Read(ref _lastWarnedAt);
        if (now - lastWarn >= 5_000)
        {
            if (Interlocked.CompareExchange(ref _lastWarnedAt, now, lastWarn) == lastWarn)
            {
                _logger.LogWarning(
                    "Trax audit channel full. {DroppedTotal} entries dropped since process start.",
                    total
                );
            }
        }
        return false;
    }

    /// <summary>Consumer read stream. Only the writer service reads from this.</summary>
    public ChannelReader<TraxAuditEntry> Reader => _channel.Reader;

    /// <summary>Signals no more entries will be enqueued (used on shutdown).</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <summary>Observed total dropped count since process start. For tests and diagnostics.</summary>
    public long TotalDropped => Interlocked.Read(ref _totalDropped);

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
