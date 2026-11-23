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
