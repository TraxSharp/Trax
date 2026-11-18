using System.Collections.Concurrent;
using HotChocolate.Execution.Caching;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;

namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// Replacements for HotChocolate's parsed-document and prepared-operation caches that can
/// be emptied, never return an entry older than the last change, and never return one older
/// than the maximum age.
/// </summary>
/// <remarks>
/// HotChocolate assumes a persisted-operation id maps to one document for all time, so its
/// caches are keyed on the id, offer no way to drop an entry, and never expire: version 16
/// removed the <c>Clear()</c> both caches used to expose, and evicting the executor does not
/// rebuild one whose schema has not changed. Trax lets an operator re-upload or deactivate a
/// document under an existing id, so it registers these instead: same contracts, plus the
/// generation stamp and maximum age described in
/// <c>docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md</c>.
/// <para>
/// Registered only when the persisted-operations package is wired in, so a host that does
/// not use the feature keeps HotChocolate's own caches.
/// </para>
/// </remarks>
internal sealed class ClearableDocumentCache(
    int capacity,
    PersistedOperationCacheGeneration generation,
    TimeProvider clock,
    TimeSpan maxAge
) : IDocumentCache
{
    private readonly StampedCache<CachedDocument> _cache = new(capacity, generation, clock, maxAge);

    public int Capacity => capacity;

    public int Count => _cache.Count;

    public bool TryGetDocument(string documentId, out CachedDocument document) =>
        _cache.TryGet(documentId, out document!);

    /// <summary>
    /// Caches a document under <paramref name="documentId"/>. A document the store did not supply
    /// is cached only under its own hash: the id a request names is the store's to define, and an
    /// inline document cached under it would become what that id runs.
    /// </summary>
    public void TryAddDocument(string documentId, CachedDocument document)
    {
        if (
            !document.IsPersisted
            && !string.Equals(documentId, document.Hash.Value, StringComparison.Ordinal)
        )
            return;

        _cache.Add(documentId, document);
    }

    public void Clear() => _cache.Clear();
}
