using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Subscriptions;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// One push stream: the events of one row watch, and its place under the limits, both released once
/// when the stream ends or HotChocolate disposes it (the client completed it, or the socket went away).
/// </summary>
internal sealed class MachineChangedStream(
    SnapshotWatch? watch,
    IDisposable? lease,
    Func<SnapshotRowChange, (MachineChange Event, bool Ends)?> present,
    MachineChange? only = null
) : ISourceStream<MachineChange>
{
    private int _disposed;

    /// <summary>The watch rule asked again while the stream is open, or null for a stream that answers once.</summary>
    public SubscriptionRecheck? Recheck { get; init; }

    /// <summary>The last event of a stream <see cref="Recheck"/> ends.</summary>
    public MachineChange? RecheckRefused { get; init; }

    public IAsyncEnumerable<MachineChange> ReadEventsAsync() => Read<MachineChange>();

    IAsyncEnumerable<object?> ISourceStream.ReadEventsAsync() => Read<object?>();

    public ValueTask DisposeAsync() => ReleaseAsync();

    private async ValueTask ReleaseAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        if (watch is not null)
            await watch.DisposeAsync().ConfigureAwait(false);
        lease?.Dispose();
    }

    private async IAsyncEnumerable<TOut> Read<TOut>(
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        try
        {
            if (only is not null)
            {
                yield return (TOut)(object)only;
                yield break;
            }
            if (watch is null)
                yield break;

            var events = Events(watch, ct);
            if (Recheck is { } recheck && RecheckRefused is { } refused)
                events = recheck.Until(events, () => refused, ct);
            await foreach (var next in events.WithCancellation(ct).ConfigureAwait(false))
                yield return (TOut)(object)next;
        }
        finally
        {
            await ReleaseAsync().ConfigureAwait(false);
        }
    }

    private async IAsyncEnumerable<MachineChange> Events(
        SnapshotWatch watch,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        await foreach (
            var change in watch.Changes.ReadAllAsync(ct).WithCancellation(ct).ConfigureAwait(false)
        )
        {
            if (present(change) is not { } next)
                continue;
            yield return next.Event;
            if (next.Ends)
                yield break;
        }
    }
}
