using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

/// <summary>
/// Prepares a branch's own dependency injection scope from the run's, for state a scoped service
/// holds that a fresh scope would not have: a tenant, a unit of work, anything set imperatively
/// on a scoped object rather than read from an ambient context.
/// </summary>
/// <remarks>
/// Every registered initializer runs, in registration order, before the branch's first step. One
/// that throws fails the branch: a branch that cannot see what the run sees does not run.
/// </remarks>
public interface IBranchScopeInitializer
{
    /// <summary>Copies what the branch needs from the run's scope into the branch's.</summary>
    /// <param name="run">The run's scope.</param>
    /// <param name="branch">The branch's new scope.</param>
    /// <param name="cancellationToken">The branch's token.</param>
    Task Initialize(
        IServiceProvider run,
        IServiceProvider branch,
        CancellationToken cancellationToken
    );
}
