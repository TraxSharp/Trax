// Linked into every test project of this folder by tests/Directory.Build.props.
namespace Trax.Testing;

/// <summary>A clock that always reads the instant it was made with.</summary>
[global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
    global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All
)]
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => now;
}
