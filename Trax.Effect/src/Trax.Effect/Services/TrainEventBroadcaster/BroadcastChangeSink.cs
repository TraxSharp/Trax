using System.Reflection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ChangeSignal;

namespace Trax.Effect.Services.TrainEventBroadcaster;

/// <summary>
/// <see cref="IChangeSignalSink"/> that forwards coalesced change signals to other processes
/// over the existing <see cref="ITrainEventBroadcaster"/> transport. Each domain becomes a
/// <see cref="TrainLifecycleEventMessage"/> tagged with
/// <see cref="TrainLifecycleEventMessage.DataChangedEventType"/> and stamped with this process's
/// executor, so the receiving side's local-event filter drops the loopback in the process that
/// originated it (which already delivered the signal to its own subscribers in-process).
/// Registered by <c>UseBroadcaster()</c>.
/// </summary>
public sealed class BroadcastChangeSink : IChangeSignalSink
{
    private static readonly string? LocalExecutor = Assembly
        .GetEntryAssembly()
        ?.GetAssemblyProject();

    private readonly ITrainEventBroadcaster _broadcaster;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BroadcastChangeSink>? _logger;

    public BroadcastChangeSink(
        ITrainEventBroadcaster broadcaster,
        TimeProvider timeProvider,
        ILogger<BroadcastChangeSink>? logger = null
    )
    {
        _broadcaster = broadcaster;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task FlushAsync(IReadOnlyCollection<ChangeDomain> domains, CancellationToken ct)
    {
        foreach (var domain in domains)
        {
            var message = new TrainLifecycleEventMessage(
                MetadataId: 0,
                ExternalId: string.Empty,
                TrainName: string.Empty,
                TrainState: string.Empty,
                Timestamp: _timeProvider.GetUtcNow().UtcDateTime,
                FailureJunction: null,
                FailureReason: null,
                EventType: TrainLifecycleEventMessage.DataChangedEventType,
                Executor: LocalExecutor,
                Output: null,
                ChangeDomain: domain.ToString()
            );

            _logger?.LogDebug("Broadcasting data-change signal for domain {Domain}.", domain);
            await _broadcaster.PublishAsync(message, ct);
        }
    }
}
