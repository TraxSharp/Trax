using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.Services.ChangeSignal;

/// <summary>
/// Default <see cref="ITraxChangeSignal"/>: a bounded, non-blocking channel of domain
/// signals drained by <see cref="ChangeSignalCoalescer"/>. When the buffer is full new
/// signals are dropped (a coalesced refetch is coming regardless, so losing one is
/// harmless), the <c>trax.change_signal.dropped</c> counter is incremented, and a
/// throttled warning is logged. Mirrors the shape of <c>TraxAuditChannel</c>.
/// </summary>
public sealed class TraxChangeSignal : ITraxChangeSignal, IDisposable
{
    /// <summary>Diagnostic meter name.</summary>
    public const string MeterName = "Trax.ChangeSignal";

    /// <summary>Counter name for dropped signals.</summary>
    public const string DroppedCounterName = "trax.change_signal.dropped";

    private readonly Channel<ChangeDomain> _channel;
    private readonly ILogger<TraxChangeSignal>? _logger;
    private readonly Meter _meter;
    private readonly Counter<long> _droppedCounter;
    private long _totalDropped;
    private long _lastWarnedAt;

    public TraxChangeSignal(ChangeSignalOptions options, ILogger<TraxChangeSignal>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        _channel = Channel.CreateBounded<ChangeDomain>(
            new BoundedChannelOptions(options.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            }
        );

        _meter = new Meter(MeterName);
        _droppedCounter = _meter.CreateCounter<long>(DroppedCounterName);
    }

    /// <inheritdoc />
    public void Notify(ChangeDomain domain)
    {
        if (_channel.Writer.TryWrite(domain))
            return;

        _droppedCounter.Add(1);
        var total = Interlocked.Increment(ref _totalDropped);
        var now = Environment.TickCount64;
        var lastWarn = Interlocked.Read(ref _lastWarnedAt);
        if (now - lastWarn >= 5_000)
        {
            if (Interlocked.CompareExchange(ref _lastWarnedAt, now, lastWarn) == lastWarn)
            {
                _logger?.LogWarning(
                    "Trax change-signal channel full. {DroppedTotal} signals dropped since process start.",
                    total
                );
            }
        }
    }

    /// <summary>Consumer read stream. Only the coalescer reads from this.</summary>
    public ChannelReader<ChangeDomain> Reader => _channel.Reader;

    /// <summary>Observed total dropped count since process start. For tests and diagnostics.</summary>
    public long TotalDropped => Interlocked.Read(ref _totalDropped);

    /// <summary>Signals no more entries will be enqueued (used on shutdown).</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
