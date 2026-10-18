using System.Collections;
using System.Diagnostics;

namespace Trax.Core.Monad;

/// <summary>
/// What tells one <c>Parallel</c> branch's logs and traces from another's: each branch runs under a
/// logger scope and an <see cref="Activity"/> that carry its branch path (<c>Parallel#0/cocitation</c>).
/// </summary>
public static class BranchDiagnostics
{
    /// <summary>The name of the <see cref="ActivitySource"/> Trax.Core starts its activities from.</summary>
    public const string SourceName = "Trax.Core";

    /// <summary>The tag, and the logger-scope key, that holds a branch's path.</summary>
    public const string BranchPathKey = "trax.branch";

    internal static readonly ActivitySource Source = new(SourceName);
}

/// <summary>
/// The logger scope a branch runs under. A provider that reads scopes as key/value pairs gets
/// <see cref="BranchDiagnostics.BranchPathKey"/>; one that prints them gets <c>Parallel branch …</c>.
/// </summary>
internal sealed class BranchScope(string path) : IReadOnlyList<KeyValuePair<string, object?>>
{
    public string Path { get; } = path;

    public int Count => 1;

    public KeyValuePair<string, object?> this[int index] =>
        index == 0
            ? new(BranchDiagnostics.BranchPathKey, Path)
            : throw new ArgumentOutOfRangeException(nameof(index));

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        yield return this[0];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"Parallel branch {Path}";
}
