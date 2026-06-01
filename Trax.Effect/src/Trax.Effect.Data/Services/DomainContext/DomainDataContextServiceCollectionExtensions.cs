using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Effect.Data.Services.DomainContext;

/// <summary>
/// Registration and schema-bootstrap helpers for <see cref="DomainDataContext{TSelf}"/>-derived
/// domain contexts.
/// </summary>
public static class DomainDataContextServiceCollectionExtensions
{
    /// <summary>
    /// Registers a domain data context behind its companion interface, using a pooled context factory
    /// plus a scoped resolver bound to <typeparamref name="TInterface"/>.
    /// </summary>
    /// <typeparam name="TInterface">The companion interface, e.g. <c>ICatalogDbContext</c>.</typeparam>
    /// <typeparam name="TContext">The concrete context, e.g. <c>CatalogDbContext</c>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureProvider">
    /// Configures the EF provider, e.g. <c>options =&gt; options.UseNpgsql(connectionString)</c>.
    /// </param>
    /// <remarks>
    /// Pooling keeps per-request allocation low; the scoped resolver hands application code a
    /// short-lived instance through the interface. This shape (factory + scoped resolver) avoids the
    /// duplicate-options registration error from combining <c>AddPooledDbContextFactory</c> with
    /// <c>AddDbContext</c> for the same type.
    /// </remarks>
    public static IServiceCollection AddDomainDataContext<TInterface, TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureProvider
    )
        where TContext : DbContext, TInterface
        where TInterface : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureProvider);

        services.AddPooledDbContextFactory<TContext>(configureProvider);
        services.AddScoped<TInterface>(sp =>
            sp.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext()
        );

        return services;
    }

    /// <summary>
    /// Idempotently creates the context's schema and tables at startup.
    /// </summary>
    /// <remarks>
    /// Domain tables typically share a database with the Trax framework tables, so <c>EnsureCreated</c>
    /// would see the database already populated and create nothing. Instead, on a relational provider
    /// this creates the default schema (when set) and runs the model's create script; on the in-memory
    /// provider it falls back to <c>EnsureCreated</c>. The create script has no <c>IF NOT EXISTS</c>,
    /// so a second run throws an "object already exists" <see cref="DbException"/> on the first
    /// statement: that is the steady state and is swallowed, leaving the existing tables intact. This
    /// is a convenience bootstrap; production apps should use real migrations.
    /// </remarks>
    public static async Task EnsureSchemaCreatedAsync<TContext>(
        this IServiceProvider services,
        CancellationToken cancellationToken = default
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        if (!db.Database.IsRelational())
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        var schema = db.Model.GetDefaultSchema();
        if (!string.IsNullOrEmpty(schema))
        {
            // A schema name cannot be a SQL parameter (it is an identifier, not a value). The value
            // is the context's own compile-time schema constant, never user input, and is
            // identifier-validated here, so the EF1002 interpolation warning does not apply.
            if (!IsSafeIdentifier(schema))
                throw new InvalidOperationException(
                    $"Schema name '{schema}' is not a valid SQL identifier. Use letters, digits, "
                        + "and underscores only, starting with a letter or underscore."
                );

#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync(
                $"CREATE SCHEMA IF NOT EXISTS \"{schema}\";",
                cancellationToken
            );
#pragma warning restore EF1002
        }

        try
        {
            var createScript = db.Database.GenerateCreateScript();
            await db.Database.ExecuteSqlRawAsync(createScript, cancellationToken);
        }
        catch (DbException)
        {
            // Tables already exist from a previous run. The bootstrap is idempotent by design.
        }
    }

    private static bool IsSafeIdentifier(string value)
    {
        if (value.Length == 0)
            return false;

        if (value[0] != '_' && !char.IsLetter(value[0]))
            return false;

        foreach (var c in value)
        {
            if (c != '_' && !char.IsLetterOrDigit(c))
                return false;
        }

        return true;
    }
}
