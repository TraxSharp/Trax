using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.DataContextTransaction;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Models;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>The invoking state a write enters: its train, how the run's input is built, and the owner's cap.</summary>
/// <param name="TrainType">The train's interface.</param>
/// <param name="CreateInput">Builds the run's input from the context the state is entered with.</param>
/// <param name="Limit">
/// The most live invoked runs the entering user may already hold, across every machine, for the entry to be allowed;
/// system owners are not capped.
/// </param>
internal sealed record EnteringInvoke(
    Type TrainType,
    Func<JsonObject, object?> CreateInput,
    int Limit
)
{
    /// <summary>
    /// The child machine the state starts instead of a train, or null when it invokes one. A spawn is not counted
    /// against the owner's live-run cap.
    /// </summary>
    public SpawnDefinition? Spawn { get; init; }
}

/// <summary>How a write through the <see cref="InvokeOutbox"/> ended.</summary>
internal abstract record InvokeWrite
{
    /// <summary>The snapshot, the token change and any queued run committed together.</summary>
    /// <param name="InvokeToken">The token the row holds now: the new run's external id, or null.</param>
    public sealed record Written(string? InvokeToken) : InvokeWrite
    {
        /// <summary>
        /// The due time the write left on the row, where the writer reads it: when the timer of its state fires, or
        /// null when that state has none.
        /// </summary>
        public DateTimeOffset? DueAt { get; init; }
    }

    /// <summary>The row changed since it was read, or (on insert) already exists; nothing was written.</summary>
    public sealed record Conflict : InvokeWrite;

    /// <summary>The write was refused; nothing was written.</summary>
    public sealed record Refused(RefusalCode Code, string Message, Exception? Exception = null)
        : InvokeWrite;

    private InvokeWrite() { }
}

/// <summary>The most drafts one user may hold, and the idle time after which one no longer counts.</summary>
/// <param name="Max">The cap.</param>
/// <param name="Ttl">The draft TTL, or null when drafts never expire.</param>
/// <param name="Kept">
/// For a row that does not record whether the server put it in its state, the states that make it a record rather than
/// a draft, as <see cref="DraftExpiry.Key"/> writes them; null names none.
/// </param>
internal readonly record struct DraftCap(
    int Max,
    TimeSpan? Ttl,
    IReadOnlyCollection<string>? Kept = null
);

/// <summary>How a create through <see cref="InvokeOutbox.Create"/> ended.</summary>
internal abstract record CreateWrite
{
    /// <summary>The row was inserted, with its run when its state invokes one.</summary>
    public sealed record Created(Guid Id, WrittenRow Row) : CreateWrite;

    /// <summary>The user's start with the same request id and input created this row earlier.</summary>
    public sealed record Existing(Guid Id, StoredSnapshot Stored) : CreateWrite;

    /// <summary>The user's start with the same request id was started with a different input.</summary>
    public sealed record Reused : CreateWrite;

    /// <summary>The row already exists (a save racing another create of the same id); nothing was written.</summary>
    public sealed record Conflict : CreateWrite;

    /// <summary>The create was refused; nothing was written.</summary>
    public sealed record Refused(RefusalCode Code, string Message, Exception? Exception = null)
        : CreateWrite;

    private CreateWrite() { }
}

/// <summary>
/// Writes a snapshot that enters or leaves an invoking state in one transaction with everything that goes with it:
/// leaving cancels the old run and clears the row's invoke token; entering queues the new run's work queue entry
/// through the launcher, on the same data context, and sets the token to the entry's external id. A crash or a
/// refusal at any point leaves none of it, so no machine waits on a run that was never queued and no run is queued
/// for a machine that never moved.
/// </summary>
/// <remarks>
/// <para>Every write goes through a data context of its own, created for it and disposed after it, never the
/// request's. A train's junction may start or advance an instance while its run holds tracked writes on the scoped
/// context, which commit once, when the run finishes (Effect ADR 0021); saving through that context here would
/// commit them early inside this transaction, and a rollback would leave them marked saved and lose them. For the
/// same reason a transaction the caller holds does not stop the write.</para>
/// <para>On Postgres a failed statement aborts the transaction, a unique violation on the invoke token included, so
/// every lost write is followed by a rollback and reported as a conflict; nothing is written after it.</para>
/// </remarks>
internal sealed class InvokeOutbox(IDataContextProviderFactory contexts, IServiceProvider services)
{
    /// <summary>The code an entry past the owner's live-run cap is refused with.</summary>
    public const RefusalCode LimitReached = RefusalCode.InvokeLimitReached;

