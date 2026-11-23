using Microsoft.Extensions.Time.Testing;

namespace Trax.Samples.ChatService.Tests.Fixtures;

/// <summary>The instant the tests run at, and a clock that stays there.</summary>
public static class TestClock
{
    public static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public static FakeTimeProvider Fixed() => new(Now);
}
