using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Storage;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

[TestFixture]
public class PersistedOperationCacheTests
{
    [TestFixture]
    public class NoOp
    {
        [Test]
        public void TryGet_AfterSet_ReturnsNull()
        {
            var cache = new NoOpPersistedOperationCache();
            cache.Set(null, "id1", "doc");
            cache.TryGet(null, "id1").Should().BeNull();
        }

        [Test]
        public void Invalidate_DoesNotThrow()
        {
            var cache = new NoOpPersistedOperationCache();
            Action act = () => cache.Invalidate(null, "id1");
            act.Should().NotThrow();
        }
    }

    [TestFixture]
    public class InMemory
    {
        private static InMemoryPersistedOperationCache Build(TimeSpan? ttl = null) =>
            new(
                new MemoryCache(new MemoryCacheOptions()),
                new PersistedOperationsOptions { CacheTtl = ttl ?? TimeSpan.FromMinutes(15) }
            );

        [Test]
        public void TryGet_BeforeSet_ReturnsNull()
        {
            var cache = Build();
            cache.TryGet(null, "missing").Should().BeNull();
        }

        [Test]
        public void TryGet_AfterSet_ReturnsDocument()
        {
            var cache = Build();
            cache.Set(null, "id1", "the-document");
            cache.TryGet(null, "id1").Should().Be("the-document");
        }

        [Test]
        public void Set_OverwritesExistingEntry()
        {
            var cache = Build();
            cache.Set(null, "id1", "v1");
            cache.Set(null, "id1", "v2");
            cache.TryGet(null, "id1").Should().Be("v2");
        }

        [Test]
        public void Invalidate_RemovesSingleEntry()
        {
            var cache = Build();
            cache.Set(null, "id1", "doc");
            cache.Set(null, "id2", "doc2");
            cache.Invalidate(null, "id1");
            cache.TryGet(null, "id1").Should().BeNull();
            cache.TryGet(null, "id2").Should().Be("doc2");
        }

        [Test]
        public void Set_DifferentTenants_AreIsolated()
        {
            var cache = Build();
            cache.Set(tenantKey: "a", "id1", "tenant-a-doc");
            cache.Set(tenantKey: "b", "id1", "tenant-b-doc");
            cache.Set(tenantKey: null, "id1", "default-doc");

            cache.TryGet("a", "id1").Should().Be("tenant-a-doc");
            cache.TryGet("b", "id1").Should().Be("tenant-b-doc");
            cache.TryGet(null, "id1").Should().Be("default-doc");
        }

        [Test]
        public void Invalidate_OneTenant_DoesNotAffectOthers()
        {
            var cache = Build();
            cache.Set("a", "shared", "a-doc");
            cache.Set("b", "shared", "b-doc");

            cache.Invalidate("a", "shared");

            cache.TryGet("a", "shared").Should().BeNull();
            cache.TryGet("b", "shared").Should().Be("b-doc");
        }

        [Test]
        public void Set_RespectsTtl_Deterministically()
        {
            // MemoryCacheOptions.Clock lets us drive expiration without sleeping.
            var clock = new TestClock(DateTimeOffset.UtcNow);
            var memCache = new MemoryCache(
                new MemoryCacheOptions { Clock = clock, ExpirationScanFrequency = TimeSpan.Zero }
            );
            var cache = new InMemoryPersistedOperationCache(
                memCache,
                new PersistedOperationsOptions { CacheTtl = TimeSpan.FromMinutes(15) }
            );

            cache.Set(null, "id1", "doc");
            cache.TryGet(null, "id1").Should().Be("doc", "set-then-get must hit");

            // Advance past the TTL boundary.
            clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

            cache.TryGet(null, "id1").Should().BeNull("entry should have expired by TTL");
        }

        private sealed class TestClock : Microsoft.Extensions.Internal.ISystemClock
        {
            public DateTimeOffset UtcNow { get; private set; }

            public TestClock(DateTimeOffset start) => UtcNow = start;

            public void Advance(TimeSpan delta) => UtcNow += delta;
        }

        [Test]
        public void Constructor_NullCache_Throws()
        {
            Action act = () =>
                _ = new InMemoryPersistedOperationCache(null!, new PersistedOperationsOptions());
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void Constructor_NullOptions_Throws()
        {
            Action act = () =>
                _ = new InMemoryPersistedOperationCache(
                    new MemoryCache(new MemoryCacheOptions()),
                    null!
                );
            act.Should().Throw<ArgumentNullException>();
        }
    }
}