    /// <summary>The code an entry is refused with when the entering user may not run the state's train.</summary>
    public const RefusalCode Forbidden = RefusalCode.InvokeForbidden;

    /// <summary>
    /// The code an outcome is refused with when it enters an invoking state of a user-owned instance: only a
    /// system-owned instance chains runs through outcomes.
    /// </summary>
    public const RefusalCode ChainRefused = RefusalCode.InvokeChainUserOwned;

    /// <summary>
    /// Writes <paramref name="next"/> over <paramref name="owner"/>'s row, guarded by <paramref name="expectedToken"/>,
    /// cancelling <paramref name="leavingToken"/>'s run and queueing <paramref name="entering"/>'s. The instance's
    /// history records <paramref name="trigger"/>, the command that moved it.
    /// </summary>
    public async Task<InvokeWrite> Advance(
        DraftOwner owner,
        Guid id,
        Snapshot next,
        Guid expectedToken,
        AppliedRequest? request,
        string? leavingToken,
        EnteringInvoke? entering,
        CancellationToken cancellationToken,
        string? trigger = null
    ) =>
        await InTransaction(
            async (context, store) =>
            {
                if (
                    entering is { Spawn: null }
                    && await OverLimit(
                        context,
                        owner,
                        leavingToken,
                        entering.Limit,
                        cancellationToken
                    )
                        is { } refused
                )
                    return refused;

                // Built before anything is written: the input mapping is the author's code and may throw.
                var launch = entering is null ? null : Prepare(entering, owner, next, id);
                if (launch is InvokeWrite.Refused bad)
                    return bad;
                var run = launch as Prepared;

                // The snapshot first: it is the one write that can lose a race, and losing it writes nothing else.
                // The store is always StoreOver's, which records the command in the instance's history.
                var wrote = await ((EfSnapshotStore)store).Update(
                    owner,
                    id,
                    next,
                    expectedToken,
                    request,
                    new InvokeTokenWrite(run?.Token),
                    TransitionNote.Command(owner, trigger ?? request?.Trigger),
                    cancellationToken
                );
                if (!wrote)
                    return new InvokeWrite.Conflict();

                if (leavingToken is not null)
                    await InvokedRunCancellation.CancelIn(context, leavingToken, cancellationToken);

                if (run is not null)
                    await LaunchIn(run, context, store, owner, cancellationToken);

                // The update holds the row until the transaction commits, so this reads the due time it wrote.
                var written = await store.Get(owner, next.Machine, id, cancellationToken);
                return new InvokeWrite.Written(run?.Token) { DueAt = written?.DueAt };
            },
            cancellationToken
        );

    /// <summary>
    /// Creates <paramref name="owner"/>'s row for <paramref name="snapshot"/> and, when its state invokes a train,
    /// queues the run and stores its token, in one transaction. <see cref="InvokeWrite.Conflict"/> when the row
    /// already exists.
    /// </summary>
    public async Task<InvokeWrite> Insert(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        EnteringInvoke? entering,
        CancellationToken cancellationToken
    ) =>
        await InTransaction(
            async (context, store) =>
            {
                if (
                    entering is { Spawn: null }
                    && await OverLimit(
                        context,
                        owner,
                        leavingToken: null,
                        entering.Limit,
                        cancellationToken
                    )
                        is { } refused
                )
                    return refused;

                var launch = entering is null ? null : Prepare(entering, owner, snapshot, id);
                if (launch is InvokeWrite.Refused bad)
                    return bad;
                var run = launch as Prepared;

                if (!await store.Insert(owner, id, snapshot, run?.Token, cancellationToken))
                    return new InvokeWrite.Conflict();

                if (run is not null)
                    await LaunchIn(run, context, store, owner, cancellationToken);

                return new InvokeWrite.Written(run?.Token);
            },
            cancellationToken
        );

