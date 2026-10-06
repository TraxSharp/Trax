namespace Trax.Samples.StateMachine.Tests.Fakes;

/// <summary>
/// A dictionary-backed <see cref="ISnapshotStore"/> for driving the sample machines without a database. It
/// keeps the same contract the EF store enforces: user-scoped rows, optimistic concurrency on a token, and
/// total writes (a stale <see cref="Update"/> returns <c>false</c> rather than throwing). <see cref="Seed"/>
/// injects a raw stored snapshot, which is how a test stands in for "a draft an older host already wrote".
/// </summary>
public sealed class InMemorySnapshotStore : ISnapshotStore
{
    private readonly Dictionary<(string UserKey, Guid Id), StoredSnapshot> _rows = new();

    /// <summary>Pre-populate a stored draft verbatim (e.g. an older-version snapshot to migrate on load).</summary>
    public void Seed(string userKey, Guid id, string json, string? lastRequestId = null) =>
        _rows[(userKey, id)] = new StoredSnapshot(
            json,
            Guid.NewGuid(),
            lastRequestId,
            DateTimeOffset.UtcNow
        );

    public Task<StoredSnapshot?> Get(
        string userKey,
        Guid id,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(_rows.TryGetValue((userKey, id), out var row) ? row : null);

    public Task Delete(string userKey, Guid id, CancellationToken cancellationToken = default)
    {
        _rows.Remove((userKey, id));
        return Task.CompletedTask;
    }

    public Task<bool> Upsert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken = default
    )
    {
        _rows.TryGetValue((userKey, id), out var existing);
        _rows[(userKey, id)] = new StoredSnapshot(
            ToJson(snapshot),
            Guid.NewGuid(),
            existing?.LastRequestId,
            DateTimeOffset.UtcNow
        );
        return Task.FromResult(true);
    }

    public Task<bool> Update(
        string userKey,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        string? requestId = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!_rows.TryGetValue((userKey, id), out var row) || row.Token != expectedToken)
            return Task.FromResult(false);

        _rows[(userKey, id)] = new StoredSnapshot(
            ToJson(snapshot),
            Guid.NewGuid(),
            requestId ?? row.LastRequestId,
            DateTimeOffset.UtcNow
        );
        return Task.FromResult(true);
    }

    // The store persists the four snapshot fields as JSON; the service re-canonicalizes on read, so any
    // valid JSON round-trips. Rehydrate re-parses this, so exact byte-canonicality here does not matter.
    private static string ToJson(Snapshot snapshot) =>
        new JsonObject
        {
            ["machine"] = snapshot.Machine,
            ["version"] = snapshot.Version,
            ["state"] = snapshot.State,
            ["context"] = snapshot.Context?.DeepClone(),
        }.ToJsonString();
}
