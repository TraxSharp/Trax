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
