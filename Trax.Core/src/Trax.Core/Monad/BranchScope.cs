using System.Collections;
using System.Diagnostics;

namespace Trax.Core.Monad;

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
