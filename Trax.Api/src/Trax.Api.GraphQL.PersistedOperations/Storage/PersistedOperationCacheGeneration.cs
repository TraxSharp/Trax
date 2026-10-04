namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// Counts the changes this node has applied to its persisted-operation caches. Every cache entry
/// carries the generation that was current before the document it holds was read, and an entry
/// from an earlier generation is never returned.
/// </summary>
/// <remarks>
/// <para>
/// Each entry is stamped with the generation its request started in, and only a current stamp is
/// served, so an entry written by a request that started before a change is never served,
/// whenever the write lands. It is the guard the lease tokens of Facebook's memcache deployment
/// put on a cache fill.
/// </para>
/// <para>
/// See <c>docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md</c>.
/// </para>
/// </remarks>
internal sealed class PersistedOperationCacheGeneration
{
    private long _value;

    /// <summary>The generation an entry must carry to be served.</summary>
    public long Current => Interlocked.Read(ref _value);

    /// <summary>
    /// Makes every entry stamped so far unservable. Called after a change is committed, and when
    /// this node may have missed one.
    /// </summary>
    public void Advance() => Interlocked.Increment(ref _value);
}

/// <summary>
/// What the caches need to know about the request that is filling them: the generation when it
/// started, the oldest document it was given, and whether it carried its own document. Flows
/// with the request's async context from <see cref="Middleware.PersistedOperationCacheScopeMiddleware"/>.
/// </summary>
internal sealed class PersistedOperationRequestScope
{
    private static readonly AsyncLocal<PersistedOperationRequestScope?> s_current = new();

    private long _sourceTimestamp = long.MaxValue;

    private PersistedOperationRequestScope(
        PersistedOperationCacheGeneration generation,
        bool carriesDocument
    )
    {
        Generation = generation;
        StartGeneration = generation.Current;
        CarriesDocument = carriesDocument;
    }

    /// <summary>The node's generation counter this scope was opened against.</summary>
    public PersistedOperationCacheGeneration Generation { get; }

    /// <summary>The generation when the request started, before it read anything.</summary>
    public long StartGeneration { get; }

    /// <summary>True when the request sent a document of its own rather than only an id.</summary>
    public bool CarriesDocument { get; }

    /// <summary>
    /// When the oldest document this request was given was read from the database, as a
    /// <see cref="TimeProvider"/> timestamp; null when it read none.
    /// </summary>
    public long? SourceTimestamp
    {
        get
        {
            var value = Interlocked.Read(ref _sourceTimestamp);
            return value == long.MaxValue ? null : value;
        }
    }

    /// <summary>Records that this request was given a document read at <paramref name="timestamp"/>.</summary>
    public void NoteSource(long timestamp)
    {
        var current = Interlocked.Read(ref _sourceTimestamp);
        while (timestamp < current)
        {
            var seen = Interlocked.CompareExchange(ref _sourceTimestamp, timestamp, current);
            if (seen == current)
                return;
            current = seen;
        }
    }

    /// <summary>The scope of the request running on this async flow, if it belongs to <paramref name="generation"/>.</summary>
    public static PersistedOperationRequestScope? For(
        PersistedOperationCacheGeneration generation
    ) =>
        s_current.Value is { } scope && ReferenceEquals(scope.Generation, generation)
            ? scope
            : null;

    /// <summary>
    /// Opens a scope for the rest of the current async flow. The caller must run inside its own
    /// async method so the value does not leak back to its caller.
    /// </summary>
    public static PersistedOperationRequestScope Begin(
        PersistedOperationCacheGeneration generation,
        bool carriesDocument
    )
    {
        var scope = new PersistedOperationRequestScope(generation, carriesDocument);
        s_current.Value = scope;
        return scope;
    }

    /// <summary>Closes whatever scope is open on this async flow.</summary>
    public static void End() => s_current.Value = null;
}
