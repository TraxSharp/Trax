using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.DataContextTransaction;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
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
            async (context, store) =>
            {
                if (
                    entering is not null
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
            async (context, store) =>
            {
                if (
                    entering is not null
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
                    entering is not null
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

    // A user may hold at most Limit live invoked runs, counted across every machine: runs an instance they own
    // queued that have not ended, whether still queued or dispatched and executing. A dispatched run whose state was
    // left is only flagged for cancel and runs on to its next junction, so it counts until it ends; a queued run
    // whose state is left now (leavingToken) is cancelled in this transaction, so it does not. The count is taken
    // under a transaction-scoped lock on the user on Postgres, so two concurrent entries by one user, on any hosts
    // and in any machines, cannot both pass it; SQLite serializes writing transactions already (BEGIN IMMEDIATE).
    private async Task<InvokeWrite.Refused?> OverLimit(
        IDataContext context,
        DraftOwner owner,
        string? leavingToken,
        int limit,
        CancellationToken cancellationToken
    )
    {
        if (owner.Kind != SnapshotOwnerKind.User || owner.UserKey is not { } userKey)
            return null;

        if (services.GetService<ISqlDialect>() is { } dialect && context is DbContext db)
            await db.Database.ExecuteSqlRawAsync(
                dialect.LockSubject(),
                [$"trax:invoke-limit:{userKey}"],
                cancellationToken
            );

        // A run names the instance that queued it, not the user, so it is joined to the user's instances.
        var instances = context.SnapshotDrafts.Where(x =>
            x.OwnerKind == SnapshotOwnerKind.User && x.UserKey == userKey
        );
        var queued = await context.WorkQueues.CountAsync(
            w =>
                w.InvokingOwnerKind == SnapshotOwnerKind.User
                && w.Status == WorkQueueStatus.Queued
                && w.ExternalId != leavingToken
                && instances.Any(x =>
                    x.Id == w.InvokingInstanceId && x.Machine == w.InvokingMachine
                ),
            cancellationToken
        );
        var running = await context.Metadatas.CountAsync(
            m =>
                m.InvokingOwnerKind == SnapshotOwnerKind.User
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
        var store = new EfSnapshotStore(context, services.GetService<ISqlDialect>());
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
