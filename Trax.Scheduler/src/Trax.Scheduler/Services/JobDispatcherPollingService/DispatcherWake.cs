using System.Threading.Channels;

namespace Trax.Scheduler.Services.JobDispatcherPollingService;

/// <summary>
/// Ends the dispatcher's wait between cycles early, because work was queued. Signals coalesce:
/// any number of them before the dispatcher next looks starts one cycle, and one that arrives
/// while a cycle runs starts one more after it, since that cycle may have loaded the queue before
/// the new entry committed.
/// </summary>
internal sealed class DispatcherWake
{
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        }
    );

    /// <summary>Asks for a cycle. Never blocks and never throws.</summary>
    public void Signal() => _signal.Writer.TryWrite(true);

    /// <summary>Completes when a signal is waiting. Does not consume it.</summary>
    public Task WaitAsync(CancellationToken cancellationToken) =>
        _signal.Reader.WaitToReadAsync(cancellationToken).AsTask();

    /// <summary>Consumes the waiting signal, if there is one, because a cycle is about to start.</summary>
    public void Consume() => _signal.Reader.TryRead(out _);
}