    /// <summary>
    /// Applies a finished run's outcome: writes <paramref name="next"/> over the row that still holds
    /// <paramref name="invokeToken"/> and <paramref name="expectedToken"/> (<c>UPDATE ... WHERE invoke_token =</c>),
    /// replacing the token with the run <paramref name="entering"/> queues, or clearing it. The finished run needs
    /// no cancel. <see cref="InvokeWrite.Conflict"/> when the row no longer holds both tokens: the outcome was
    /// applied already, the state was left, or the row changed since it was read. The instance's history records
    /// <paramref name="trigger"/>, the outcome applied.
    /// </summary>
    public async Task<InvokeWrite> Deliver(
        DraftOwner owner,
        Guid id,
        string invokeToken,
        Snapshot next,
        Guid expectedToken,
        EnteringInvoke? entering,
        CancellationToken cancellationToken,
        string? trigger = null
    ) =>
        await InTransaction(
            async (context, store) =>
            {
                // No user is present for a run an outcome queues, so only a system-owned instance may chain one;
                // the startup check refuses a user-owned machine that declares such an edge, and this refuses the
                // write again rather than authorize it in the trusted scope.
                if (entering is not null && owner.Kind != SnapshotOwnerKind.System)
                    return new InvokeWrite.Refused(
                        ChainRefused,
                        "The step's work could not be started.",
                        new InvalidOperationException(
                            $"An outcome of '{next.Machine}' entered {next.State}, which invokes a train, but the "
                                + "instance is user-owned: a run an outcome queues has no user to authorize it. "
                                + "The startup check refuses such a machine."
                        )
                    );

                if (
                    entering is { Spawn: null }
                    && await OverLimit(
                        context,
                        owner,
                        leavingToken: invokeToken,
                        entering.Limit,
                        cancellationToken
                    )
                        is { } refused
                )
                    return refused;

                var launch = entering is null
                    ? null
                    : Prepare(entering, owner, next, id, fromOutcome: true);
                if (launch is InvokeWrite.Refused bad)
                    return bad;
                var run = launch as Prepared;

                // The conditional update is the one write that can lose, to another host applying the same
                // outcome or to the state being left; losing it writes nothing else.
                if (
                    !await ((EfSnapshotStore)store).ApplyOutcome(
                        invokeToken,
                        next,
                        run?.Token,
                        expectedToken,
                        trigger,
                        cancellationToken
                    )
                )
                    return new InvokeWrite.Conflict();

                if (run is not null)
                    await LaunchIn(run, context, store, owner, cancellationToken);

                // The update holds the row until the transaction commits, so this reads the due time it wrote.
                var written = await store.Get(owner, next.Machine, id, cancellationToken);
                return new InvokeWrite.Written(run?.Token) { DueAt = written?.DueAt };
            },
            cancellationToken
        );

