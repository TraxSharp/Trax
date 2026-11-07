using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;

namespace Trax.StateMachine.Persistence.FromIr;

/// <summary>
/// Fires a trigger on one stored instance as the server does (ADR <c>Trax.Docs/adr/0058</c>): the engine's advance,
/// then the timer's write path, which starts and cancels runs and timers as states are entered and left, and queues the
/// policies the move sets off. A policy fires through it, and so does a feature run's step that no person takes.
/// </summary>
internal static class ServerFire
{
    internal abstract record Fired
    {
        internal sealed record Moved(string State) : Fired;

        internal sealed record Refused(RefusalCode Code, string Message) : Fired;

        /// <summary>Nothing was fired, and why: the instance is gone, unreadable, or not this host's. Trying again does not help.</summary>
        internal sealed record Failed(string Why) : Fired;

        /// <summary>
        /// Nothing was fired because another write holds the instance or reached it first, or an effect is in flight on
        /// it. The same fire may succeed later.
        /// </summary>
        internal sealed record Busy(string Why) : Fired;
    }

    internal static async Task<Fired> Fire(
        IServiceProvider services,
        IDataContext context,
        IReadOnlyDictionary<string, IMachineInternals> machines,
        long rowId,
        string trigger,
        JsonNode? input,
        TransitionNote note,
        Func<string, string?>? beforeFiring,
        CancellationToken cancellationToken
    )
    {
        var outbox = services.GetRequiredService<InvokeOutbox>();
        var store = outbox.StoreOver(context, services.GetService<ISqlDialect>());
        var due = await context
            .SnapshotDrafts.AsNoTracking()
            .Where(d => d.RowId == rowId)
            .Select(x => new DueRow(
                x.RowId,
                x.Id,
                x.Machine,
                x.OwnerKind,
                x.UserKey,
                x.Version,
                x.State,
                x.Context,
                x.Revision,
                x.InvokeToken
            ))
            .FirstOrDefaultAsync(cancellationToken);
        if (due is null)
            return new Fired.Failed("the instance is gone");
        if (beforeFiring?.Invoke(due.State) is { } skip)
            return new Fired.Failed(skip);
        if (!machines.TryGetValue(due.Machine, out var machine))
            return new Fired.Failed($"this host does not serve {due.Machine}");
        if (!await store.LockDue(due, cancellationToken))
            return new Fired.Busy("another write holds it");
        if (machine.Rehydrate(due.Json) is not RehydrationResult.Ok readable)
            return new Fired.Failed("this host cannot read it");

        var current = readable.Snapshot;
        switch (machine.Advance(current, trigger, input))
        {
            case AdvanceResult.Transitioned transitioned:
                var next = transitioned.Snapshot;
                var write = await outbox.FireTimer(
                    context,
                    store,
                    due,
                    next,
                    leaving: machine.Entering(current.State) is not null
                        && next.State != current.State,
                    entering: machine.Entering(next.State),
                    cancellationToken,
                    trigger,
                    note
                );
                return write switch
                {
                    InvokeWrite.Written => new Fired.Moved(next.State),
                    InvokeWrite.Refused refused => new Fired.Failed(
                        refused.Exception?.Message ?? refused.Message
                    ),
                    _ => new Fired.Busy(
                        "another write reached it first, or an effect is in flight"
                    ),
                };
            case AdvanceResult.Rejected rejected:
                return new Fired.Refused(
                    rejected.Reason,
                    rejected.Detail ?? rejected.Reason.ToWire()
                );
            default:
                return new Fired.Failed("unknown result");
        }
    }

    /// <summary>The row of an instance, or null when it has none.</summary>
    internal static Task<long?> RowOf(
        IDataContext context,
        string machine,
        Guid id,
        CancellationToken cancellationToken
    ) =>
        context
            .SnapshotDrafts.AsNoTracking()
            .Where(d => d.Machine == machine && d.Id == id)
            .Select(d => (long?)d.RowId)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Puts a stored instance in <paramref name="state"/> with <paramref name="contextJson"/>, as a test arranges a
    /// database: written as a save, so it sets off no policy, through the timer's write path, so the timer and the run
    /// of the state it enters start as they would. A feature run's Given arranges an instance this way.
    /// </summary>
    internal static async Task<Fired> Seed(
        IServiceProvider services,
        IDataContext context,
        IReadOnlyDictionary<string, IMachineInternals> machines,
        long rowId,
        string state,
        JsonObject? contextJson,
        CancellationToken cancellationToken
    )
    {
        var outbox = services.GetRequiredService<InvokeOutbox>();
        var store = outbox.StoreOver(context, services.GetService<ISqlDialect>());
        var due = await context
            .SnapshotDrafts.AsNoTracking()
            .Where(d => d.RowId == rowId)
            .Select(x => new DueRow(
                x.RowId,
                x.Id,
                x.Machine,
                x.OwnerKind,
                x.UserKey,
                x.Version,
                x.State,
                x.Context,
                x.Revision,
                x.InvokeToken
            ))
            .FirstOrDefaultAsync(cancellationToken);
        if (due is null)
            return new Fired.Failed("the instance is gone");
        if (!machines.TryGetValue(due.Machine, out var machine))
            return new Fired.Failed($"this host does not serve {due.Machine}");
        if (!await store.LockDue(due, cancellationToken))
            return new Fired.Busy("another write holds it");
        if (machine.Rehydrate(due.Json) is not RehydrationResult.Ok readable)
            return new Fired.Failed("this host cannot read it");
        var current = readable.Snapshot;
        var next = current with { State = state };
        if (contextJson is not null)
            next = next with { Context = contextJson };
        var owner = new DraftOwner(due.OwnerKind, due.UserKey);
        var write = await outbox.FireTimer(
            context,
            store,
            due,
            next,
            leaving: machine.Entering(current.State) is not null && state != current.State,
            entering: state != current.State ? machine.Entering(state) : null,
            cancellationToken,
            null,
            TransitionNote.Save(owner)
        );
        return write switch
        {
            InvokeWrite.Written => new Fired.Moved(state),
            InvokeWrite.Refused refused => new Fired.Failed(
                refused.Exception?.Message ?? refused.Message
            ),
            _ => new Fired.Busy("another write reached it first, or an effect is in flight"),
        };
    }
}
