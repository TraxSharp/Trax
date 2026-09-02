using FluentAssertions;
using HotChocolate.Language;
using Trax.Api.GraphQL.PersistedOperations.Storage;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// Trax substitutes these for HotChocolate's own caches purely so a persisted-operation
/// upsert can drop what it cached. They still have to behave like caches: hold entries,
/// stay bounded, and keep a live working set across generation turnover.
/// </summary>
[TestFixture]
public class ClearableOperationCachesTests
{
    private static CachedDocument Document(string text) =>
        new(Utf8GraphQLParser.Parse(text), default, isPersisted: true);

    #region ClearableDocumentCache

    [Test]
    public void DocumentCache_StoresAndReturnsAnEntry()
    {
        var cache = new ClearableDocumentCache(16);
        var document = Document("{ a }");

        cache.TryAddDocument("id", document);

        cache.TryGetDocument("id", out var found).Should().BeTrue();
        found.Should().Be(document);
        cache.Count.Should().Be(1);
    }

    [Test]
    public void DocumentCache_OverwritesAnExistingEntry()
    {
        // This is the whole point: the same id must be able to hold a new document.
        var cache = new ClearableDocumentCache(16);
        cache.TryAddDocument("id", Document("{ a }"));

        var replacement = Document("{ b }");
        cache.TryAddDocument("id", replacement);

        cache.TryGetDocument("id", out var found).Should().BeTrue();
        found.Should().Be(replacement);
    }

    [Test]
    public void DocumentCache_MissingEntry_IsNotFound()
    {
        new ClearableDocumentCache(16).TryGetDocument("absent", out _).Should().BeFalse();
    }

    [Test]
    public void DocumentCache_Clear_EmptiesIt()
    {
        var cache = new ClearableDocumentCache(16);
        cache.TryAddDocument("id", Document("{ a }"));

        cache.Clear();

        cache.Count.Should().Be(0);
        cache.TryGetDocument("id", out _).Should().BeFalse();
    }

    [Test]
    public void DocumentCache_ExposesItsCapacity()
    {
        new ClearableDocumentCache(64).Capacity.Should().Be(64);
    }

    #endregion

    #region Bounding and retention

    [Test]
    public void Cache_StaysBounded_UnderSustainedWrites()
    {
        var cache = new ClearableDocumentCache(8);

        for (var i = 0; i < 500; i++)
            cache.TryAddDocument($"id-{i}", Document("{ a }"));

        // Two generations of at most `capacity` entries each.
        cache.Count.Should().BeLessThanOrEqualTo(16);
    }

    [Test]
    public void Cache_KeepsARepeatedlyUsedEntry_AcrossGenerationTurnover()
    {
        // A hot key must survive eviction pressure, or the cache would degrade into a
        // permanent miss for the operations a host actually runs.
        var cache = new ClearableDocumentCache(8);
        var hot = Document("{ hot }");
        cache.TryAddDocument("hot", hot);

        for (var i = 0; i < 200; i++)
        {
            cache.TryAddDocument($"cold-{i}", Document("{ a }"));
            cache.TryGetDocument("hot", out _);
        }

        cache.TryGetDocument("hot", out var found).Should().BeTrue();
        found.Should().Be(hot);
    }

    [Test]
    public void Cache_ConcurrentWrites_DoNotThrowOrExceedTheBound()
    {
        var cache = new ClearableDocumentCache(8);

        Parallel.For(
            0,
            2_000,
            i =>
            {
                cache.TryAddDocument($"id-{i}", Document("{ a }"));
                cache.TryGetDocument($"id-{i / 2}", out _);
            }
        );

        cache.Count.Should().BeLessThanOrEqualTo(16);
    }

    #endregion

    #region ClearablePreparedOperationCache

    [Test]
    public void PreparedOperationCache_MissingEntry_IsNotFound()
    {
        var cache = new ClearablePreparedOperationCache(16);

        cache.TryGetOperation("absent", out _).Should().BeFalse();
        cache.Count.Should().Be(0);
    }

    [Test]
    public void PreparedOperationCache_ExposesItsCapacity()
    {
        new ClearablePreparedOperationCache(32).Capacity.Should().Be(32);
    }

    [Test]
    public void PreparedOperationCache_Clear_EmptiesIt()
    {
        // Compiled operations cannot be constructed outside HotChocolate, so the storage
        // behaviour is covered end-to-end in HotChocolateCacheInvalidationTests; here we
        // pin that clearing an empty cache is safe and idempotent.
        var cache = new ClearablePreparedOperationCache(16);

        cache.Clear();
        cache.Clear();

        cache.Count.Should().Be(0);
    }

    #endregion
}