    /// <summary>
    /// Creates <paramref name="owner"/>'s row for <paramref name="snapshot"/> in one transaction, under a lock on the
    /// user: for a <c>start</c> (<paramref name="start"/> set), a row the user's start with the same request id
    /// created is answered instead, marked active so an expiry sweep racing it keeps it, or refused as reused when
    /// its input differs; then the user's drafts are counted against <paramref name="cap"/>, and the row is inserted
    /// with the run its state invokes, if any. A refusal writes nothing.
    /// </summary>
    /// <param name="owner">The user who owns the new row.</param>
    /// <param name="id">The new row's id.</param>
    /// <param name="snapshot">The new row's snapshot.</param>
    /// <param name="start">The start's request id and input fingerprint; null for a draft a save creates.</param>
    /// <param name="entering">The invoking state the snapshot is in, or null.</param>
    /// <param name="cap">The most drafts the user may hold, or null for no cap.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task<CreateWrite> Create(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        StartRequest? start,
        EnteringInvoke? entering,
        DraftCap? cap,
        CancellationToken cancellationToken
    )
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var dialect = services.GetService<ISqlDialect>();
        var store = StoreOver(context, dialect);
        IDataContextTransaction? transaction = await context.BeginTransaction(cancellationToken);
        try
        {
            CreateWrite result;
            try
            {
                result = await CreateIn(
                    context,
                    store,
                    dialect,
                    owner,
                    id,
                    snapshot,
                    start,
                    entering,
                    cap,
                    cancellationToken
                );
            }
            catch (UnauthorizedAccessException)
            {
                result = new CreateWrite.Refused(
                    Forbidden,
                    "You may not start the work this step runs."
                );
            }

            if (result is CreateWrite.Created or CreateWrite.Existing)
            {
                await transaction.Commit();
                transaction.Dispose();
                services.GetService<SnapshotWriteSignal>()?.Written();
            }
            else
                await RollbackQuietly(transaction);
            transaction = null;
            return result;
        }
        catch
        {
            if (transaction is not null)
                await RollbackQuietly(transaction);
            throw;
        }
    }

    private async Task<CreateWrite> CreateIn(
        IDataContext context,
        EfSnapshotStore store,
        ISqlDialect? dialect,
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        StartRequest? start,
        EnteringInvoke? entering,
        DraftCap? cap,
        CancellationToken cancellationToken
    )
    {
        var userKey =
            owner.UserKey
            ?? throw new ArgumentException("Only a user's draft is created here.", nameof(owner));

        // One user's creates take turns, on every host, so a count and the insert after it cannot interleave with
        // another create of theirs. SQLite serializes writing transactions already (BEGIN IMMEDIATE).
        if (dialect is not null && context is DbContext db)
            await db.Database.ExecuteSqlRawAsync(
                dialect.LockSubject(),
                [$"trax:drafts:{userKey}"],
                cancellationToken
            );

        if (start is not null)
        {
            var existing = await store.FindStarted(
                owner,
                snapshot.Machine,
                start.RequestId,
                cancellationToken
            );
            if (existing is not null)
            {
                if (
                    !string.Equals(
                        existing.Fingerprint,
                        start.Fingerprint,
                        StringComparison.Ordinal
                    )
                )
                    return new CreateWrite.Reused();

                // A retry is activity: marking the row active moves its revision, so a sweep that read it as expired
                // deletes nothing, and the id answered here is never one it deletes. A row written since it was read
                // is active already.
                var touched = await store.Touch(
                    owner,
                    snapshot.Machine,
                    existing.Id,
                    existing.Stored.Token,
                    cancellationToken
                );
                var current = touched
                    ? existing.Stored with
                    {
                        Revision = existing.Stored.Revision + 1,
                    }
                    : (
                        await store.FindStarted(
                            owner,
                            snapshot.Machine,
                            start.RequestId,
                            cancellationToken
                        )
                    )?.Stored;
                return current is null
                    ? new CreateWrite.Conflict()
                    : new CreateWrite.Existing(existing.Id, current);
            }
        }

        if (cap is { } limit)
        {
            var held = await store.CountDrafts(
                userKey,
                limit.Ttl is { } ttl ? DateTimeOffset.UtcNow - ttl : null,
                limit.Kept,
                cancellationToken
            );
            if (held >= limit.Max)
                return new CreateWrite.Refused(
                    RefusalCode.TooManyDrafts,
                    $"You already have {held} drafts, the most allowed. Finish or discard one, then try again."
                );
        }

        if (
            entering is { Spawn: null }
            && await OverLimit(
                context,
                owner,
                leavingToken: null,
                entering.Limit,
                cancellationToken
            )
                is { } refused
        )
            return new CreateWrite.Refused(refused.Code, refused.Message, refused.Exception);

        var launch = entering is null ? null : Prepare(entering, owner, snapshot, id);
        if (launch is InvokeWrite.Refused bad)
            return new CreateWrite.Refused(bad.Code, bad.Message, bad.Exception);
        var run = launch as Prepared;

        var row = start is null
            ? await ((IRowReportingSnapshotStore)store).Insert(
                userKey,
                id,
                snapshot,
                cancellationToken
            )
            : await store.InsertStarted(owner, id, snapshot, run?.Token, start, cancellationToken);
        if (row is not { } written)
            return new CreateWrite.Conflict();

        if (run is not null)
            await LaunchIn(run, context, store, owner, cancellationToken);

        return new CreateWrite.Created(id, written);
    }

    // What entering a state starts, prepared before anything is written, with the token the row will hold.
    private abstract record Prepared(string Token);

    private sealed record PreparedLaunch(InvokedTrainLaunch Launch) : Prepared(Launch.ExternalId);

    private sealed record PreparedSpawn(string Token, Guid ChildId, Snapshot Child)
        : Prepared(Token);

    private object Prepare(
        EnteringInvoke entering,
        DraftOwner owner,
        Snapshot snapshot,
        Guid id,
        bool fromOutcome = false
    )
    {
        if (entering.Spawn is { } spawn)
            return PrepareSpawn(spawn, snapshot);

        object? input;
        try
        {
            input = entering.CreateInput(snapshot.Context);
        }
        catch (Exception ex)
        {
            return new InvokeWrite.Refused(
                RefusalCode.InternalError,
                "The step's work could not be started.",
                ex
            );
        }

        if (input is null)
            return new InvokeWrite.Refused(
                RefusalCode.InternalError,
                "The step's work could not be started.",
                new InvalidOperationException(
                    $"The input mapping of {entering.TrainType.Name} in '{snapshot.Machine}' state "
                        + $"{snapshot.State} returned null."
                )
            );

        return new PreparedLaunch(
            new InvokedTrainLaunch(
                entering.TrainType,
                input,
                Guid.NewGuid().ToString("N"),
                new InvokedBy(snapshot.Machine, id, owner.Kind),
                snapshot.State
            )
            {
                FromOutcome = fromOutcome,
                StarterHash =
                    owner.Kind == SnapshotOwnerKind.User
                        ? RunStarter.OfUserKey(owner.UserKey)
                        : null,
            }
        );
    }

    // A user may hold at most Limit live invoked runs, counted across every machine: runs an instance they own
    // queued that have not ended, whether still queued or dispatched and executing. A dispatched run whose state was
    // left is only flagged for cancel and runs on to its next junction, so it counts until it ends; a queued run
    // whose state is left now (leavingToken) is cancelled in this transaction, so it does not. The count is taken
    // under a transaction-scoped lock on the user on Postgres, so two concurrent entries by one user, on any hosts
    // and in any machines, cannot both pass it; SQLite serializes writing transactions already (BEGIN IMMEDIATE).
    // A user owner whose key names nobody cannot be counted, so it is refused rather than let past the cap.
    internal async Task<InvokeWrite.Refused?> OverLimit(
        IDataContext context,
        DraftOwner owner,
        string? leavingToken,
        int limit,
        CancellationToken cancellationToken
    )
    {
        if (owner.Kind != SnapshotOwnerKind.User)
            return null;
        if (owner.UserKey is not { } userKey || RunStarter.OfUserKey(userKey) is not { } starter)
            return new InvokeWrite.Refused(
                Forbidden,
                "The step's work could not be started.",
                new InvalidOperationException(
                    "A user-owned instance with no user key entered a state that invokes a train, so its live runs "
                        + "cannot be counted against the cap."
                )
            );

        if (services.GetService<ISqlDialect>() is { } dialect && context is DbContext db)
            await db.Database.ExecuteSqlRawAsync(
                dialect.LockSubject(),
                [$"trax:invoke-limit:{userKey}"],
                cancellationToken
            );

        // A run records its starter, the instance's owner, so it is counted even after a draft lifetime deleted the
        // instance. A run written before the starter was recorded is joined to the user's instances, as it was.
        var instances = context.SnapshotDrafts.Where(x =>
            x.OwnerKind == SnapshotOwnerKind.User && x.UserKey == userKey
        );
        var queued = await context.WorkQueues.CountAsync(
            w =>
                w.InvokingOwnerKind == SnapshotOwnerKind.User
                && w.Status == WorkQueueStatus.Queued
                && w.ExternalId != leavingToken
                && (
                    w.StarterHash == starter
                    || (
                        w.StarterHash == null
                        && instances.Any(x =>
                            x.Id == w.InvokingInstanceId && x.Machine == w.InvokingMachine
                        )
                    )
                ),
            cancellationToken
        );
        // Two counts, so the one by starter reads ix_metadata_live_invoked_starter (083): an OR with the legacy branch
        // does not imply that index's predicate, and the planner would fall back to the older ones.
        var running =
            await LiveRunsStartedBy(context, starter).CountAsync(cancellationToken)
            + await context.Metadatas.CountAsync(
                m =>
                    m.StarterHash == null
                    && m.InvokingOwnerKind == SnapshotOwnerKind.User
                    && (m.TrainState == TrainState.Pending || m.TrainState == TrainState.InProgress)
                    && instances.Any(x =>
                        x.Id == m.InvokingInstanceId && x.Machine == m.InvokingMachine
                    ),
                cancellationToken
            );

        var live = queued + running;
        return live >= limit
            ? new InvokeWrite.Refused(
                LimitReached,
                $"You already have {live} steps running, the most allowed at once. "
                    + "Wait for one to finish, then try again."
            )
            : null;
    }

    // The runs a user's instances invoked that have not ended, by the starter they recorded. Shaped to match the
    // predicate of ix_metadata_live_invoked_starter.
    internal static IQueryable<Trax.Effect.Models.Metadata.Metadata> LiveRunsStartedBy(
        IDataContext context,
        string starter
    ) =>
        context.Metadatas.Where(m =>
            m.StarterHash == starter
            && m.InvokingOwnerKind == SnapshotOwnerKind.User
            && (m.TrainState == TrainState.Pending || m.TrainState == TrainState.InProgress)
        );

    // The store a write goes through: on the write's own context, recording the IR hash of the definition that made
    // it and what it does to the row's timer, as the scoped store does.
    internal EfSnapshotStore StoreOver(IDataContext context, ISqlDialect? dialect) =>
        new(context, dialect)
        {
            IrHashes = services.GetService<MachineIrHashes>() is { } hashes ? hashes.Of : null,
            Timers = services.GetService<MachineTimerLookup>() is { } timers ? timers.Of : null,
            Writes = services.GetService<SnapshotWriteSignal>(),
        };

    /// <summary>
    /// Fires a due timer inside the sweep's transaction, on its context: writes <paramref name="next"/> over the row
    /// only while it is still <paramref name="due"/>'s row, state and revision (so a client event, an outcome or
    /// another host that wrote it first wins), cancels the run of the invoking state it leaves and queues the run of
    /// the one it enters, which only a system-owned instance may. The leaving path is the advance's: the token is
    /// cleared in the same statement, and the run's cancel commits with it.
    /// </summary>
    internal async Task<InvokeWrite> FireTimer(
        IDataContext context,
        EfSnapshotStore store,
        DueRow due,
        Snapshot next,
        bool leaving,
        EnteringInvoke? entering,
        CancellationToken cancellationToken,
        string? trigger = null
    )
    {
        var owner = new DraftOwner(due.OwnerKind, due.UserKey);
        if (entering is not null && owner.Kind != SnapshotOwnerKind.System)
            return new InvokeWrite.Refused(
                ChainRefused,
                "The step's work could not be started.",
                new InvalidOperationException(
                    $"The timer of '{next.Machine}' entered {next.State}, which invokes a train, but the instance is "
                        + "user-owned: a run a timer queues has no user to authorize it. The startup check refuses "
                        + "such a machine."
                )
            );

        var launch = entering is null
            ? null
            : Prepare(entering, owner, next, due.Id, fromOutcome: true);
        if (launch is InvokeWrite.Refused bad)
            return bad;
        var run = launch as Prepared;

        if (
            !await store.FireTimer(
                due,
                next,
                leaving || run is not null ? new InvokeTokenWrite(run?.Token) : null,
                cancellationToken,
                trigger
            )
        )
            return new InvokeWrite.Conflict();

        if (leaving && due.InvokeToken is { } leavingToken)
            await InvokedRunCancellation.CancelIn(context, leavingToken, cancellationToken);

        if (run is not null)
            await LaunchIn(run, context, store, owner, cancellationToken);

        return new InvokeWrite.Written(run?.Token);
    }

    // A child starts in its own initial state, with the context the spawn builds from the parent's (or its declared
    // one), as a record the server made. Its id goes into the parent's context, in the snapshot about to be written,
    // so the client that moved the parent reads where its child is; the token they share is minted here.
    private object PrepareSpawn(SpawnDefinition spawn, Snapshot snapshot)
    {
        var child = services
            .GetServices<IMachine>()
            .OfType<IMachineInternals>()
            .FirstOrDefault(m => m.GetType() == spawn.ChildMachine);
        if (child is null)
            return new InvokeWrite.Refused(
                RefusalCode.InternalError,
                "The step's work could not be started.",
                new InvalidOperationException(
                    $"'{snapshot.Machine}' state {snapshot.State} spawns {spawn.ChildMachine.Name}, which this host "
                        + "does not register as a machine. The startup check refuses such a host."
                )
            );

        Snapshot start;
        try
        {
            var context = spawn.CreateChildContext?.Invoke(
                (JsonObject)snapshot.Context.DeepClone()
            );
            start = child.InitialSnapshot(context) with { ServerRecord = true };
        }
        catch (Exception ex)
        {
            return new InvokeWrite.Refused(
                RefusalCode.InternalError,
                "The step's work could not be started.",
                ex
            );
        }

        var childId = Guid.NewGuid();
        snapshot.Context[spawn.ChildIdField] = childId.ToString();
        return new PreparedSpawn(Guid.NewGuid().ToString("N"), childId, start);
    }

    // Starts what Prepare prepared, inside the write's transaction: queues the run, or inserts the child for the same
    // owner carrying the token the parent now holds.
    private async Task LaunchIn(
        Prepared prepared,
        IDataContext context,
        IMachineInstanceStore store,
        DraftOwner owner,
        CancellationToken cancellationToken
    )
    {
        switch (prepared)
        {
            case PreparedLaunch launch:
                await Launcher().Launch(launch.Launch, context, cancellationToken);
                break;
            case PreparedSpawn spawn:
                if (
                    !await ((EfSnapshotStore)store).InsertSpawned(
                        owner,
                        spawn.ChildId,
                        spawn.Child,
                        spawn.Token,
                        cancellationToken
                    )
                )
                    throw new InvalidOperationException(
                        $"The child {spawn.ChildId} of '{spawn.Child.Machine}' could not be inserted: its owner already "
                            + "holds a row with that id."
                    );
                break;
        }
    }

    private IInvokedTrainLauncher Launcher() =>
        services.GetService<IInvokedTrainLauncher>()
        ?? throw new InvalidOperationException(
            "A state that invokes a train was entered, but no IInvokedTrainLauncher is registered. Call "
                + "AddMediator(...) after AddStateMachines(...); the startup check refuses such a host."
        );

    // Runs the write in a transaction of its own, on a data context created for it. A refusal or conflict rolls back
    // whatever was written before it; an exception rolls back and propagates, except that a refused authorization
    // is a refusal. The context goes with the write, so nothing it tracked outlives it.
    private async Task<InvokeWrite> InTransaction(
        Func<IDataContext, IMachineInstanceStore, Task<InvokeWrite>> write,
        CancellationToken cancellationToken
    )
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var store = StoreOver(context, services.GetService<ISqlDialect>());
        IDataContextTransaction? transaction = await context.BeginTransaction(cancellationToken);
        try
        {
            InvokeWrite result;
            try
            {
                result = await write(context, store);
            }
            catch (UnauthorizedAccessException)
            {
                // A refusal, not a failure: the caller is told, and there is nothing for the server's log.
                result = new InvokeWrite.Refused(
                    Forbidden,
                    "You may not start the work this step runs."
                );
            }

            if (result is InvokeWrite.Written)
            {
                await transaction.Commit();
                transaction.Dispose();
                services.GetService<SnapshotWriteSignal>()?.Written();
            }
            else
                await RollbackQuietly(transaction);
            transaction = null;
            return result;
        }
        catch
        {
            if (transaction is not null)
                await RollbackQuietly(transaction);
            throw;
        }
    }

    private static async Task RollbackQuietly(IDataContextTransaction transaction)
    {
        try
        {
            await transaction.Rollback();
        }
        catch (Exception)
        {
            // The failure that led here is the one the caller needs; a rollback that fails usually means the
            // connection went with it, and the database rolls the transaction back itself.
        }
        finally
        {
            transaction.Dispose();
        }
    }
}
