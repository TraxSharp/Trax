// Linked into every test project of this folder by tests/Directory.Build.props.
namespace Trax.Testing;

/// <summary>
/// The instant a test stamps the rows and messages it builds with when nothing it tests reads the
/// clock, so its result does not depend on when it runs, and a clock that always reads it.
/// </summary>
[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
    System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All
)]
internal static class TestTime
{
    /// <summary>Noon UTC on the first of January 2026.</summary>
    public static readonly DateTimeOffset Instant = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary><see cref="Instant"/> as a UTC <see cref="DateTime"/>.</summary>
    public static DateTime UtcDateTime => Instant.UtcDateTime;

    /// <summary>A clock that always reads <see cref="Instant"/>.</summary>
    public static TimeProvider Clock { get; } = new FixedClock(Instant);
}
