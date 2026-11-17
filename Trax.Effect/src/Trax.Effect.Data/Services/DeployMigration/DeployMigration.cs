using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DomainContext;

namespace Trax.Effect.Data.Services.DeployMigration;

internal sealed class DeployMigration<TContext> : IDeployMigration
    where TContext : DbContext
{
    public string ContextName => typeof(TContext).Name;

    public async Task<string> RunAsync(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var scope = services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var factory = scope.ServiceProvider.GetService<IDbContextFactory<TContext>>();
        if (factory is null)
            throw new InvalidOperationException(
                $"MigrateOnDeploy<{ContextName}>() names a context with no IDbContextFactory<{ContextName}> "
                    + $"registered. Register it with AddDbContextFactory<{ContextName}>(...) or "
                    + "AddDomainDataContext<TInterface, TContext>(...)."
            );

        var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var dbDisposal = db.ConfigureAwait(false);

        // A context with EF migrations is migrated by EF, which holds its own lock while it does. One without them
        // is bootstrapped the way it would be at startup: its missing tables are created, by name (effect/0002).
        if (db.Database.IsRelational() && db.Database.GetMigrations().Any())
        {
            var pending = (
                await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)
            ).ToList();
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            return pending.Count == 0 ? "up to date" : $"applied {string.Join(", ", pending)}";
        }

        await services.EnsureSchemaCreatedAsync<TContext>(cancellationToken).ConfigureAwait(false);
        return "missing tables created (no EF migrations in this context)";
    }
}
