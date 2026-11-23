using Trax.Core.Train;

namespace Trax.Core.Monad;

/// <summary>One declared branch: its name and its chain.</summary>
internal sealed record DeclaredBranch<TInput, TReturn>(
    string Name,
    Func<MonadTask<TInput, TReturn>, MonadTask<TInput, TReturn>> Body
)
{
    /// <summary>
    /// True when the branch's name is refused (empty, repeated, or holding a separator), so its
    /// steps could not have ids of their own and it is left out of the recorded chain.
    /// </summary>
    public bool Refused { get; init; }
}
