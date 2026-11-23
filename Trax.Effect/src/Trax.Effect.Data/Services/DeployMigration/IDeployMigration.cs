using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DomainContext;

namespace Trax.Effect.Data.Services.DeployMigration;

/// <summary>
/// One of the host's own contexts that a process started with <c>--trax-migrate</c> brings up to date, registered by
/// <c>MigrateOnDeploy&lt;TContext&gt;()</c>.
/// </summary>
internal interface IDeployMigration
{
    /// <summary>The context's type name, for the log.</summary>
    string ContextName { get; }

    /// <summary>
    /// Applies the context's pending migrations, or creates its missing tables when it has none, and returns what it
    /// did.
    /// </summary>
    Task<string> RunAsync(IServiceProvider services, CancellationToken cancellationToken);
}
