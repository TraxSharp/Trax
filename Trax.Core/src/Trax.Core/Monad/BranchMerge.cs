using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

/// <summary>
/// What a <c>Parallel</c> step's join puts into the run's Memory: each type exactly one branch
/// added or changed. Apart from the monad so the law it keeps, that the order the branches are
/// declared in cannot change the result, can be checked on its own.
/// </summary>
internal static class BranchMerge
{
    /// <summary>
    /// The values to put into the run's Memory. A type two branches both added is left out:
    /// neither value is the run's, and the startup check refuses the declared collisions, so only
    /// an undeclared one (an interface of a tuple element) reaches here.
    /// </summary>
    /// <param name="fork">The run's Memory as it was when the branches forked.</param>
    /// <param name="branches">Each branch's Memory as it ended.</param>
    public static Dictionary<Type, object> Added(
        IReadOnlyDictionary<Type, object> fork,
        IEnumerable<IReadOnlyDictionary<Type, object>> branches
    )
    {
        var added = new Dictionary<Type, object?>();

        foreach (var branch in branches)
        foreach (var (type, value) in branch)
        {
            if (type == typeof(IServiceProvider) || !ChainVerification.Merged(type))
                continue;

            if (fork.TryGetValue(type, out var before) && ReferenceEquals(before, value))
                continue;

            // Seen in an earlier branch: a collision, kept as a marker so a third does not win.
            added[type] = added.ContainsKey(type) ? null : value;
        }

        return added
            .Where(a => a.Value is not null)
            .ToDictionary(a => a.Key, a => a.Value.NotNull());
    }
}
