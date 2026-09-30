using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.Services.ChangeSignal;

/// <summary>
/// Default <see cref="ITraxChangeSignal"/>: the set of domains with a change not yet read,
/// drained by <see cref="ChangeSignalCoalescer"/>. A domain is queued once; notifying it again
/// while it is still waiting adds nothing, because the pending signal already covers the new
/// change. A burst for one domain therefore never crowds out another domain's signal. Reading a
/// domain clears it, so a change after the read queues it again. A signal raised after
/// <see cref="Complete"/> is dropped, the <c>trax.change_signal.dropped</c> counter is
/// incremented, and a throttled warning is logged.
/// </summary>
public sealed class TraxChangeSignal : ITraxChangeSignal, IDisposable
{
    /// <summary>Diagnostic meter name.</summary>
    public const string MeterName = "Trax.ChangeSignal";

    /// <summary>Counter name for dropped signals.</summary>
    public const string DroppedCounterName = "trax.change_signal.dropped";

    private readonly Channel<ChangeDomain> _channel;
    private readonly PendingReader _reader;

    // One bit per ChangeDomain value: set while that domain sits in the channel unread.
    private int _pending;
    private readonly ILogger<TraxChangeSignal>? _logger;
    private readonly Meter _meter;
    private readonly Counter<long> _droppedCounter;
    private long _totalDropped;
    private long _lastWarnedAt;

    /// <summary>
    /// Creates the signal and registers the <see cref="MeterName"/> meter. The buffer holds at most one entry per
    /// domain, whatever <see cref="ChangeSignalOptions.ChannelCapacity"/> says. Registered as a singleton by <c>AddTrax</c>; not intended to be
    /// constructed directly.
    /// </summary>
    /// <param name="options">Buffer sizing. Throws <see cref="ArgumentNullException"/> when <c>null</c>.</param>
    /// <param name="logger">Receives the throttled warning logged when signals are dropped; optional.</param>
    public TraxChangeSignal(ChangeSignalOptions options, ILogger<TraxChangeSignal>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        _channel = Channel.CreateBounded<ChangeDomain>(
            new BoundedChannelOptions(Math.Max(options.ChannelCapacity, DomainCount))
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            }
        );

        _reader = new PendingReader(this);
        _meter = new Meter(MeterName);
        _droppedCounter = _meter.CreateCounter<long>(DroppedCounterName);
    }

    /// <inheritdoc />
    public void Notify(ChangeDomain domain)
    {
        var bit = Bit(domain);
        if (bit != 0 && (Interlocked.Or(ref _pending, bit) & bit) != 0)
            return; // Already waiting to be read: that signal covers this change.

        if (_channel.Writer.TryWrite(domain))
            return;

        // Only a completed channel refuses a write: it has room for every domain.
        if (bit != 0)
            Interlocked.And(ref _pending, ~bit);

        _droppedCounter.Add(1);
        var total = Interlocked.Increment(ref _totalDropped);
        var now = Environment.TickCount64;
        var lastWarn = Interlocked.Read(ref _lastWarnedAt);
        if (now - lastWarn >= 5_000)
        {
            if (Interlocked.CompareExchange(ref _lastWarnedAt, now, lastWarn) == lastWarn)
            {
                _logger?.LogWarning(
                    "Trax change-signal raised after shutdown. {DroppedTotal} signals dropped since process start.",
                    total
                );
            }
        }
    }

    /// <summary>
    /// Consumer read stream, holding each pending domain once. Only the coalescer reads from this. Reading a
    /// domain clears it from the pending set, so the next change to it is queued again.
    /// </summary>
    public ChannelReader<ChangeDomain> Reader => _reader;

    /// <summary>Signals raised after <see cref="Complete"/>, which are dropped, since process start. For tests and diagnostics.</summary>
    public long TotalDropped => Interlocked.Read(ref _totalDropped);

    /// <summary>Signals no more entries will be enqueued (used on shutdown).</summary>
    public void Complete() => _channel.Writer.TryComplete();

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    private static readonly int DomainCount = Enum.GetValues<ChangeDomain>().Length;

    // A value outside the enum's first 32 members has no bit and is queued without dedupe.
    private static int Bit(ChangeDomain domain) =>
        (int)domain is >= 0 and < 32 ? 1 << (int)domain : 0;

    private void Cleared(ChangeDomain domain) => Interlocked.And(ref _pending, ~Bit(domain));

    /// <summary>The channel's reader, clearing each domain from the pending set as it is read.</summary>
    private sealed class PendingReader(TraxChangeSignal owner) : ChannelReader<ChangeDomain>
    {
        private ChannelReader<ChangeDomain> Inner => owner._channel.Reader;

        public override bool TryRead(out ChangeDomain item)
        {
            if (!Inner.TryRead(out item))
                return false;
            owner.Cleared(item);
            return true;
        }

        public override ValueTask<bool> WaitToReadAsync(
            CancellationToken cancellationToken = default
        ) => Inner.WaitToReadAsync(cancellationToken);

        public override Task Completion => Inner.Completion;

        public override bool CanCount => Inner.CanCount;

        public override int Count => Inner.Count;

        public override bool CanPeek => Inner.CanPeek;

        public override bool TryPeek(out ChangeDomain item) => Inner.TryPeek(out item);
    }
}
