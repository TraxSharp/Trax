using Microsoft.Extensions.Time.Testing;
using Trax.Scheduler.Sandbox.Launcher.Core;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>A captured request starts no second sandbox: its nonce is remembered for longer than it stays fresh.</summary>
[TestFixture]
public class NonceCacheTests
{
    [Test]
    public void TryAccept_SameNonceTwice_IsRefusedTheSecondTime()
    {
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var cache = new NonceCache(time);

        cache.TryAccept("aa", 1_700_000_000).Should().BeTrue();
        cache.TryAccept("aa", 1_700_000_000).Should().BeFalse();
    }

    [Test]
    public void TryAccept_AfterTheWindow_StillRefusedWhileFresh()
    {
        var time = new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var cache = new NonceCache(time);
        cache.TryAccept("aa", 1_700_000_000);

        time.Advance(NonceCache.Window);

        cache.IsFresh(1_700_000_000).Should().BeTrue("the edge of the window is still fresh");
        cache
            .TryAccept("aa", 1_700_000_000)
            .Should()
            .BeFalse("so its nonce must still be remembered");
    }

    [TestCase(-61, false)]
    [TestCase(-60, true)]
    [TestCase(0, true)]
    [TestCase(60, true)]
    [TestCase(61, false)]
    public void IsFresh_ByDistanceFromNow(int seconds, bool fresh)
    {
        var cache = new NonceCache(
            new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000))
        );

        cache.IsFresh(1_700_000_000 + seconds).Should().Be(fresh);
    }
}
