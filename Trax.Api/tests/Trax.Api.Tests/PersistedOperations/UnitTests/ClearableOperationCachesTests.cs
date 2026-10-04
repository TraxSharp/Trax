using AwesomeAssertions;
using HotChocolate.Language;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.Tests.PersistedOperations.Fixtures;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// Trax substitutes these for HotChocolate's own caches purely so a persisted-operation
/// upsert can drop what it cached. They still have to behave like caches: hold entries,
/// stay bounded, and keep a live working set across generation turnover.
/// </summary>
[TestFixture]
public class ClearableOperationCachesTests
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    private PersistedOperationCacheGeneration _generation = null!;
    private ManualClock _clock = null!;

    [SetUp]
    public void SetUp()
    {
        _generation = new PersistedOperationCacheGeneration();
        _clock = new ManualClock();
        // Entries are added only inside a request scope, as HotChocolate's middleware does.
        PersistedOperationRequestScope.Begin(_generation, carriesDocument: false);
    }

    [TearDown]
    public void TearDown() => PersistedOperationRequestScope.End();

    private ClearableDocumentCache DocumentCache(int capacity) =>
        new(capacity, _generation, _clock, MaxAge);

    private ClearablePreparedOperationCache OperationCache(int capacity) =>
        new(capacity, _generation, _clock, MaxAge);

    private static CachedDocument Document(string text) =>
        new(Utf8GraphQLParser.Parse(text), default, isPersisted: true);

    #region ClearableDocumentCache

    [Test]
    public void DocumentCache_StoresAndReturnsAnEntry()
    {
        var cache = DocumentCache(16);
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
        var cache = DocumentCache(16);
        cache.TryAddDocument("id", Document("{ a }"));

        var replacement = Document("{ b }");
        cache.TryAddDocument("id", replacement);

        cache.TryGetDocument("id", out var found).Should().BeTrue();
        found.Should().Be(replacement);
    }

    [Test]
    public void DocumentCache_AnInlineDocument_IsCachedOnlyUnderItsOwnHash()
    {
        var cache = DocumentCache(16);
        var hash = new OperationDocumentHash("abc123", "MD5", HashFormat.Hex);
        var inline = new CachedDocument(Utf8GraphQLParser.Parse("{ a }"), hash, isPersisted: false);

        cache.TryAddDocument("SomeOperatorId", inline);
        cache.TryAddDocument("abc123", inline);

        cache.TryGetDocument("SomeOperatorId", out _).Should().BeFalse();
        cache.TryGetDocument("abc123", out _).Should().BeTrue();
    }

    [Test]
    public void DocumentCache_MissingEntry_IsNotFound()
    {
        DocumentCache(16).TryGetDocument("absent", out _).Should().BeFalse();
    }

    [Test]
    public void DocumentCache_Clear_EmptiesIt()
    {
        var cache = DocumentCache(16);
        cache.TryAddDocument("id", Document("{ a }"));

        cache.Clear();

        cache.Count.Should().Be(0);
        cache.TryGetDocument("id", out _).Should().BeFalse();
    }

    [Test]
    public void DocumentCache_ExposesItsCapacity()
    {
        DocumentCache(64).Capacity.Should().Be(64);
    }

    #endregion

    #region Bounding and retention

    [Test]
    public void Cache_StaysBounded_UnderSustainedWrites()
    {
        var cache = DocumentCache(8);

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
        var cache = DocumentCache(8);
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
        var cache = DocumentCache(8);

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

    [Test]
    public void Cache_ConcurrentWrites_NeverHoldMoreThanTwiceTheCapacity_AtAnyMoment()
    {
        // The bound is a guarantee, not an eventual property: sample it while writers race.
        for (var round = 0; round < 50; round++)
        {
            var cache = DocumentCache(8);
            var document = Document("{ a }");
            var largest = 0;
            using var done = new CancellationTokenSource();
            var sampler = Task.Run(() =>
            {
                while (!done.IsCancellationRequested)
                    largest = Math.Max(largest, cache.Count);
            });

            Parallel.For(
                0,
                2_000,
                new ParallelOptions { MaxDegreeOfParallelism = 8 },
                i =>
                {
                    cache.TryAddDocument($"id-{round}-{i}", document);
                    cache.TryGetDocument($"id-{round}-{i / 2}", out _);
                }
            );
            done.Cancel();
            sampler.Wait();

            Math.Max(largest, cache.Count).Should().BeLessThanOrEqualTo(16, $"round {round}");
        }
    }

    #endregion

    #region Generation and maximum age

    [Test]
    public void AnEntry_IsNotServedAfterTheGenerationAdvances()
    {
        var cache = DocumentCache(16);
        cache.TryAddDocument("id", Document("{ a }"));

        _generation.Advance();

        cache.TryGetDocument("id", out _).Should().BeFalse();
    }

    [Test]
    public void AnEntryAddedByARequestThatStartedBeforeAChange_IsNeverServed()
    {
        // The request started (scope opened in SetUp), a change landed, then the request
        // finished and wrote what it had read.
        var cache = DocumentCache(16);
        _generation.Advance();

        cache.TryAddDocument("id", Document("{ old }"));

        cache.TryGetDocument("id", out _).Should().BeFalse();
    }

    [Test]
    public void AnEntryAddedOutsideARequest_IsNotCached()
    {
        var cache = DocumentCache(16);
        PersistedOperationRequestScope.End();

        cache.TryAddDocument("id", Document("{ a }"));

        cache.Count.Should().Be(0);
    }

    [Test]
    public void AnEntry_IsServedUntilItsMaximumAge_AndNotAfter()
    {
        var cache = DocumentCache(16);
        cache.TryAddDocument("id", Document("{ a }"));

        _clock.Advance(MaxAge - TimeSpan.FromTicks(1));
        cache.TryGetDocument("id", out _).Should().BeTrue();

        _clock.Advance(TimeSpan.FromTicks(1));
        cache.TryGetDocument("id", out _).Should().BeFalse();
    }

    [Test]
    public void AnEntrysAge_CountsFromWhenItsDocumentWasRead()
    {
        // A document read from the store a minute ago and compiled now is a minute old.
        var readAt = _clock.GetTimestamp();
        _clock.Advance(TimeSpan.FromMinutes(1));
        var scope = PersistedOperationRequestScope.Begin(_generation, carriesDocument: false);
        scope.NoteSource(readAt);
        var cache = DocumentCache(16);

        cache.TryAddDocument("id", Document("{ a }"));
        _clock.Advance(MaxAge - TimeSpan.FromMinutes(1));

        cache.TryGetDocument("id", out _).Should().BeFalse();
    }

    [Test]
    public void ACacheHit_TellsTheRequestHowOldItsDocumentIs()
    {
        var cache = DocumentCache(16);
        var addedAt = _clock.GetTimestamp();
        cache.TryAddDocument("id", Document("{ a }"));
        _clock.Advance(TimeSpan.FromSeconds(30));
        var scope = PersistedOperationRequestScope.Begin(_generation, carriesDocument: false);

        cache.TryGetDocument("id", out _).Should().BeTrue();

        scope.SourceTimestamp.Should().Be(addedAt);
    }

    [Test]
    public void AScopeForAnotherNode_IsIgnored()
    {
        // Two hosts in one process each have their own generation; a scope belongs to one.
        var cache = DocumentCache(16);
        PersistedOperationRequestScope.Begin(
            new PersistedOperationCacheGeneration(),
            carriesDocument: false
        );

        cache.TryAddDocument("id", Document("{ a }"));

        cache.Count.Should().Be(0);
    }

    [Test]
    public void AScope_KeepsTheOldestSourceItWasGiven()
    {
        var scope = PersistedOperationRequestScope.Begin(_generation, carriesDocument: false);
        scope.SourceTimestamp.Should().BeNull();

        scope.NoteSource(50);
        scope.NoteSource(80);
        scope.NoteSource(20);

        scope.SourceTimestamp.Should().Be(20);
    }

    #endregion

    #region ClearablePreparedOperationCache

    [Test]
    public void PreparedOperationCache_MissingEntry_IsNotFound()
    {
        var cache = OperationCache(16);

        cache.TryGetOperation("absent", out _).Should().BeFalse();
        cache.Count.Should().Be(0);
    }

    [Test]
    public void PreparedOperationCache_ExposesItsCapacity()
    {
        OperationCache(32).Capacity.Should().Be(32);
    }

    [Test]
    public void PreparedOperationCache_Clear_EmptiesIt()
    {
        // Compiled operations cannot be constructed outside HotChocolate, so the storage
        // behaviour is covered end-to-end in HotChocolateCacheInvalidationTests; here we
        // pin that clearing an empty cache is safe and idempotent.
        var cache = OperationCache(16);

        cache.Clear();
        cache.Clear();

        cache.Count.Should().Be(0);
    }

    #endregion
}
