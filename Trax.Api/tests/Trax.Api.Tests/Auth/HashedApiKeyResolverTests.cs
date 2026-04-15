using System.Diagnostics;
using System.Security.Cryptography;
using FluentAssertions;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class HashedApiKeyResolverTests
{
    [Test]
    public async Task CorrectKey_Resolves_ToConfiguredPrincipal()
    {
        var entry = HashedApiKeyResolver.Entry.FromPlainKey(
            "secret-key-value",
            () => new TraxPrincipal("alice", "Alice", ["User"])
        );
        var resolver = new HashedApiKeyResolver([entry]);

        var result = await resolver.ResolveAsync("secret-key-value", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be("alice");
        result.DisplayName.Should().Be("Alice");
    }

    [Test]
    public async Task WrongKey_ReturnsNull()
    {
        var entry = HashedApiKeyResolver.Entry.FromPlainKey(
            "secret-key-value",
            () => new TraxPrincipal("alice", "Alice", ["User"])
        );
        var resolver = new HashedApiKeyResolver([entry]);

        var result = await resolver.ResolveAsync("wrong-key", CancellationToken.None);

        result.Should().BeNull();
    }

    [Test]
    public async Task EmptyKey_ReturnsNull_WithoutIteratingEntries()
    {
        var invoked = 0;
        var entry = new HashedApiKeyResolver.Entry(
            new byte[] { 1 },
            new byte[32],
            () =>
            {
                invoked++;
                return new TraxPrincipal("x", "x", []);
            }
        );
        var resolver = new HashedApiKeyResolver([entry]);

        var result = await resolver.ResolveAsync("", CancellationToken.None);

        result.Should().BeNull();
        invoked.Should().Be(0);
    }

    [Test]
    public async Task MultipleEntries_CorrectKey_ResolvesCorrectPrincipal()
    {
        var entries = new[]
        {
            HashedApiKeyResolver.Entry.FromPlainKey(
                "alice-key",
                () => new TraxPrincipal("alice", "Alice", ["User"])
            ),
            HashedApiKeyResolver.Entry.FromPlainKey(
                "bob-key",
                () => new TraxPrincipal("bob", "Bob", ["User"])
            ),
            HashedApiKeyResolver.Entry.FromPlainKey(
                "charlie-key",
                () => new TraxPrincipal("charlie", "Charlie", ["Admin"])
            ),
        };
        var resolver = new HashedApiKeyResolver(entries);

        var result = await resolver.ResolveAsync("bob-key", CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be("bob");
    }

    [Test]
    public async Task EachEntryUsesIndependentSalt_SamePlainKeyProducesDifferentHashes()
    {
        var e1 = HashedApiKeyResolver.Entry.FromPlainKey(
            "same",
            () => new TraxPrincipal("x", "x", [])
        );
        var e2 = HashedApiKeyResolver.Entry.FromPlainKey(
            "same",
            () => new TraxPrincipal("y", "y", [])
        );

        e1.Salt.Should().NotEqual(e2.Salt);
        e1.Sha256Hash.Should().NotEqual(e2.Sha256Hash);

        // Both resolve the same key to valid principals, not nulls.
        var resolver = new HashedApiKeyResolver([e1, e2]);
        var result = await resolver.ResolveAsync("same", CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Test]
    public void FromPlainKey_NullOrWhitespace_Throws()
    {
        Action a1 = () =>
            HashedApiKeyResolver.Entry.FromPlainKey("", () => new TraxPrincipal("x", "x", []));
        Action a2 = () =>
            HashedApiKeyResolver.Entry.FromPlainKey("   ", () => new TraxPrincipal("x", "x", []));

        a1.Should().Throw<ArgumentException>();
        a2.Should().Throw<ArgumentException>();
    }

    [Test]
    public async Task PrincipalFactory_OnlyInvoked_OnMatch()
    {
        var invokedGood = 0;
        var invokedBad = 0;
        var goodEntry = HashedApiKeyResolver.Entry.FromPlainKey(
            "good",
            () =>
            {
                invokedGood++;
                return new TraxPrincipal("g", "g", []);
            }
        );
        var badEntry = HashedApiKeyResolver.Entry.FromPlainKey(
            "bad",
            () =>
            {
                invokedBad++;
                return new TraxPrincipal("b", "b", []);
            }
        );
        var resolver = new HashedApiKeyResolver([goodEntry, badEntry]);

        await resolver.ResolveAsync("good", CancellationToken.None);

        invokedGood.Should().Be(1);
        invokedBad.Should().Be(0);
    }

    [Test]
    [Category("Timing")]
    [Explicit(
        "Timing-sensitive. Exercises constant-time behavior but is flaky under noisy CI. "
            + "Run manually to validate."
    )]
    public async Task Timing_SimilarAndDifferentKeys_BoundedVariance()
    {
        // Build a large entry set so timing differences are amplified. If the resolver
        // short-circuited on the first match (or used non-constant-time equality), we'd
        // expect keys that match the first vs. last vs. no entry to diverge measurably.
        const int count = 50;
        var entries = Enumerable
            .Range(0, count)
            .Select(i =>
                HashedApiKeyResolver.Entry.FromPlainKey(
                    $"key-{i}",
                    () => new TraxPrincipal($"u{i}", $"U{i}", [])
                )
            )
            .ToArray();
        var resolver = new HashedApiKeyResolver(entries);

        var sw = new Stopwatch();

        async Task<double> AverageMicros(string input, int iters = 2000)
        {
            // Warm-up
            for (var i = 0; i < 200; i++)
                await resolver.ResolveAsync(input, CancellationToken.None);

            sw.Restart();
            for (var i = 0; i < iters; i++)
                await resolver.ResolveAsync(input, CancellationToken.None);
            sw.Stop();
            return sw.Elapsed.TotalMicroseconds / iters;
        }

        var first = await AverageMicros("key-0");
        var last = await AverageMicros("key-49");
        var miss = await AverageMicros("definitely-not-a-real-key");

        // Allow a generous 30% band — constant-time is about structural behavior,
        // not cycle-exact. If we regressed to short-circuit, "first" would be
        // several-fold faster than "last" and "miss".
        var avg = (first + last + miss) / 3;
        Math.Abs(first - avg).Should().BeLessThan(avg * 0.30);
        Math.Abs(last - avg).Should().BeLessThan(avg * 0.30);
        Math.Abs(miss - avg).Should().BeLessThan(avg * 0.30);
    }

    [Test]
    public void Hash_DifferentSaltsWithSameInput_ProduceDifferentHashes()
    {
        var salt1 = RandomNumberGenerator.GetBytes(16);
        var salt2 = RandomNumberGenerator.GetBytes(16);
        var input = "same-clear-text";

        var h1 = HashedApiKeyResolver.Hash(salt1, input);
        var h2 = HashedApiKeyResolver.Hash(salt2, input);

        h1.Should().NotEqual(h2);
    }
}
