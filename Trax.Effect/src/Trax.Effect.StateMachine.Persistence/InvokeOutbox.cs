using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.DataContextTransaction;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>The invoking state a write enters: its train, how the run's input is built, and the owner's cap.</summary>
/// <param name="TrainType">The train's interface.</param>
/// <param name="CreateInput">Builds the run's input from the context the state is entered with.</param>
/// <param name="Limit">The most live invoked runs one user may hold in the machine; system owners are not capped.</param>
internal sealed record EnteringInvoke(
    Type TrainType,
    Func<JsonObject, object?> CreateInput,
    int Limit
);

/// <summary>How a write through the <see cref="InvokeOutbox"/> ended.</summary>
internal abstract record InvokeWrite
{
    /// <summary>The snapshot, the token change and any queued run committed together.</summary>
    /// <param name="InvokeToken">The token the row holds now: the new run's external id, or null.</param>
    public sealed record Written(string? InvokeToken) : InvokeWrite;

    /// <summary>The row changed since it was read, or (on insert) already exists; nothing was written.</summary>
    public sealed record Conflict : InvokeWrite;

    /// <summary>The write was refused; nothing was written.</summary>
    public sealed record Refused(string Code, string Message, Exception? Exception = null)
        : InvokeWrite;

    private InvokeWrite() { }
}

