using AwesomeAssertions;
using Trax.Scheduler.Services.RequestSigning;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// The per-process nonce store a single-instance runner opts into. Enforces
/// <c>docs/adr/0009-a-runner-shares-its-accepted-nonces-through-the-database.md</c>.
/// </summary>
[Property("adr", "docs/adr/0009-a-runner-shares-its-accepted-nonces-through-the-database.md")]
[TestFixture]
public class InMemoryNonceStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Test]
    public async Task ANonce_IsRecordedOnce()
    {
        var store = new InMemoryNonceStore();

        (await store.TryRecordAsync("a", Now.AddMinutes(5), Now, CancellationToken.None))
            .Should()
            .BeTrue();
        (await store.TryRecordAsync("a", Now.AddMinutes(5), Now, CancellationToken.None))
            .Should()
            .BeFalse();
    }

    [Test]
    public async Task AnExpiredRecord_IsTakenOver()
    {
        var store = new InMemoryNonceStore();
        await store.TryRecordAsync("a", Now.AddSeconds(-1), Now.AddMinutes(-5), default);

        (await store.TryRecordAsync("a", Now.AddMinutes(5), Now, CancellationToken.None))
            .Should()
            .BeTrue();
    }

    [Test]
    public async Task ARecordExpiringNow_StillRefuses()
    {
        // The verifier accepts a timestamp exactly the skew away, so the record must still hold then.
        var store = new InMemoryNonceStore();
        await store.TryRecordAsync("a", Now, Now.AddMinutes(-5), default);

        (await store.TryRecordAsync("a", Now.AddMinutes(5), Now, CancellationToken.None))
            .Should()
            .BeFalse();
    }

    [Test]
    public async Task ConcurrentRecordsOfOneNonce_OneWins()
    {
        var store = new InMemoryNonceStore();

        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 64)
                .Select(_ =>
                    Task.Run(async () =>
                        await store.TryRecordAsync(
                            "a",
                            Now.AddMinutes(5),
                            Now,
                            CancellationToken.None
                        )
                    )
                )
        );

        results
            .Count(r => r)
            .Should()
            .Be(
                1,
                "a nonce is recorded once (see docs/adr/0009-a-runner-shares-its-accepted-nonces-through-the-database.md)"
            );
    }
}
