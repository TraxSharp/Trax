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

internal sealed class DeployMigration<TContext> : IDeployMigration
    where TContext : DbContext
{
    public string ContextName => typeof(TContext).Name;

    public async Task<string> RunAsync(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetService<IDbContextFactory<TContext>>();
        if (factory is null)
            throw new InvalidOperationException(
                $"MigrateOnDeploy<{ContextName}>() names a context with no IDbContextFactory<{ContextName}> "
                    + $"registered. Register it with AddDbContextFactory<{ContextName}>(...) or "
                    + "AddDomainDataContext<TInterface, TContext>(...)."
            );

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        // A context with EF migrations is migrated by EF, which holds its own lock while it does. One without them
        // is bootstrapped the way it would be at startup: its missing tables are created, by name (effect/0002).
        if (db.Database.IsRelational() && db.Database.GetMigrations().Any())
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            await db.Database.MigrateAsync(cancellationToken);
            return pending.Count == 0 ? "up to date" : $"applied {string.Join(", ", pending)}";
        }

        await services.EnsureSchemaCreatedAsync<TContext>(cancellationToken);
        return "missing tables created (no EF migrations in this context)";
    }
}
