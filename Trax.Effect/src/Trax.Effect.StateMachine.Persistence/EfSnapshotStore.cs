using System.Globalization;
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
        IMachineInstanceStore,
        IRowReportingSnapshotStore,
        IConditionalDraftDelete
{
    /// <summary>
    /// The IR hash of a machine's current definition by its name, written to <c>ir_hash</c> with every snapshot.
    /// Null, or a machine it answers null for, writes none.
    /// </summary>
    internal Func<string, string?>? IrHashes { get; init; }

    private string? IrHashOf(string machine) => IrHashes?.Invoke(machine);

    /// <summary>
    /// What a write of a snapshot that does not say (<see cref="Snapshot.Timer"/> is null) does to the row's timer, by
    /// machine, state and context: the state's timer kept from entry, or none. Null, or a machine it answers null for,
    /// keeps the due time while the row stays in its state and clears it when the state changes.
    /// </summary>
    internal Func<string, string, JsonObject, SnapshotTimer?>? Timers { get; init; }

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
            Revision = record.Revision,
            RowId = record.RowId,
            DueAt = record.DueAt,
            RecentRequests = RequestIdRing.Parse(record.RecentRequests),
            Effect = record.EffectToken is { } effectToken
                ? new PendingEffect(
                    effectToken,
                    record.EffectKey,
                    record.EffectTrigger,
                    record.EffectRequestId,
                    record.EffectLeaseUntil
                )
                : null,
            ServerRecord = record.ServerRecord,
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

    /// <inheritdoc/>
    async Task<bool> IConditionalDraftDelete.DeleteAsRead(
        string userKey,
        string machine,
        Guid id,
        StoredSnapshot read,
        Func<Task> afterDelete
    )
    {
        if (read.RowId is not { } rowId || read.Revision is not { } revision)
            throw new ArgumentException(
                "A conditional delete needs the row id and revision the draft was read with.",
                nameof(read)
            );
        var token = read.InvokeToken;
        return await DeleteThen(
            Owned(DraftOwner.User(userKey))
                .Where(x =>
                    x.Id == id
                    && x.Machine == machine
                    && x.RowId == rowId
                    && x.Revision == revision
                    && x.InvokeToken == token
                    && x.EffectToken == null
                ),
            afterDelete
        );
    }

    // Deletes the row the query names and runs the follow-up writes in one transaction, joining the context's own
    // when it already has one. Nothing is written, and the follow-up is not run, when the query names no row. None of
    // it can be cancelled: a delete committed without its follow-up would leave claims no one reads again.
    private async Task<bool> DeleteThen(IQueryable<SnapshotDraft> target, Func<Task> afterDelete)
    {
        var ambient = ((DbContext)db).Database.CurrentTransaction is not null;
        using var transaction = ambient ? null : await db.BeginTransaction(CancellationToken.None);
        bool deleted;
        if (((DbContext)db).Database.IsRelational())
            deleted = await target.ExecuteDeleteAsync(CancellationToken.None) > 0;
        else
        {
            // The InMemory provider has no set-based delete: the row is read and removed.
            var record = await target.FirstOrDefaultAsync(CancellationToken.None);
            if (record is not null)
                db.SnapshotDrafts.Remove(record);
            deleted = record is not null && await Save(record, CancellationToken.None);
        }
        if (!deleted)
        {
            if (transaction is not null)
                await transaction.Rollback();
            return false;
        }
        await afterDelete();
        if (transaction is not null)
            await transaction.Commit();
        return true;
    }

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
    ) =>
        await ((IRowReportingSnapshotStore)this).Upsert(userKey, id, snapshot, cancellationToken)
            is not null;

    async Task<WrittenRow?> IRowReportingSnapshotStore.Upsert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        var machine = snapshot.Machine;
        var record = await Owned(DraftOwner.User(userKey))
            .FirstOrDefaultAsync(x => x.Id == id && x.Machine == machine, cancellationToken);
        if (record is null)
            return await InsertOwned(DraftOwner.User(userKey), id, snapshot, cancellationToken);

        // A row with a pending effect is written by that effect only.
        if (record.EffectToken is not null)
        {
            ((DbContext)db).Entry(record).State = EntityState.Detached;
            return null;
        }

        // The write is guarded by the concurrency token read with the row, so it lands on the revision read here,
        // and the trigger moves it to the same next value; on InMemory, which has no trigger, this is the bump.
        record.Revision += 1;
        var due = DueFor(snapshot);
        record.DueAt = due.Resolve(
            record.State,
            record.DueAt,
            snapshot.State,
            await DueValue(due, cancellationToken)
        );
        Apply(record, snapshot);
        return await Save(record, cancellationToken) ? Written(record) : null;
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
    public async Task<bool> Insert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken = default
    ) => await InsertOwned(DraftOwner.User(userKey), id, snapshot, cancellationToken) is not null;

    Task<WrittenRow?> IRowReportingSnapshotStore.Insert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken
    ) => InsertOwned(DraftOwner.User(userKey), id, snapshot, cancellationToken);

    async Task<bool> IMachineInstanceStore.Insert(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        string? invokeToken,
        CancellationToken cancellationToken
    ) => await InsertOwned(owner, id, snapshot, cancellationToken, invokeToken) is not null;

    async Task<bool> IMachineInstanceStore.DeleteHolding(
        DraftOwner owner,
        string machine,
        Guid id,
        string? invokeToken,
        CancellationToken cancellationToken
    ) =>
        await Owned(owner)
            .Where(x =>
                x.Id == id
                && x.Machine == machine
                && x.InvokeToken == invokeToken
                && x.EffectToken == null
            )
            .ExecuteDeleteAsync(cancellationToken) > 0;

    private async Task<WrittenRow?> InsertOwned(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken,
        string? invokeToken = null,
        StartRequest? start = null
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
            return null;

        // A created row enters its state, so a timed state's timer starts now.
        var due = DueFor(snapshot);
        var record = new SnapshotDraft
        {
            Id = id,
            OwnerKind = owner.Kind,
            UserKey = owner.UserKey,
            InvokeToken = invokeToken,
            StartRequestId = start?.RequestId,
            StartFingerprint = start?.Fingerprint,
            DueAt = due.Resolve(null, null, snapshot.State, await DueValue(due, cancellationToken)),
        };
        Apply(record, snapshot);
        record.CreatedAt = record.UpdatedAt;
        db.SnapshotDrafts.Add(record);
        // A new row starts at revision 1; its row id is the database's, read back by the insert.
        return await Save(record, cancellationToken) ? Written(record) : null;
    }

    private static WrittenRow Written(SnapshotDraft record) =>
        new(record.RowId, record.Revision) { DueAt = record.DueAt };

    private void Apply(SnapshotDraft record, Snapshot snapshot)
    {
        // A stranded instance that moves to another state is no longer stranded.
        if (record.State != snapshot.State)
            record.InvokeStrandedState = null;
        record.Machine = snapshot.Machine;
        record.Version = snapshot.Version;
        record.State = snapshot.State;
        record.Context = snapshot.Context.ToJsonString();
        record.IrHash = IrHashOf(snapshot.Machine);
        // A snapshot written through the store states who put the row in its state, or null when host code wrote it
        // and stated nothing, which leaves the row judged by its state.
        record.ServerRecord = snapshot.ServerRecord;
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
        Write(
            Updated(DraftOwner.User(userKey), id, snapshot, expectedToken),
            UpdateWrite(snapshot, request, invokeToken: null) with
            {
                ForgetsUnstatedServerRecord = true,
            },
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

    async Task<WrittenRow?> IRowReportingSnapshotStore.UpdateWithRequest(
        string userKey,
        Guid id,
        Snapshot snapshot,
        StoredSnapshot read,
        AppliedRequest? request,
        CancellationToken cancellationToken
    )
    {
        var write = UpdateWrite(snapshot, request, invokeToken: null);
        if (
            !await Write(
                Updated(DraftOwner.User(userKey), id, snapshot, read.Token),
                write,
                cancellationToken
            )
        )
            return null;

        // The write was guarded by the token read with the row, so it changed exactly the row read: its due time
        // follows from that row and what the write did to it, with no second read another write could overtake.
        var before = StateOf(read.Json);
        var due = write.Due is { } written
            ? written.Resolve(before, read.DueAt, snapshot.State, write.DueValue)
            : read.DueAt;
        return new WrittenRow(read.RowId ?? 0, (read.Revision ?? 0) + 1) { DueAt = due };
    }

    private static string? StateOf(string json)
    {
        try
        {
            return JsonNode.Parse(json)?["state"]?.GetValue<string>();
        }
        catch (Exception ex)
            when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private Task<bool> UpdateOwned(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        AppliedRequest? request,
        InvokeTokenWrite? invokeToken,
        CancellationToken cancellationToken
    ) =>
        Write(
            Updated(owner, id, snapshot, expectedToken),
            UpdateWrite(snapshot, request, invokeToken),
            cancellationToken
        );

    // Optimistic update as a single atomic statement that bypasses the change tracker. The token guard is in the
    // WHERE, so a write that lost the race updates 0 rows — no lost update, no exception.
    private IQueryable<SnapshotDraft> Updated(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken
    )
    {
        var machineId = snapshot.Machine;
        return Owned(owner)
            .Where(x =>
                x.Id == id && x.Machine == machineId && x.ConcurrencyToken == expectedToken
            );
    }

    private RowWrite UpdateWrite(
        Snapshot snapshot,
        AppliedRequest? request,
        InvokeTokenWrite? invokeToken
    ) =>
        new RowWrite
        {
            Snapshot = snapshot,
            IrHash = IrHashOf(snapshot.Machine),
            Due = DueFor(snapshot),
            WritesRequest = true,
            Request = request,
            RingJson = request?.Ring is { } ring ? RequestIdRing.Serialize(ring) : null,
            InvokeToken = invokeToken,
            // A stranded instance that is given a run, or moves to another state, is no longer stranded.
            Stranded = invokeToken is { Value: not null }
                ? StrandedWrite.Clear
                : StrandedWrite.ClearUnlessSameState,
        };

    Task<bool> IMachineInstanceStore.SetInvokeToken(
        DraftOwner owner,
        string machine,
        Guid id,
        Guid expectedToken,
        InvokeTokenWrite invokeToken,
        CancellationToken cancellationToken
    ) =>
        Write(
            Owned(owner)
                .Where(x =>
                    x.Id == id && x.Machine == machine && x.ConcurrencyToken == expectedToken
                ),
            new RowWrite
            {
                InvokeToken = invokeToken,
                Stranded = invokeToken.Value is not null ? StrandedWrite.Clear : StrandedWrite.Keep,
            },
            cancellationToken
        );

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
        var target = db.SnapshotDrafts.Where(x =>
            x.InvokeToken == invokeToken && x.Machine == machineId
        );
        if (expectedToken is { } expected)
            target = target.Where(x => x.ConcurrencyToken == expected);

        return Write(
            target,
            new RowWrite
            {
                Snapshot = snapshot,
                IrHash = IrHashOf(snapshot.Machine),
                Due = DueFor(snapshot),
                InvokeToken = new InvokeTokenWrite(nextInvokeToken),
                Stranded = StrandedWrite.Clear,
                // An invoked train's outcome is the server's write.
                ServerRecord = true,
            },
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
        return Write(
            db.SnapshotDrafts.Where(x =>
                x.InvokeToken == invokeToken
                && x.Machine == machineId
                && x.ConcurrencyToken == expectedToken
            ),
            new RowWrite
            {
                Snapshot = snapshot,
                IrHash = IrHashOf(snapshot.Machine),
                Due = DueFor(snapshot),
                InvokeToken = new InvokeTokenWrite(null),
                Stranded = StrandedWrite.Mark,
            },
            cancellationToken
        );
    }

    // What an update does to invoke_stranded_state.
    private enum StrandedWrite
    {
        // Left as it is.
        Keep,

        // Cleared.
        Clear,

        // Kept while the row stays in its state, cleared when it moves to another.
        ClearUnlessSameState,

        // Set to the state written, which the instance is stranded in.
        Mark,
    }

    // One conditional update of a row, described once so the relational statement and the InMemory provider's
    // tracked write make the same change. Every update gives the row a fresh concurrency token and updated_at and
    // bumps its revision by one: the database's trigger bumps it as well, to the same value, and is what numbers a
    // write that does not come through here.
    private sealed record RowWrite
    {
        public Guid NewToken { get; } = Guid.NewGuid();

        public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;

        // The snapshot to write, or null to leave version, state, context and the IR hash alone.
        public Snapshot? Snapshot { get; init; }

        public string? IrHash { get; init; }

        // Whether the last-request columns are written: to Request's, or cleared when it is null.
        public bool WritesRequest { get; init; }

        public AppliedRequest? Request { get; init; }

        // The ring of recent client requests to write, or null to leave it alone.
        public string? RingJson { get; init; }

        // The invoke token to write, or null to leave it alone.
        public InvokeTokenWrite? InvokeToken { get; init; }

        public StrandedWrite Stranded { get; init; }

        // What the write does to due_at, or null to leave it alone (a write with no snapshot).
        public DueWrite? Due { get; init; }

        // The due time an arming write sets, from the database's clock; read before the statement when it arms.
        public DateTimeOffset? DueValue { get; set; }

        // The claimant whose pending effect this write belongs to: the write matches only a row that effect is pending
        // on. Null for every other write, which matches only a row with no pending effect.
        public Guid? EffectOwner { get; init; }

        // What the write does to the pending-effect columns, or null to leave them alone.
        public EffectWrite? Effect { get; init; }

        // What the write records as who put the row in its state, or null to take it from Snapshot (or leave it).
        public bool? ServerRecord { get; init; }

        public bool? ServerRecordWritten => ServerRecord ?? Snapshot?.ServerRecord;

        // Whether a write that states no record clears the row's (host code writing through the store) rather than
        // leaving it (a timer, which carries the record over).
        public bool ForgetsUnstatedServerRecord { get; init; }
    }

    // What one write does to the pending-effect columns: marks the row with a claimant, its event, its request and its
    // lease (Token set), moves the claimant and lease of a mark taken over (Trigger and RequestId left as they are), or
    // clears them (Token null).
    private sealed record EffectWrite(
        Guid? Token,
        DateTimeOffset? LeaseUntil,
        bool KeepsEvent = false,
        string? Key = null,
        string? Trigger = null,
        string? RequestId = null
    )
    {
        public static readonly EffectWrite Cleared = new(null, null);
    }

    // What one write does to due_at.
    internal enum DueMode
    {
        // Cleared: the state written has no timer.
        Clear,

        // Set from now: the write enters a timed state.
        Arm,

        // Kept while the row stays in the state and holds one; set from now otherwise.
        ArmUnlessHeld,

        // Kept while the row stays in the state; cleared when it moves to another. For a state nothing describes.
        ClearUnlessSameState,
    }

    internal readonly record struct DueWrite(DueMode Mode, TimeSpan Duration)
    {
        public bool Arms => Mode is DueMode.Arm or DueMode.ArmUnlessHeld;

        // The due time a row ends with, from the state and due time it had (none for a new row) and the state written.
        public DateTimeOffset? Resolve(
            string? beforeState,
            DateTimeOffset? beforeDue,
            string state,
            DateTimeOffset? armed
        ) =>
            Mode switch
            {
                DueMode.Clear => null,
                DueMode.Arm => armed,
                DueMode.ArmUnlessHeld => beforeState == state && beforeDue is not null
                    ? beforeDue
                    : armed,
                _ => beforeState == state ? beforeDue : null,
            };
    }

    // What writing snapshot does to its row's timer: what the snapshot says, else what the machine's timers say of
    // its state, else keep a due time while the state stays and clear it when it changes.
    private DueWrite DueFor(Snapshot snapshot)
    {
        var timer =
            snapshot.Timer ?? Timers?.Invoke(snapshot.Machine, snapshot.State, snapshot.Context);
        return timer switch
        {
            null => new DueWrite(DueMode.ClearUnlessSameState, TimeSpan.Zero),
            { Duration: null } => new DueWrite(DueMode.Clear, TimeSpan.Zero),
            { Duration: { } duration, Restart: true } => new DueWrite(DueMode.Arm, duration),
            { Duration: { } duration } => new DueWrite(DueMode.ArmUnlessHeld, duration),
        };
    }

    // The due time an arming write sets: the database's clock plus the duration, so every host arms and sweeps on one
    // clock. Null when the write does not arm.
    private async Task<DateTimeOffset?> DueValue(
        DueWrite due,
        CancellationToken cancellationToken
    ) => due.Arms ? await DatabaseNow(cancellationToken) + due.Duration : null;

    /// <summary>
    /// The database's clock, in UTC: Postgres's <c>clock_timestamp()</c>, the time of the call rather than the start of
    /// the transaction, so a timer armed late in a long transaction is armed from when it is written; SQLite's
    /// <c>'now'</c>. The InMemory provider has none, so there it is this host's. A timer is armed and found due on
    /// this clock, never a host's.
    /// </summary>
    internal async Task<DateTimeOffset> DatabaseNow(CancellationToken cancellationToken)
    {
        var database = ((DbContext)db).Database;
        if (!database.IsRelational())
            return DateTimeOffset.UtcNow;
        var sql =
            database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true
                ? "SELECT to_char(clock_timestamp() AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS.US') AS \"Value\""
                : "SELECT strftime('%Y-%m-%dT%H:%M:%f', 'now') AS \"Value\"";
        var text = (
            await database.SqlQueryRaw<string>(sql).ToListAsync(cancellationToken)
        ).Single();
        return DateTimeOffset.Parse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal
        );
    }

    // One UPDATE that matches at most one row. A write that would give a second row the same invoke token is a
    // unique violation, which the dialect reads as a lost race like any other conflict; anything else propagates.
    // ExecuteUpdate throws the provider's exception unwrapped, so it is wrapped the way SaveChanges would wrap it
    // before the dialect reads it. On Postgres the failed statement still aborts an enclosing transaction (there
    // is no savepoint around it): a caller writing tokens inside its own transaction treats false as fatal to it.
    //
    // The InMemory provider has no set-based update and no trigger: there the row is read, changed and saved under
    // its concurrency token, which makes the same change and bumps the revision in the store.
    private async Task<bool> Write(
        IQueryable<SnapshotDraft> target,
        RowWrite write,
        CancellationToken cancellationToken
    )
    {
        if (write.Due is { Arms: true } arming)
            write.DueValue = await DueValue(arming, cancellationToken);

        // A row with a pending effect is written only by that effect; every other write matches a row with none.
        var owner = write.EffectOwner;
        target = owner is null
            ? target.Where(x => x.EffectToken == null)
            : target.Where(x => x.EffectToken == owner);

        if (!((DbContext)db).Database.IsRelational())
            return await WriteTracked(target, write, cancellationToken);

        try
        {
            return await target.ExecuteUpdateAsync(
                    setters => Setters(setters, write),
                    cancellationToken
                ) == 1;
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            return false;
        }
    }

    private static void Setters(UpdateSettersBuilder<SnapshotDraft> setters, RowWrite write)
    {
        var newToken = write.NewToken;
        var now = write.Now;
        setters
            .SetProperty(x => x.Revision, x => x.Revision + 1)
            .SetProperty(x => x.ConcurrencyToken, newToken)
            .SetProperty(x => x.UpdatedAt, now);

        string? state = null;
        if (write.Snapshot is { } snapshot)
        {
            var version = snapshot.Version;
            state = snapshot.State;
            var contextJson = snapshot.Context.ToJsonString();
            var irHash = write.IrHash;
            setters
                .SetProperty(x => x.Version, version)
                .SetProperty(x => x.State, state)
                .SetProperty(x => x.Context, contextJson)
                .SetProperty(x => x.IrHash, irHash);
        }

        if (write.Due is { } due)
        {
            // The comparisons read the row as it was before this statement.
            var dueValue = write.DueValue;
            switch (due.Mode)
            {
                case DueMode.Clear:
                    setters.SetProperty(x => x.DueAt, (DateTimeOffset?)null);
                    break;
                case DueMode.Arm:
                    setters.SetProperty(x => x.DueAt, dueValue);
                    break;
                case DueMode.ArmUnlessHeld:
                    setters.SetProperty(
                        x => x.DueAt,
                        x => x.State == state && x.DueAt != null ? x.DueAt : dueValue
                    );
                    break;
                default:
                    setters.SetProperty(
                        x => x.DueAt,
                        x => x.State == state ? x.DueAt : (DateTimeOffset?)null
                    );
                    break;
            }
        }

        if (write.WritesRequest)
        {
            var requestId = write.Request?.RequestId;
            var requestTrigger = write.Request?.Trigger;
            var requestFromState = write.Request?.FromState;
            setters
                .SetProperty(x => x.LastRequestId, requestId)
                .SetProperty(x => x.LastRequestTrigger, requestTrigger)
                .SetProperty(x => x.LastRequestFromState, requestFromState);
        }

        if (write.RingJson is { } ring)
            setters.SetProperty(x => x.RecentRequests, ring);

        if (write.InvokeToken is { Value: var next })
            setters.SetProperty(x => x.InvokeToken, next);

        if (write.ServerRecordWritten is { } serverRecord)
            setters.SetProperty(x => x.ServerRecord, serverRecord);
        else if (write.ForgetsUnstatedServerRecord)
            setters.SetProperty(x => x.ServerRecord, (bool?)null);

        if (write.Effect is { } effect)
        {
            var effectToken = effect.Token;
            var leaseUntil = effect.LeaseUntil;
            setters
                .SetProperty(x => x.EffectToken, effectToken)
                .SetProperty(x => x.EffectLeaseUntil, leaseUntil);
            if (!effect.KeepsEvent)
            {
                var effectKey = effect.Key;
                var effectTrigger = effect.Trigger;
                var effectRequest = effect.RequestId;
                setters
                    .SetProperty(x => x.EffectKey, effectKey)
                    .SetProperty(x => x.EffectTrigger, effectTrigger)
                    .SetProperty(x => x.EffectRequestId, effectRequest);
            }
        }

        switch (write.Stranded)
        {
            case StrandedWrite.Clear:
                setters.SetProperty(x => x.InvokeStrandedState, (string?)null);
                break;
            case StrandedWrite.Mark:
                setters.SetProperty(x => x.InvokeStrandedState, state);
                break;
            case StrandedWrite.ClearUnlessSameState:
                // The comparison reads the row as it was before this statement.
                setters.SetProperty(
                    x => x.InvokeStrandedState,
                    x => x.State == state ? x.InvokeStrandedState : null
                );
                break;
        }
    }

    private async Task<bool> WriteTracked(
        IQueryable<SnapshotDraft> target,
        RowWrite write,
        CancellationToken cancellationToken
    )
    {
        var rows = await target.Take(2).ToListAsync(cancellationToken);
        if (rows.Count != 1)
        {
            foreach (var row in rows)
                ((DbContext)db).Entry(row).State = EntityState.Detached;
            return false;
        }

        var record = rows[0];
        var before = record.State;
        if (write.Due is { } due)
            record.DueAt = due.Resolve(
                before,
                record.DueAt,
                write.Snapshot?.State ?? before,
                write.DueValue
            );
        record.Revision += 1;
        record.ConcurrencyToken = write.NewToken;
        record.UpdatedAt = write.Now;
        if (write.Snapshot is { } snapshot)
        {
            record.Version = snapshot.Version;
            record.State = snapshot.State;
            record.Context = snapshot.Context.ToJsonString();
            record.IrHash = write.IrHash;
        }
        if (write.WritesRequest)
        {
            record.LastRequestId = write.Request?.RequestId;
            record.LastRequestTrigger = write.Request?.Trigger;
            record.LastRequestFromState = write.Request?.FromState;
        }
        if (write.RingJson is { } ring)
            record.RecentRequests = ring;
        if (write.InvokeToken is { Value: var next })
            record.InvokeToken = next;
        if (write.ServerRecordWritten is { } serverRecord)
            record.ServerRecord = serverRecord;
        else if (write.ForgetsUnstatedServerRecord)
            record.ServerRecord = null;
        if (write.Effect is { } effect)
        {
            record.EffectToken = effect.Token;
            record.EffectLeaseUntil = effect.LeaseUntil;
            if (!effect.KeepsEvent)
            {
                record.EffectKey = effect.Key;
                record.EffectTrigger = effect.Trigger;
                record.EffectRequestId = effect.RequestId;
            }
        }
        record.InvokeStrandedState = write.Stranded switch
        {
            StrandedWrite.Clear => null,
            StrandedWrite.Mark => write.Snapshot?.State,
            StrandedWrite.ClearUnlessSameState when before != record.State => null,
            _ => record.InvokeStrandedState,
        };
        return await Save(record, cancellationToken);
    }

    /// <summary>
    /// The row <paramref name="owner"/>'s start with <paramref name="requestId"/> created for
    /// <paramref name="machine"/>, or null when there is none.
    /// </summary>
    internal async Task<StartedRow?> FindStarted(
        DraftOwner owner,
        string machine,
        string requestId,
        CancellationToken cancellationToken
    )
    {
        var record = await Owned(owner)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Machine == machine && x.StartRequestId == requestId,
                cancellationToken
            );
        return record is null
            ? null
            : new StartedRow(record.Id, record.StartFingerprint, ToStored(record));
    }

    /// <summary>
    /// How many drafts <paramref name="userKey"/> holds that count against the per-user cap: every one of theirs,
    /// across machines, except a draft idle since before <paramref name="expiredBefore"/> that waits on no invoked
    /// run, and a row the server put in its state, which is a record rather than a draft. A row that records neither
    /// is judged by its state: one in a state of <paramref name="kept"/> (as <see cref="DraftExpiry.Key"/> writes it)
    /// is a record. System rows are nobody's draft and never count.
    /// </summary>
    internal Task<int> CountDrafts(
        string userKey,
        DateTimeOffset? expiredBefore,
        IReadOnlyCollection<string>? kept,
        CancellationToken cancellationToken
    )
    {
        var keptStates = kept ?? [];
        var drafts = Owned(DraftOwner.User(userKey))
            .Where(x =>
                x.ServerRecord == false
                || (
                    x.ServerRecord == null
                    && !keptStates.Contains(x.Machine + DraftExpiry.Separator + x.State)
                )
            );
        if (expiredBefore is { } cutoff)
            drafts = drafts.Where(x => x.UpdatedAt >= cutoff || x.InvokeToken != null);
        return drafts.CountAsync(cancellationToken);
    }

    /// <summary>
    /// Creates <paramref name="owner"/>'s row for a <c>start</c>, recording its request id and input fingerprint, and
    /// the run its initial state invoked. Null when the owner already holds the id, or a start with the request id,
    /// for the machine.
    /// </summary>
    internal Task<WrittenRow?> InsertStarted(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        string? invokeToken,
        StartRequest start,
        CancellationToken cancellationToken
    ) => InsertOwned(owner, id, snapshot, cancellationToken, invokeToken, start);

    /// <summary>
    /// Marks <paramref name="owner"/>'s row active now, guarded by <paramref name="expectedToken"/>, without changing
    /// its snapshot: a fresh concurrency token, <c>updated_at</c> and the next revision. False when the row changed
    /// since it was read, or is gone.
    /// </summary>
    internal Task<bool> Touch(
        DraftOwner owner,
        string machine,
        Guid id,
        Guid expectedToken,
        CancellationToken cancellationToken
    ) =>
        Write(
            Owned(owner)
                .Where(x =>
                    x.Id == id && x.Machine == machine && x.ConcurrencyToken == expectedToken
                ),
            new RowWrite { Stranded = StrandedWrite.Keep },
            cancellationToken
        );

    /// <summary>
    /// Up to <paramref name="limit"/> users' drafts of <paramref name="machines"/> that <paramref name="expiry"/> says
    /// have expired, oldest first. System rows are never read.
    /// </summary>
    internal async Task<IReadOnlyList<ExpiredDraft>> ReadExpired(
        IReadOnlyCollection<string> machines,
        DraftExpiry expiry,
        int limit,
        CancellationToken cancellationToken
    ) =>
        await Expired(db.SnapshotDrafts.AsNoTracking(), expiry)
            .Where(x => x.UserKey != null && machines.Contains(x.Machine))
            .OrderBy(x => x.UpdatedAt)
            .ThenBy(x => x.RowId)
            .Take(limit)
            .Select(x => new ExpiredDraft(
                x.RowId,
                x.UserKey!,
                x.Machine,
                x.Id,
                x.Revision,
                x.InvokeToken,
                x.State,
                x.Context
            ))
            .ToListAsync(cancellationToken);

    // A user's draft has expired when it has been idle past the TTL with no timer armed and no invoked run, or has
    // lived past the maximum lifetime whatever its timers or its run say. Neither applies to a row the server put in its
    // state, nor to one with a pending effect; a row that does not record who put it there is kept while its state is
    // one the earlier rule kept (see DraftExpiry.Kept). An armed timer, due or not, keeps an idle draft until it fires:
    // the state moves on by itself, and the fire is a write the TTL counts again from. A row written before its creation
    // time was recorded counts its age from its last write.
    private static IQueryable<SnapshotDraft> Expired(
        IQueryable<SnapshotDraft> drafts,
        DraftExpiry expiry
    )
    {
        var idleBefore = expiry.IdleBefore ?? DateTimeOffset.MinValue;
        var idle = expiry.IdleBefore is not null;
        var bornBefore = expiry.BornBefore ?? DateTimeOffset.MinValue;
        var old = expiry.BornBefore is not null;
        var kept = expiry.Kept ?? [];
        return drafts.Where(x =>
            x.OwnerKind == SnapshotOwnerKind.User
            && x.EffectToken == null
            && (
                x.ServerRecord == false
                || (
                    x.ServerRecord == null
                    && !kept.Contains(x.Machine + DraftExpiry.Separator + x.State)
                )
            )
            && (
                (idle && x.InvokeToken == null && x.UpdatedAt < idleBefore && x.DueAt == null)
                || (old && (x.CreatedAt ?? x.UpdatedAt) < bornBefore)
            )
        );
    }

    /// <summary>
    /// Deletes the expired draft <paramref name="draft"/> names only while it is still the row, revision and invoke
    /// token that were read and still expired by <paramref name="expiry"/>, so a draft written since the read is kept,
    /// then runs <paramref name="afterDelete"/> in the same transaction. True when it was deleted.
    /// </summary>
    internal Task<bool> DeleteExpired(
        ExpiredDraft draft,
        DraftExpiry expiry,
        Func<Task> afterDelete
    )
    {
        var token = draft.InvokeToken;
        return DeleteThen(
            Expired(db.SnapshotDrafts, expiry)
                .Where(x =>
                    x.RowId == draft.RowId && x.Revision == draft.Revision && x.InvokeToken == token
                ),
            afterDelete
        );
    }

    /// <summary>
    /// Up to <paramref name="limit"/> rows of <paramref name="machines"/> whose timer is due on the database's clock,
    /// soonest first, leaving out <paramref name="skip"/>. The read takes no lock: each row is then fired in a
    /// transaction of its own, which first locks it (<see cref="LockDue"/>).
    /// </summary>
    internal async Task<IReadOnlyList<DueRow>> ReadDue(
        IReadOnlyCollection<string> machines,
        IReadOnlyCollection<long> skip,
        int limit,
        CancellationToken cancellationToken
    )
    {
        var now = await DatabaseNow(cancellationToken);
        var machineList = machines.ToList();
        var skipList = skip.ToList();
        return await db
            .SnapshotDrafts.AsNoTracking()
            .Where(x =>
                x.DueAt != null
                && x.DueAt <= now
                && x.EffectToken == null
                && machineList.Contains(x.Machine)
                && !skipList.Contains(x.RowId)
            )
            .OrderBy(x => x.DueAt)
            .ThenBy(x => x.RowId)
            .Take(limit)
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
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Locks <paramref name="due"/>'s row for the caller's transaction and says whether it is still the revision that
    /// was read. On Postgres the row is locked <c>FOR UPDATE SKIP LOCKED</c>, so a row another sweep or an effect
    /// runner's check holds is passed over rather than waited on; on SQLite the caller's transaction already holds
    /// the database's write lock. False when the row is gone, held elsewhere or written since it was read.
    /// </summary>
    internal async Task<bool> LockDue(DueRow due, CancellationToken cancellationToken)
    {
        var database = ((DbContext)db).Database;
        if (IsPostgres(database))
            return (
                    await database
                        .SqlQueryRaw<long>(
                            """
                            SELECT revision AS "Value" FROM trax.snapshot_draft
                            WHERE row_id = {0}
                            FOR UPDATE SKIP LOCKED
                            """,
                            due.RowId
                        )
                        .ToListAsync(cancellationToken)
                )
                    is [var locked]
                && locked == due.Revision;
        return await db
            .SnapshotDrafts.AsNoTracking()
            .AnyAsync(x => x.RowId == due.RowId && x.Revision == due.Revision, cancellationToken);
    }

    /// <summary>
    /// Whether the row <paramref name="rowId"/> is still at <paramref name="revision"/>. On Postgres the read takes a
    /// share lock on the row, so it waits for a transaction that is writing it (a timer's fire) to end and then reads
    /// what that transaction left. The effect runner asks after it has claimed the effect and before it runs it.
    /// </summary>
    internal async Task<bool> StillAt(
        long rowId,
        long revision,
        CancellationToken cancellationToken
    )
    {
        var database = ((DbContext)db).Database;
        if (IsPostgres(database))
            return (
                    await database
                        .SqlQueryRaw<long>(
                            """
                            SELECT revision AS "Value" FROM trax.snapshot_draft
                            WHERE row_id = {0}
                            FOR SHARE
                            """,
                            rowId
                        )
                        .ToListAsync(cancellationToken)
                )
                    is [var current]
                && current == revision;
        return await db
            .SnapshotDrafts.AsNoTracking()
            .AnyAsync(x => x.RowId == rowId && x.Revision == revision, cancellationToken);
    }

    private static bool IsPostgres(
        Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade database
    ) => database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) == true;

    /// <summary>
    /// How long until the soonest timer of <paramref name="machines"/> is due, on the database's clock, leaving out
    /// <paramref name="skip"/> and the rows with a pending effect, which wait for it: zero when one is due already, null
    /// when none is pending.
    /// </summary>
    internal async Task<TimeSpan?> UntilNextDue(
        IReadOnlyCollection<string> machines,
        IReadOnlyCollection<long> skip,
        CancellationToken cancellationToken
    )
    {
        var machineList = machines.ToList();
        var skipList = skip.ToList();
        var next = await db
            .SnapshotDrafts.AsNoTracking()
            .Where(x =>
                x.DueAt != null
                && x.EffectToken == null
                && machineList.Contains(x.Machine)
                && !skipList.Contains(x.RowId)
            )
            .OrderBy(x => x.DueAt)
            .Select(x => x.DueAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (next is not { } due)
            return null;
        var until = due - await DatabaseNow(cancellationToken);
        return until > TimeSpan.Zero ? until : TimeSpan.Zero;
    }

    /// <summary>
    /// The fire of a due timer: writes <paramref name="next"/> over <paramref name="due"/>'s row only while it is still
    /// the row, state and revision that was read, so whatever wrote it since wins and the fire matches nothing. Sets
    /// the invoke token when <paramref name="invokeToken"/> is given, never touches the client's request ring, and
    /// gives the row the due time its new state's timer sets. True only for the write that matched.
    /// </summary>
    internal Task<bool> FireTimer(
        DueRow due,
        Snapshot next,
        InvokeTokenWrite? invokeToken,
        CancellationToken cancellationToken
    ) =>
        Write(
            db.SnapshotDrafts.Where(x =>
                x.RowId == due.RowId && x.State == due.State && x.Revision == due.Revision
            ),
            new RowWrite
            {
                Snapshot = next,
                IrHash = IrHashOf(next.Machine),
                Due = DueFor(next),
                InvokeToken = invokeToken,
                Stranded = StrandedWrite.Clear,
            },
            cancellationToken
        );

    /// <summary>
    /// Clears the due time of <paramref name="due"/>'s row while it is still the row, state and revision that was read:
    /// a timer that cannot be fired, or one the row's state no longer declares, is not tried again. True when it
    /// matched.
    /// </summary>
    internal Task<bool> ClearDue(DueRow due, CancellationToken cancellationToken) =>
        Write(
            db.SnapshotDrafts.Where(x =>
                x.RowId == due.RowId && x.State == due.State && x.Revision == due.Revision
            ),
            new RowWrite
            {
                Due = new DueWrite(DueMode.Clear, TimeSpan.Zero),
                Stranded = StrandedWrite.Keep,
            },
            cancellationToken
        );

    /// <summary>
    /// Marks <paramref name="owner"/>'s row with a pending effect of <paramref name="trigger"/>, to run under
    /// <paramref name="key"/>, claimed by
    /// <paramref name="token"/> until <paramref name="lease"/> from now on the database's clock, only while the row
    /// still carries <paramref name="expectedToken"/>, the concurrency token it was read with, and has no pending
    /// effect. From then on no other write changes the row until the effect is recorded or ended. False when the row
    /// changed since it was read.
    /// </summary>
    internal async Task<bool> MarkEffect(
        DraftOwner owner,
        string machine,
        Guid id,
        Guid expectedToken,
        string key,
        string trigger,
        string? requestId,
        Guid token,
        TimeSpan lease,
        CancellationToken cancellationToken
    )
    {
        var leaseUntil = await DatabaseNow(cancellationToken) + lease;
        return await Write(
            Owned(owner)
                .Where(x =>
                    x.Id == id && x.Machine == machine && x.ConcurrencyToken == expectedToken
                ),
            new RowWrite
            {
                Stranded = StrandedWrite.Keep,
                Effect = new EffectWrite(
                    token,
                    leaseUntil,
                    Key: key,
                    Trigger: trigger,
                    RequestId: requestId
                ),
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Takes over the effect pending on <paramref name="owner"/>'s row from <paramref name="from"/> once its lease has
    /// passed on the database's clock: the row's claimant becomes <paramref name="token"/>, with a lease of
    /// <paramref name="lease"/> from now. False when the effect is no longer pending under <paramref name="from"/>, or
    /// its lease has not passed.
    /// </summary>
    internal async Task<bool> TakeOverEffect(
        DraftOwner owner,
        string machine,
        Guid id,
        Guid from,
        Guid token,
        TimeSpan lease,
        CancellationToken cancellationToken
    )
    {
        var now = await DatabaseNow(cancellationToken);
        return await Write(
            Owned(owner)
                .Where(x =>
                    x.Id == id
                    && x.Machine == machine
                    && x.EffectLeaseUntil != null
                    && x.EffectLeaseUntil < now
                ),
            new RowWrite
            {
                Stranded = StrandedWrite.Keep,
                EffectOwner = from,
                Effect = new EffectWrite(token, now + lease, KeepsEvent: true),
            },
            cancellationToken
        );
    }

    /// <summary>
    /// Ends the effect <paramref name="token"/> claimed on <paramref name="owner"/>'s row without recording anything,
    /// leaving the row's snapshot as it was: the effect did not happen. False when it is no longer that claimant's.
    /// </summary>
    internal Task<bool> EndEffect(
        DraftOwner owner,
        string machine,
        Guid id,
        Guid token,
        CancellationToken cancellationToken
    ) =>
        Write(
            Owned(owner).Where(x => x.Id == id && x.Machine == machine),
            new RowWrite
            {
                Stranded = StrandedWrite.Keep,
                EffectOwner = token,
                Effect = EffectWrite.Cleared,
            },
            cancellationToken
        );

    /// <summary>
    /// Records the effect <paramref name="token"/> claimed on <paramref name="owner"/>'s row: writes
    /// <paramref name="snapshot"/> (the effect's target, holding its receipt) with <paramref name="request"/>, records
    /// that the server put the row there, and ends the mark, in one write that matches only while the effect is still
    /// that claimant's. Reports the row it left, from <paramref name="read"/>, the row as read under the mark.
    /// </summary>
    internal async Task<WrittenRow?> CompleteEffect(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        StoredSnapshot read,
        AppliedRequest? request,
        Guid token,
        CancellationToken cancellationToken
    )
    {
        var machineId = snapshot.Machine;
        var write = UpdateWrite(snapshot, request, invokeToken: null) with
        {
            EffectOwner = token,
            Effect = EffectWrite.Cleared,
            ServerRecord = true,
        };
        if (
            !await Write(
                Owned(owner).Where(x => x.Id == id && x.Machine == machineId),
                write,
                cancellationToken
            )
        )
            return null;
        var due = write.Due is { } written
            ? written.Resolve(StateOf(read.Json), read.DueAt, snapshot.State, write.DueValue)
            : read.DueAt;
        return new WrittenRow(read.RowId ?? 0, (read.Revision ?? 0) + 1) { DueAt = due };
    }

    /// <summary>
    /// Up to <paramref name="limit"/> users' rows of <paramref name="machines"/> whose pending effect's lease has passed
    /// on the database's clock, the longest overdue first.
    /// </summary>
    internal async Task<IReadOnlyList<AbandonedEffect>> ReadAbandonedEffects(
        IReadOnlyCollection<string> machines,
        int limit,
        CancellationToken cancellationToken
    )
    {
        var now = await DatabaseNow(cancellationToken);
        var machineList = machines.ToList();
        return await db
            .SnapshotDrafts.AsNoTracking()
            .Where(x =>
                x.EffectToken != null
                && x.EffectLeaseUntil != null
                && x.EffectLeaseUntil < now
                && x.OwnerKind == SnapshotOwnerKind.User
                && x.UserKey != null
                && machineList.Contains(x.Machine)
            )
            .OrderBy(x => x.EffectLeaseUntil)
            .ThenBy(x => x.RowId)
            .Take(limit)
            .Select(x => new AbandonedEffect(x.RowId, x.UserKey!, x.Machine, x.Id, x.EffectTrigger))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The claims the earlier effect-claim ledger holds under <paramref name="keys"/>: each one's key, receipt (null
    /// while in flight), content fingerprint, and whether its lease is still live on the database's clock.
    /// </summary>
    internal async Task<IReadOnlyList<LedgerClaim>> LedgerClaims(
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken
    )
    {
        if (keys.Count == 0)
            return [];
        var keyList = keys.ToList();
        var claims = await db
            .EffectClaims.AsNoTracking()
            .Where(c => keyList.Contains(c.EffectKey))
            .Select(c => new
            {
                c.EffectKey,
                c.Receipt,
                c.ContentFingerprint,
                c.LeaseExpiresAt,
            })
            .ToListAsync(cancellationToken);
        if (claims.Count == 0)
            return [];
        var now = await DatabaseNow(cancellationToken);
        return claims
            .Select(c => new LedgerClaim(
                c.EffectKey,
                c.Receipt,
                c.ContentFingerprint,
                c.LeaseExpiresAt > now
            ))
            .ToList();
    }

    /// <summary>Removes the earlier ledger's claims under <paramref name="keys"/>, on a token nothing cancels.</summary>
    internal async Task ReleaseLedger(IReadOnlyCollection<string> keys)
    {
        if (keys.Count == 0)
            return;
        var keyList = keys.ToList();
        if (((DbContext)db).Database.IsRelational())
        {
            await db
                .EffectClaims.Where(c => keyList.Contains(c.EffectKey))
                .ExecuteDeleteAsync(CancellationToken.None);
            return;
        }
        var rows = await db
            .EffectClaims.Where(c => keyList.Contains(c.EffectKey))
            .ToListAsync(CancellationToken.None);
        db.EffectClaims.RemoveRange(rows);
        await ((DbContext)db).SaveChangesAsync(CancellationToken.None);
        foreach (var row in rows)
            ((DbContext)db).Entry(row).State = EntityState.Detached;
    }

    private bool IsUniqueViolation(Exception exception) =>
        dialect is not null
        && dialect.IsUniqueViolation(
            exception as DbUpdateException ?? new DbUpdateException(exception.Message, exception)
        );
}

/// <summary>The request id and input fingerprint of the <c>start</c> that creates a row.</summary>
/// <param name="RequestId">The client's request id.</param>
/// <param name="Fingerprint">The fingerprint of the start's input.</param>
internal sealed record StartRequest(string RequestId, string Fingerprint);

/// <summary>A row a <c>start</c> created: its id, the fingerprint of the input it was started with, and the row as stored.</summary>
internal sealed record StartedRow(Guid Id, string? Fingerprint, StoredSnapshot Stored);

/// <summary>When a user's draft has expired, as one pass of the draft sweep reads it.</summary>
/// <param name="IdleBefore">
/// A draft last written before this, with no timer armed and no invoked run, has expired, unless its state is
/// <paramref name="Kept"/>; null for no TTL.
/// </param>
/// <param name="BornBefore">
/// A draft created before this (or, with no creation time recorded, last written before it) has expired whatever its
/// timers or its run say, unless its state is <paramref name="Kept"/>; null for no bound.
/// </param>
/// <param name="Kept">
/// For a row that does not record whether the server put it in its state, the states both
/// <paramref name="IdleBefore"/> and <paramref name="BornBefore"/> leave alone, each as <see cref="Key"/> writes it; null
/// keeps none. A row that records it is kept when the server put it there, whatever its state.
/// </param>
internal readonly record struct DraftExpiry(
    DateTimeOffset? IdleBefore,
    DateTimeOffset? BornBefore,
    IReadOnlyCollection<string>? Kept = null
)
{
    /// <summary>Between a machine's name and a state's in <see cref="Key"/>; in neither, being a control character.</summary>
    internal const string Separator = "\u001f";

    /// <summary>One machine's state, as <see cref="Kept"/> lists it.</summary>
    internal static string Key(string machine, string state) => machine + Separator + state;
}

/// <summary>
/// A user's draft the sweep read as expired: which row, whose, of which machine, at which revision, the run it holds,
/// its state, and its context, which the sweep reads the receipts it recorded from.
/// </summary>
internal sealed record ExpiredDraft(
    long RowId,
    string UserKey,
    string Machine,
    Guid Id,
    long Revision,
    string? InvokeToken,
    string State,
    string Context
);

/// <summary>A user's row whose pending effect's lease has passed, as the reconciler read it.</summary>
internal sealed record AbandonedEffect(
    long RowId,
    string UserKey,
    string Machine,
    Guid Id,
    string? Trigger
);

/// <summary>A claim of the earlier effect-claim ledger, as a row's effect key names it.</summary>
/// <param name="Key">The effect key.</param>
/// <param name="Receipt">The recorded receipt, or null while the claim is in flight.</param>
/// <param name="ContentFingerprint">The fingerprint of the content the effect ran on, or null when none was recorded.</param>
/// <param name="LeaseLive">Whether the claim's lease has not passed yet, on the database's clock.</param>
internal sealed record LedgerClaim(
    string Key,
    string? Receipt,
    string? ContentFingerprint,
    bool LeaseLive
);

/// <summary>A row whose timer is due, as the sweep read it.</summary>
internal sealed record DueRow(
    long RowId,
    Guid Id,
    string Machine,
    SnapshotOwnerKind OwnerKind,
    string? UserKey,
    int Version,
    string State,
    string Context,
    long Revision,
    string? InvokeToken
)
{
    /// <summary>The row's snapshot as stored, which the machine rehydrates (and migrates) before the timer fires.</summary>
    public string Json =>
        new JsonObject
        {
            ["machine"] = Machine,
            ["version"] = Version,
            ["state"] = State,
            ["context"] = JsonNode.Parse(Context),
        }.ToJsonString();
}