/// <summary>
/// Writes a snapshot that enters or leaves an invoking state in one transaction with everything that goes with it:
/// leaving cancels the old run and clears the row's invoke token; entering queues the new run's work queue entry
/// through the launcher, on the same data context, and sets the token to the entry's external id. A crash or a
/// refusal at any point leaves none of it, so no machine waits on a run that was never queued and no run is queued
/// for a machine that never moved.
/// </summary>
/// <remarks>
/// <para>The transaction belongs to the request's data context, never to the advance train's own: the train saves
/// its metadata through the context its effect runner creates, and junction progress writes through one of its
/// own (Effect ADR 0021), so neither can commit or roll back this one part-way.</para>
/// <para>On Postgres a failed statement aborts the transaction, a unique violation on the invoke token included, so
/// every lost write is followed by a rollback and reported as a conflict; nothing is written after it.</para>
/// </remarks>
[Experimental(ExperimentalIds.Invokes)]
internal sealed class InvokeOutbox(
    IDataContext context,
    IMachineInstanceStore store,
    IServiceProvider services
)
{
    /// <summary>The code an entry past the owner's live-run cap is refused with.</summary>
    public const string LimitReached = "invoke-limit-reached";

    /// <summary>The code an entry is refused with when the entering user may not run the state's train.</summary>
    public const string Forbidden = "invoke-forbidden";

    /// <summary>
    /// The code an outcome is refused with when it enters an invoking state of a user-owned instance: only a
    /// system-owned instance chains runs through outcomes.
    /// </summary>
    public const string ChainRefused = "invoke-chain-user-owned";

    /// <summary>
    /// Writes <paramref name="next"/> over <paramref name="owner"/>'s row, guarded by <paramref name="expectedToken"/>,
    /// cancelling <paramref name="leavingToken"/>'s run and queueing <paramref name="entering"/>'s.
    /// </summary>
    public async Task<InvokeWrite> Advance(
        DraftOwner owner,
        Guid id,
        Snapshot next,
        Guid expectedToken,
        AppliedRequest? request,
        string? leavingToken,
        EnteringInvoke? entering,
        CancellationToken cancellationToken
    ) =>
        await InTransaction(
            async () =>
            {
                if (
                    entering is not null
                    && await OverLimit(owner, next.Machine, id, entering.Limit, cancellationToken)
                        is { } refused
                )
                    return refused;

                // Built before anything is written: the input mapping is the author's code and may throw.
                var launch = entering is null ? null : Prepare(entering, owner, next, id);
                if (launch is InvokeWrite.Refused bad)
                    return bad;
                var run = launch as PreparedLaunch;

                // The snapshot first: it is the one write that can lose a race, and losing it writes nothing else.
                var wrote = await store.Update(
                    owner,
                    id,
                    next,
                    expectedToken,
                    request,
                    new InvokeTokenWrite(run?.Launch.ExternalId),
                    cancellationToken
                );
                if (!wrote)
                    return new InvokeWrite.Conflict();

                if (leavingToken is not null)
                    await InvokedRunCancellation.CancelIn(context, leavingToken, cancellationToken);

                if (run is not null)
                    await Launcher().Launch(run.Launch, context, cancellationToken);

                return new InvokeWrite.Written(run?.Launch.ExternalId);
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
            async () =>
            {
                if (
                    entering is not null
                    && await OverLimit(
                        owner,
                        snapshot.Machine,
                        id,
                        entering.Limit,
                        cancellationToken
                    )
                        is { } refused
                )
                    return refused;

                var launch = entering is null ? null : Prepare(entering, owner, snapshot, id);
                if (launch is InvokeWrite.Refused bad)
                    return bad;
                var run = launch as PreparedLaunch;

                if (
                    !await store.Insert(
                        owner,
                        id,
                        snapshot,
                        run?.Launch.ExternalId,
                        cancellationToken
                    )
                )
                    return new InvokeWrite.Conflict();

                if (run is not null)
                    await Launcher().Launch(run.Launch, context, cancellationToken);

                return new InvokeWrite.Written(run?.Launch.ExternalId);
            },
            cancellationToken
        );

    /// <summary>
    /// Applies a finished run's outcome: writes <paramref name="next"/> over the row that still holds
    /// <paramref name="invokeToken"/> and <paramref name="expectedToken"/> (<c>UPDATE ... WHERE invoke_token =</c>),
    /// replacing the token with the run <paramref name="entering"/> queues, or clearing it. The finished run needs
    /// no cancel. <see cref="InvokeWrite.Conflict"/> when the row no longer holds both tokens: the outcome was
    /// applied already, the state was left, or the row changed since it was read.
    /// </summary>
    public async Task<InvokeWrite> Deliver(
        DraftOwner owner,
        Guid id,
        string invokeToken,
        Snapshot next,
        Guid expectedToken,
        EnteringInvoke? entering,
        CancellationToken cancellationToken
    ) =>
        await InTransaction(
            async () =>
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
                    entering is not null
                    && await OverLimit(owner, next.Machine, id, entering.Limit, cancellationToken)
                        is { } refused
                )
                    return refused;

                var launch = entering is null
                    ? null
                    : Prepare(entering, owner, next, id, fromOutcome: true);
                if (launch is InvokeWrite.Refused bad)
                    return bad;
                var run = launch as PreparedLaunch;

                // The conditional update is the one write that can lose, to another host applying the same
                // outcome or to the state being left; losing it writes nothing else.
                if (
                    !await store.ApplyByInvokeToken(
                        invokeToken,
                        next,
                        run?.Launch.ExternalId,
                        expectedToken,
                        cancellationToken
                    )
                )
                    return new InvokeWrite.Conflict();

                if (run is not null)
                    await Launcher().Launch(run.Launch, context, cancellationToken);

                return new InvokeWrite.Written(run?.Launch.ExternalId);
            },
            cancellationToken
        );

    private sealed record PreparedLaunch(InvokedTrainLaunch Launch);

    private static object Prepare(
        EnteringInvoke entering,
        DraftOwner owner,
        Snapshot snapshot,
        Guid id,
        bool fromOutcome = false
    )
    {
        object? input;
        try
        {
            input = entering.CreateInput(snapshot.Context);
        }
        catch (Exception ex)
        {
            return new InvokeWrite.Refused(
                RejectionReasons.InternalError,
                "The step's work could not be started.",
                ex
            );
        }

        if (input is null)
            return new InvokeWrite.Refused(
                RejectionReasons.InternalError,
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
                new InvokedBy(snapshot.Machine, id, owner.Kind)
            )
            {
                FromOutcome = fromOutcome,
            }
        );
    }

    // A user may hold at most Limit live runs in this machine: rows of theirs holding a token, other than the one
    // being written. The count is taken under a transaction-scoped lock on (machine, user) on Postgres, so two
    // concurrent entries cannot both pass it; SQLite serializes writing transactions already.
    private async Task<InvokeWrite.Refused?> OverLimit(
        DraftOwner owner,
        string machine,
        Guid id,
        int limit,
        CancellationToken cancellationToken
    )
    {
        if (owner.Kind != SnapshotOwnerKind.User || owner.UserKey is not { } userKey)
            return null;

        if (services.GetService<ISqlDialect>() is { } dialect && context is DbContext db)
            await db.Database.ExecuteSqlRawAsync(
                dialect.LockSubject(),
                [$"trax:invoke-limit:{machine}:{userKey}"],
                cancellationToken
            );

        var live = await context.SnapshotDrafts.CountAsync(
            x =>
                x.OwnerKind == SnapshotOwnerKind.User
                && x.UserKey == userKey
                && x.Machine == machine
                && x.InvokeToken != null
                && x.Id != id,
            cancellationToken
        );

        return live >= limit
            ? new InvokeWrite.Refused(
                LimitReached,
                $"You already have {live} steps running in this flow, the most allowed at once. "
                    + "Wait for one to finish, then try again."
            )
            : null;
    }

    private IInvokedTrainLauncher Launcher() =>
        services.GetService<IInvokedTrainLauncher>()
        ?? throw new InvalidOperationException(
            "A state that invokes a train was entered, but no IInvokedTrainLauncher is registered. Call "
                + "AddMediator(...) after AddStateMachines(...); the startup check refuses such a host."
        );

    // Runs the write in a transaction of its own on the request's data context. A refusal or conflict rolls back
    // whatever was written before it; an exception rolls back and propagates, except that a refused authorization
    // is a refusal. The run's entry is never left tracked: committed, it would be served stale; rolled back, a
    // later save on the shared context would insert it after all.
    private async Task<InvokeWrite> InTransaction(
        Func<Task<InvokeWrite>> write,
        CancellationToken cancellationToken
    )
    {
        var db = (DbContext)context;
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException(
                "A state that invokes a train is written in a transaction of its own, but the request's data "
                    + "context already holds one. Write the snapshot outside it."
            );

        var tracked = db.ChangeTracker.Entries<WorkQueue>().Select(e => e.Entity).ToHashSet();
        IDataContextTransaction? transaction = await context.BeginTransaction(cancellationToken);
        try
        {
            InvokeWrite result;
            try
            {
                result = await write();
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
        finally
        {
            foreach (var entry in db.ChangeTracker.Entries<WorkQueue>().ToList())
                if (!tracked.Contains(entry.Entity))
                    entry.State = EntityState.Detached;
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
