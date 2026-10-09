using Trax.Effect.Enums;
using Trax.Effect.StateMachine;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The owner a draft row is looked up under. Every lookup names the owner kind as well as the id, because a user
/// may hold a draft under the same id as a system instance and must never reach it.
/// </summary>
/// <param name="Kind">Whether a user or the system owns the row.</param>
/// <param name="UserKey">The user's key on a user row; null on a system row.</param>
internal readonly record struct DraftOwner(SnapshotOwnerKind Kind, string? UserKey)
{
    /// <summary>The system, which owns instances created from code and has no user key.</summary>
    public static DraftOwner System { get; } = new(SnapshotOwnerKind.System, null);

    /// <summary>The user <paramref name="userKey"/> names.</summary>
    public static DraftOwner User(string userKey) => new(SnapshotOwnerKind.User, userKey);
}

/// <summary>A write to a row's <c>invoke_token</c>: set it to <see cref="Value"/>, or clear it when that is null.</summary>
/// <param name="Value">The token to store, or null to clear it.</param>
internal readonly record struct InvokeTokenWrite(string? Value);

/// <summary>A row read by its invoke token: who owns it, which draft it is, and the draft as stored.</summary>
/// <param name="Owner">The row's owner.</param>
/// <param name="Machine">The machine the row belongs to.</param>
/// <param name="Id">The draft or instance id.</param>
/// <param name="Snapshot">The stored snapshot, its concurrency token and its invoke token.</param>
internal sealed record StoredInstance(
    DraftOwner Owner,
    string Machine,
    Guid Id,
    StoredSnapshot Snapshot
);

/// <summary>A row that holds a live invoke token, as the outcome reconciler lists them.</summary>
/// <param name="Owner">The row's owner.</param>
/// <param name="Machine">The machine the row belongs to.</param>
/// <param name="Id">The draft or instance id.</param>
/// <param name="State">The state the row is in, the one whose invoked run the token names.</param>
/// <param name="InvokeToken">The token, the external id of the run the state invoked.</param>
internal sealed record InvokingInstance(
    DraftOwner Owner,
    string Machine,
    Guid Id,
    string State,
    string InvokeToken
);

/// <summary>
/// Owner-aware, server-only access to <c>trax.snapshot_draft</c>: system-owned instances, and the
/// <c>invoke_token</c> that correlates an invoked train run with the state that queued it. Internal because no
/// host or client writes either: a host's own <see cref="ISnapshotStore"/> stays user-scoped, and these rows and
/// tokens are reached only through the data context the provider registers.
/// </summary>
/// <remarks>
/// Every write is total in the same way as <see cref="ISnapshotStore"/>: an expected race (a stale concurrency
/// token, a token that no longer matches, a unique key another writer took first) returns <c>false</c>, and any
/// other database failure propagates.
/// </remarks>
internal interface IMachineInstanceStore
{
    /// <summary>Reads the row <paramref name="owner"/> holds for <paramref name="machine"/> under <paramref name="id"/>, or null.</summary>
    Task<StoredSnapshot?> Get(
        DraftOwner owner,
        string machine,
        Guid id,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates <paramref name="owner"/>'s row for <paramref name="snapshot"/>'s machine under <paramref name="id"/>,
    /// and only creates it: false when that owner already has one, which is how two concurrent creates of one
    /// system instance make one row.
    /// </summary>
    Task<bool> Insert(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken = default
    ) => Insert(owner, id, snapshot, invokeToken: null, cancellationToken);

    /// <summary>
    /// <see cref="Insert(DraftOwner, Guid, Snapshot, CancellationToken)"/> that also stores
    /// <paramref name="invokeToken"/>, the run the new row's initial state invoked.
    /// </summary>
    Task<bool> Insert(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        string? invokeToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Deletes <paramref name="owner"/>'s row for <paramref name="machine"/> under <paramref name="id"/> only while it
    /// still holds <paramref name="invokeToken"/> (none, when null): a draft whose run was cancelled before it is
    /// deleted, and which has since entered an invoking state again, keeps its new run. False when nothing was
    /// deleted.
    /// </summary>
    Task<bool> DeleteHolding(
        DraftOwner owner,
        string machine,
        Guid id,
        string? invokeToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// One atomic update of <paramref name="owner"/>'s row, guarded by <paramref name="expectedToken"/>: writes the
    /// snapshot, the applied request (cleared when null), a fresh concurrency token and <c>updated_at</c>, and the
    /// invoke token when <paramref name="invokeToken"/> is given (left alone when it is null). False when the row
    /// changed since it was read or does not exist.
    /// </summary>
    Task<bool> Update(
        DraftOwner owner,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        AppliedRequest? request,
        InvokeTokenWrite? invokeToken = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sets or clears the invoke token of <paramref name="owner"/>'s row, guarded by
    /// <paramref name="expectedToken"/>, and gives the row a fresh concurrency token. False when the row changed
    /// since it was read, does not exist, or (as a unique violation) another row already holds the token.
    /// </summary>
    Task<bool> SetInvokeToken(
        DraftOwner owner,
        string machine,
        Guid id,
        Guid expectedToken,
        InvokeTokenWrite invokeToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>Reads the row whose invoke token is <paramref name="invokeToken"/>, whoever owns it, or null.</summary>
    Task<StoredInstance?> GetByInvokeToken(
        string invokeToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// The conditional update an outcome is applied with: <c>UPDATE ... WHERE invoke_token = @invokeToken</c>
    /// (and, when <paramref name="expectedToken"/> is given, the concurrency token too). Writes the snapshot, the
    /// next invoke token (null leaves the row with no live run), a fresh concurrency token and <c>updated_at</c>.
    /// True only for the write that matched: a token that was cleared or replaced, or an outcome applied already,
    /// matches nothing and returns false.
    /// </summary>
    Task<bool> ApplyByInvokeToken(
        string invokeToken,
        Snapshot snapshot,
        string? nextInvokeToken,
        Guid? expectedToken = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Leaves the row that still holds <paramref name="invokeToken"/> and <paramref name="expectedToken"/> in
    /// <paramref name="snapshot"/>'s state with no live run, in one conditional update: writes the snapshot, clears
    /// the invoke token, records the state in <c>invoke_stranded_state</c>, and gives the row a fresh concurrency
    /// token and <c>updated_at</c>. Used when a run ended and not even its failure could be applied. False when no
    /// row holds both tokens.
    /// </summary>
    Task<bool> StrandByInvokeToken(
        string invokeToken,
        Snapshot snapshot,
        Guid expectedToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Up to <paramref name="limit"/> rows that hold a live invoke token, ordered by the token, starting after
    /// <paramref name="afterToken"/> (keyset paging; null starts at the beginning). The reconciler sweeps these.
    /// </summary>
    Task<IReadOnlyList<InvokingInstance>> ListInvoking(
        int limit,
        string? afterToken = null,
        CancellationToken cancellationToken = default
    );
}
