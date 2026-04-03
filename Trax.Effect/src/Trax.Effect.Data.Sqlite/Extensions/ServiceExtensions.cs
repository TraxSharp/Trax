using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Data.Sqlite.Services.SqlDialect;
using Trax.Effect.Data.Sqlite.Services.SqliteContextFactory;
using Trax.Effect.Data.Sqlite.Utils;
using Trax.Effect.Extensions;
using TraxEffectBuilder = Trax.Effect.Configuration.TraxEffectBuilder.TraxEffectBuilder;

namespace Trax.Effect.Data.Sqlite.Extensions;

/// <summary>
/// Extension methods for configuring Trax.Effect with a SQLite data provider.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds SQLite database support to the Trax.Effect system.
    /// </summary>
    /// <param name="configurationBuilder">The effect configuration builder.</param>
    /// <param name="connectionString">SQLite connection string (e.g., "Data Source=trax.db").</param>
    /// <returns>The promoted builder for chaining data-dependent extensions.</returns>
    /// <remarks>
    /// SQLite is a single-server provider: it supports full scheduling, persistence, and
    /// transactions but does not support multi-server coordination (advisory locks,
    /// FOR UPDATE SKIP LOCKED). Use Postgres for multi-server deployments.
    ///
    /// WAL mode is enabled automatically for better read/write concurrency.
    /// </remarks>
    public static TraxEffectBuilderWithData UseSqlite(
        this TraxEffectBuilder configurationBuilder,
        string connectionString
    )
    {
        // Run migrations unless explicitly skipped
        if (!configurationBuilder.MigrationsDisabled)
            DatabaseMigrator.Migrate(connectionString).Wait();

        // Enable WAL mode for better concurrent read/write performance
        EnableWalMode(connectionString);

        // Register the DbContextFactory
        configurationBuilder.ServiceCollection.AddDbContextFactory<Services.SqliteContext.SqliteContext>(
            (_, options) =>
            {
                options.UseSqlite(connectionString).UseLoggerFactory(new NullLoggerFactory());
            }
        );

        // Register SqliteContext as scoped IDataContext (created from factory)
        configurationBuilder.ServiceCollection.AddScoped<
            IDataContext,
            Services.SqliteContext.SqliteContext
        >(sp =>
            sp.GetRequiredService<IDbContextFactory<Services.SqliteContext.SqliteContext>>()
                .CreateDbContext()
        );

        // Enable data context logging
        configurationBuilder.DataContextLoggingEffectEnabled = true;

        // Register the context provider factory
        configurationBuilder.AddEffect<IDataContextProviderFactory, SqliteContextProviderFactory>(
            toggleable: false
        );

        // Register the SQL dialect
        configurationBuilder.ServiceCollection.AddSingleton<ISqlDialect, SqliteSqlDialect>();

        configurationBuilder.HasDatabaseProvider = true;
        configurationBuilder.HasDataProvider = true;

        var promoted =
            configurationBuilder as TraxEffectBuilderWithData
            ?? new TraxEffectBuilderWithData(configurationBuilder);
        promoted.DataContextLoggingEffectEnabled = true;
        return promoted;
    }

    private static void EnableWalMode(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();
    }
}
