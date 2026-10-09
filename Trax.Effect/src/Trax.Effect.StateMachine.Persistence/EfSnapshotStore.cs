using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine;
using SnapshotDraft = Trax.Effect.Models.SnapshotDraft.SnapshotDraft;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The <see cref="ISnapshotStore"/> over <see cref="IDataContext.SnapshotDrafts"/>: user-scoped reads and
/// writes of a <see cref="SnapshotDraft"/> with the context in a real <c>jsonb</c> column (<c>TEXT</c> on
/// SQLite) and optimistic concurrency via its concurrency token. Only EXPECTED races (an optimistic conflict,
/// or a concurrent create the <paramref name="dialect"/> recognises as a unique violation) are returned as
/// <c>false</c>; any other database error propagates, so it cannot masquerade as a benign conflict.
///
/// <para>Every <see cref="ISnapshotStore"/> member filters on <c>owner_kind = user</c> as well as the user's key,
/// so no user path reads, writes, lists or expires a system-owned instance, even one under the same id. System
/// rows and invoke tokens are reached only through the store's internal, owner-aware members.</para>
/// </summary>
/// <param name="db">The data context the table is reached through.</param>
/// <param name="dialect">
/// Recognises a unique violation on the configured provider. Without one, a concurrent create of the same draft
/// throws instead of losing the race.
/// </param>
public sealed class EfSnapshotStore(IDataContext db, ISqlDialect? dialect = null)
    : ISnapshotStore,
        IMachineInstanceStore
{
    // The rows one owner holds. Every owner-scoped query of the table starts here, so none of them can forget the
    // owner kind. A user's key never matches a system row (whose key is null) anyway, but the kind is named so the
    // rule does not rest on that.
    private IQueryable<SnapshotDraft> Owned(DraftOwner owner)
    {
        if (owner.Kind == SnapshotOwnerKind.System)
            return db.SnapshotDrafts.Where(x =>
                x.OwnerKind == SnapshotOwnerKind.System && x.UserKey == null
            );

        var userKey =
            owner.UserKey
            ?? throw new ArgumentException("A user's draft needs the user's key.", nameof(owner));
        return db.SnapshotDrafts.Where(x =>
            x.OwnerKind == SnapshotOwnerKind.User && x.UserKey == userKey
        );
    }

    /// <inheritdoc/>
    public Task<StoredSnapshot?> Get(
        string userKey,
        Guid id,
        CancellationToken cancellationToken = default
    ) => Read(Owned(DraftOwner.User(userKey)).Where(x => x.Id == id), cancellationToken);

    /// <inheritdoc/>
    public Task<StoredSnapshot?> Get(
        string userKey,
        string machine,
        Guid id,
        CancellationToken cancellationToken = default
    ) => GetOwned(DraftOwner.User(userKey), machine, id, cancellationToken);

    Task<StoredSnapshot?> IMachineInstanceStore.Get(
        DraftOwner owner,
        string machine,
        Guid id,
        CancellationToken cancellationToken
    ) => GetOwned(owner, machine, id, cancellationToken);

    private Task<StoredSnapshot?> GetOwned(
        DraftOwner owner,
        string machine,
        Guid id,
        CancellationToken cancellationToken
    ) => Read(Owned(owner).Where(x => x.Id == id && x.Machine == machine), cancellationToken);

    private static async Task<StoredSnapshot?> Read(
        IQueryable<SnapshotDraft> query,
        CancellationToken cancellationToken
    )
    {
        var record = await query.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return record is null ? null : ToStored(record);
    }

    private static StoredSnapshot ToStored(SnapshotDraft record)
    {
        var snapshot = new JsonObject
        {
            ["machine"] = record.Machine,
            ["version"] = record.Version,
            ["state"] = record.State,
            ["context"] = JsonNode.Parse(record.Context),
        };
        return new StoredSnapshot(
            snapshot.ToJsonString(),
            record.ConcurrencyToken,
            record.LastRequestId,
            record.UpdatedAt
        )
        {
            LastRequestTrigger = record.LastRequestTrigger,
            LastRequestFromState = record.LastRequestFromState,
            InvokeToken = record.InvokeToken,
        };
    }

    /// <inheritdoc/>
    public Task Delete(string userKey, Guid id, CancellationToken cancellationToken = default) =>
        Owned(DraftOwner.User(userKey))
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

    /// <inheritdoc/>
    public Task Delete(
        string userKey,
        string machine,
        Guid id,
        CancellationToken cancellationToken = default
    ) =>
        Owned(DraftOwner.User(userKey))
            .Where(x => x.Id == id && x.Machine == machine)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Inserts the draft of <paramref name="snapshot"/>'s machine, or overwrites its version, state and context,
    /// with a fresh concurrency token and <c>updated_at</c>. It leaves the last-request columns and the invoke
    /// token untouched, and never touches another machine's draft, or a system instance, under the same id.
    /// Returns <c>false</c> when the row changed between this call's read and its write, or a concurrent insert
    /// of the same <c>(user_key, machine, id)</c> won; any other database error propagates.
    /// </summary>
    /// <param name="userKey">The owning user's key.</param>
    /// <param name="id">The client-minted draft id.</param>
    /// <param name="snapshot">The snapshot to store.</param>
    /// <param name="cancellationToken">Cancels the database calls.</param>
    public async Task<bool> Upsert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken = default
    )
    {
        var machine = snapshot.Machine;
        var record = await Owned(DraftOwner.User(userKey))
            .FirstOrDefaultAsync(x => x.Id == id && x.Machine == machine, cancellationToken);
        if (record is null)
            return await Insert(userKey, id, snapshot, cancellationToken);

        Apply(record, snapshot);
        return await Save(record, cancellationToken);
    }

    /// <summary>
    /// Inserts a new draft row in one statement. Returns <c>false</c> when a row with the same
    /// <c>(user_key, machine, id)</c> already exists, which is how a writer that lost the race to create a draft
    /// finds out; any other database error propagates. A system instance under the same id is not a conflict:
    /// the user's draft is a row of its own.
    /// </summary>
    /// <param name="userKey">The owning user's key.</param>
    /// <param name="id">The client-minted draft id.</param>
    /// <param name="snapshot">The snapshot to store.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    public Task<bool> Insert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken = default
    ) => InsertOwned(DraftOwner.User(userKey), id, snapshot, cancellationToken);

    Task<bool> IMachineInstanceStore.Insert(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        string? invokeToken,
        CancellationToken cancellationToken
    ) => InsertOwned(owner, id, snapshot, cancellationToken, invokeToken);

    async Task<bool> IMachineInstanceStore.DeleteHolding(
        DraftOwner owner,
        string machine,
        Guid id,
        string? invokeToken,
        CancellationToken cancellationToken
    ) =>
        await Owned(owner)
            .Where(x => x.Id == id && x.Machine == machine && x.InvokeToken == invokeToken)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    private async Task<bool> InsertOwned(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken,
        string? invokeToken = null
    )
    {
        // The owner's unique index refuses a second row, and the dialect reads that as a lost race. A provider
        // with no dialect (InMemory) enforces no unique index, so there the row is looked for first; the look and
        // the insert are two steps, which is all InMemory offers.
        var machine = snapshot.Machine;
        if (
            dialect is null
            && await Owned(owner)
                .AnyAsync(x => x.Id == id && x.Machine == machine, cancellationToken)
        )
            return false;

        var record = new SnapshotDraft
        {
            Id = id,
            OwnerKind = owner.Kind,
            UserKey = owner.UserKey,
            InvokeToken = invokeToken,
        };
        Apply(record, snapshot);
        record.CreatedAt = record.UpdatedAt;
        db.SnapshotDrafts.Add(record);
        return await Save(record, cancellationToken);
    }

    private static void Apply(SnapshotDraft record, Snapshot snapshot)
    {
        // A stranded instance that moves to another state is no longer stranded.
        if (record.State != snapshot.State)
            record.InvokeStrandedState = null;
        record.Machine = snapshot.Machine;
        record.Version = snapshot.Version;
        record.State = snapshot.State;
        record.Context = snapshot.Context.ToJsonString();
        record.ConcurrencyToken = Guid.NewGuid();
        record.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<bool> Save(SnapshotDraft record, CancellationToken cancellationToken)
    {
        try
        {
            await ((DbContext)db).SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A stale optimistic write: someone else changed the row since it was read.
            return false;
        }
        catch (DbUpdateException ex) when (dialect?.IsUniqueViolation(ex) == true)
        {
            // A concurrent create of the same row for one owner: the other writer got there first. Any other
            // DbUpdateException (a NOT NULL or check-constraint violation from a real bug) is not swallowed.
            return false;
        }
        finally
        {
            // Stop tracking the row whether or not it was written: the context may be shared with the rest of
            // the request, a failed write left tracked would be retried by its next save, and a written one
            // would be served stale from the identity map to this store's next read.
            ((DbContext)db)
                .Entry(record)
                .State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Conditional update that records <paramref name="requestId"/> alone and clears the stored trigger and
    /// from-state, so a retry of that request is refused as a reused id rather than replayed. The draft service
    /// writes through <see cref="UpdateWithRequest"/> instead. Returns <c>false</c> when the row no longer carries
    /// <paramref name="expectedToken"/> or does not exist.
    /// </summary>
    /// <param name="userKey">The owning user's key.</param>
    /// <param name="id">The client-minted draft id.</param>
    /// <param name="snapshot">The snapshot to store.</param>
    /// <param name="expectedToken">The concurrency token read with the draft.</param>
    /// <param name="requestId">The idempotency key to record, or null to clear it.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    public Task<bool> Update(
        string userKey,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        string? requestId = null,
        CancellationToken cancellationToken = default
    ) =>
        UpdateWithRequest(
            userKey,
            id,
            snapshot,
            expectedToken,
            requestId is null ? null : new AppliedRequest(requestId, null, null),
            cancellationToken
        );

    /// <summary>
    /// One atomic <c>UPDATE ... WHERE concurrency_token = expectedToken</c> on the user's draft of
    /// <paramref name="snapshot"/>'s machine that bypasses the change tracker. It writes the snapshot, the
    /// request's id, trigger and from-state (all null when <paramref name="request"/> is
    /// null), a fresh token and <c>updated_at</c>. A write that lost the race, or targets a missing row, updates
    /// nothing and returns <c>false</c> rather than throwing.
    /// </summary>
    /// <param name="userKey">The owning user's key.</param>
    /// <param name="id">The client-minted draft id.</param>
    /// <param name="snapshot">The snapshot to store.</param>
    /// <param name="expectedToken">The concurrency token read with the draft.</param>
    /// <param name="request">The applied request to record, or null to clear the last-request columns.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    public Task<bool> UpdateWithRequest(
        string userKey,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        AppliedRequest? request,
        CancellationToken cancellationToken = default
    ) =>
        UpdateOwned(
            DraftOwner.User(userKey),
            id,
            snapshot,
            expectedToken,
            request,
            invokeToken: null,
            cancellationToken
        );

    Task<bool> IMachineInstanceStore.Update(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        AppliedRequest? request,
        InvokeTokenWrite? invokeToken,
        CancellationToken cancellationToken
    ) => UpdateOwned(owner, id, snapshot, expectedToken, request, invokeToken, cancellationToken);

    private Task<bool> UpdateOwned(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        AppliedRequest? request,
        InvokeTokenWrite? invokeToken,
        CancellationToken cancellationToken
    )
    {
        // Optimistic update as a single atomic statement that bypasses the change tracker. The token guard is
        // in the WHERE, so a write that lost the race updates 0 rows — no lost update, no exception.
        var machineId = snapshot.Machine;
        var version = snapshot.Version;
        var state = snapshot.State;
        var contextJson = snapshot.Context.ToJsonString();
        var newToken = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var requestId = request?.RequestId;
        var requestTrigger = request?.Trigger;
        var requestFromState = request?.FromState;

        return Write(
            Owned(owner)
                .Where(x =>
                    x.Id == id && x.Machine == machineId && x.ConcurrencyToken == expectedToken
                ),
            setters =>
            {
                setters
                    .SetProperty(x => x.Version, version)
                    .SetProperty(x => x.State, state)
                    .SetProperty(x => x.Context, contextJson)
                    .SetProperty(x => x.ConcurrencyToken, newToken)
                    .SetProperty(x => x.LastRequestId, requestId)
                    .SetProperty(x => x.LastRequestTrigger, requestTrigger)
                    .SetProperty(x => x.LastRequestFromState, requestFromState)
                    .SetProperty(x => x.UpdatedAt, now);
                if (invokeToken is { Value: var next })
                    setters.SetProperty(x => x.InvokeToken, next);

                // A stranded instance that is given a run, or moves to another state, is no longer stranded.
                if (invokeToken is { Value: not null })
                    setters.SetProperty(x => x.InvokeStrandedState, (string?)null);
                else
                    setters.SetProperty(
                        x => x.InvokeStrandedState,
                        x => x.State == state ? x.InvokeStrandedState : null
                    );
            },
            cancellationToken
        );
    }

    Task<bool> IMachineInstanceStore.SetInvokeToken(
        DraftOwner owner,
        string machine,
        Guid id,
        Guid expectedToken,
        InvokeTokenWrite invokeToken,
        CancellationToken cancellationToken
    )
    {
        var next = invokeToken.Value;
        var newToken = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        return Write(
            Owned(owner)
                .Where(x =>
                    x.Id == id && x.Machine == machine && x.ConcurrencyToken == expectedToken
                ),
            setters =>
            {
                setters
                    .SetProperty(x => x.InvokeToken, next)
                    .SetProperty(x => x.ConcurrencyToken, newToken)
                    .SetProperty(x => x.UpdatedAt, now);
                if (next is not null)
                    setters.SetProperty(x => x.InvokeStrandedState, (string?)null);
            },
            cancellationToken
        );
    }

    async Task<StoredInstance?> IMachineInstanceStore.GetByInvokeToken(
        string invokeToken,
        CancellationToken cancellationToken
    )
    {
        var record = await db
            .SnapshotDrafts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.InvokeToken == invokeToken, cancellationToken);
        return record is null
            ? null
            : new StoredInstance(
                new DraftOwner(record.OwnerKind, record.UserKey),
                record.Machine,
                record.Id,
                ToStored(record)
            );
    }

    Task<bool> IMachineInstanceStore.ApplyByInvokeToken(
        string invokeToken,
        Snapshot snapshot,
        string? nextInvokeToken,
        Guid? expectedToken,
        CancellationToken cancellationToken
    )
    {
        var machineId = snapshot.Machine;
        var version = snapshot.Version;
        var state = snapshot.State;
        var contextJson = snapshot.Context.ToJsonString();
        var newToken = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var target = db.SnapshotDrafts.Where(x =>
            x.InvokeToken == invokeToken && x.Machine == machineId
        );
        if (expectedToken is { } expected)
            target = target.Where(x => x.ConcurrencyToken == expected);

        return Write(
            target,
            setters =>
                setters
                    .SetProperty(x => x.Version, version)
                    .SetProperty(x => x.State, state)
                    .SetProperty(x => x.Context, contextJson)
                    .SetProperty(x => x.InvokeToken, nextInvokeToken)
                    .SetProperty(x => x.InvokeStrandedState, (string?)null)
                    .SetProperty(x => x.ConcurrencyToken, newToken)
                    .SetProperty(x => x.UpdatedAt, now),
            cancellationToken
        );
    }

    Task<bool> IMachineInstanceStore.StrandByInvokeToken(
        string invokeToken,
        Snapshot snapshot,
        Guid expectedToken,
        CancellationToken cancellationToken
    )
    {
        var machineId = snapshot.Machine;
        var version = snapshot.Version;
        var state = snapshot.State;
        var contextJson = snapshot.Context.ToJsonString();
        var newToken = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        return Write(
            db.SnapshotDrafts.Where(x =>
                x.InvokeToken == invokeToken
                && x.Machine == machineId
                && x.ConcurrencyToken == expectedToken
            ),
            setters =>
                setters
                    .SetProperty(x => x.Version, version)
                    .SetProperty(x => x.State, state)
                    .SetProperty(x => x.Context, contextJson)
                    .SetProperty(x => x.InvokeToken, (string?)null)
                    .SetProperty(x => x.InvokeStrandedState, state)
                    .SetProperty(x => x.ConcurrencyToken, newToken)
                    .SetProperty(x => x.UpdatedAt, now),
            cancellationToken
        );
    }

    // One UPDATE that matches at most one row. A write that would give a second row the same invoke token is a
    // unique violation, which the dialect reads as a lost race like any other conflict; anything else propagates.
    // ExecuteUpdate throws the provider's exception unwrapped, so it is wrapped the way SaveChanges would wrap it
    // before the dialect reads it. On Postgres the failed statement still aborts an enclosing transaction (there
    // is no savepoint around it): a caller writing tokens inside its own transaction treats false as fatal to it.
    private async Task<bool> Write(
        IQueryable<SnapshotDraft> target,
        Action<UpdateSettersBuilder<SnapshotDraft>> setters,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await target.ExecuteUpdateAsync(setters, cancellationToken) == 1;
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            return false;
        }
    }

    private bool IsUniqueViolation(Exception exception) =>
        dialect is not null
        && dialect.IsUniqueViolation(
            exception as DbUpdateException ?? new DbUpdateException(exception.Message, exception)
        );
}
