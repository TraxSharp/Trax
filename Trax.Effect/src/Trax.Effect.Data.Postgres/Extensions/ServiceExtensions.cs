using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.Enums;
using Trax.Effect.Data.Postgres.Services.PostgresContext;
using Trax.Effect.Data.Postgres.Services.PostgresContextFactory;
using Trax.Effect.Data.Postgres.Utils;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.DataContextLoggingProvider;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models;
using TraxEffectBuilder = Trax.Effect.Configuration.TraxEffectBuilder.TraxEffectBuilder;

namespace Trax.Effect.Data.Postgres.Extensions;

/// <summary>
/// Provides extension methods for configuring Trax.Effect.Data.Postgres services in the dependency injection container.
/// </summary>
/// <remarks>
/// The ServiceExtensions class contains utility methods that simplify the registration
/// of Trax.Effect.Data.Postgres services with the dependency injection system.
///
/// These extensions enable:
/// 1. Easy configuration of PostgreSQL database contexts
/// 2. Automatic database migration
/// 3. Integration with the Trax.Effect configuration system
///
/// By using these extensions, applications can easily configure and use the
/// Trax.Effect.Data.Postgres system with minimal boilerplate code.
/// </remarks>
public static class ServiceExtensions
{
    /// <summary>
    /// Disables the automatic database migration that normally runs inside <see cref="UsePostgres"/>.
    /// When called before <c>UsePostgres()</c>, the schema migration step is skipped entirely.
    /// </summary>
    /// <remarks>
    /// Use this in AWS Lambda runners, serverless functions, or any environment where database
    /// migrations are managed externally (e.g., CI/CD pipelines, a dedicated migration Lambda,
    /// or the API/scheduler process on startup).
    ///
    /// Migrations can be run separately by calling <see cref="DatabaseMigrator.Migrate"/> directly:
    /// <code>
    /// await DatabaseMigrator.Migrate(connectionString);
    /// </code>
    /// </remarks>
    /// <param name="builder">The effect builder.</param>
    /// <returns>The effect builder for chaining.</returns>
    public static TraxEffectBuilder SkipMigrations(this TraxEffectBuilder builder)
    {
        builder.MigrationsDisabled = true;
        return builder;
    }

    /// <summary>
    /// Adds PostgreSQL database support to the Trax.Effect system.
    /// </summary>
    /// <param name="configurationBuilder">The Trax.Core effect configuration builder</param>
    /// <param name="connectionString">The connection string to the PostgreSQL database</param>
    /// <returns>The configuration builder for method chaining</returns>
    /// <remarks>
    /// This method configures the Trax.Effect system to use PostgreSQL for train metadata persistence.
    /// It performs the following steps:
    ///
    /// 1. Migrates the database schema to the latest version using the DatabaseMigrator
    /// 2. Creates a data source with the necessary enum mappings
    /// 3. Registers a DbContextFactory for creating PostgresContext instances
    /// 4. Enables data context logging
    /// 5. Registers the PostgresContextProviderFactory as an IDataContextProviderFactory
    ///
    /// The PostgreSQL implementation is suitable for production environments where
    /// persistent storage and advanced database features are required.
    ///
    /// Example usage:
    /// ```csharp
    /// services.AddTrax(trax => trax
    ///     .AddEffects(effects => effects.UsePostgres("Host=localhost;Database=trax;Username=postgres;Password=password"))
    /// );
    /// ```
    /// </remarks>
    public static TraxEffectBuilderWithData UsePostgres(
        this TraxEffectBuilder configurationBuilder,
        string connectionString
    )
    {
        // Migrate the database schema to the latest version (unless explicitly skipped)
        if (!configurationBuilder.MigrationsDisabled)
            DatabaseMigrator.Migrate(connectionString).Wait();

        // Create a data source with enum mappings and register for disposal on shutdown
        var dataSource = ModelBuilderExtensions.BuildDataSource(connectionString);
        configurationBuilder.ServiceCollection.AddSingleton(dataSource);

        // Register the DbContextFactory
        configurationBuilder.ServiceCollection.AddDbContextFactory<PostgresContext>(
            (_, options) =>
            {
                options
                    .UseNpgsql(
                        dataSource,
                        o =>
                        {
                            o.MapEnum<TrainState>("train_state", "trax");
                            o.MapEnum<LogLevel>("log_level", "trax");
                            o.MapEnum<ScheduleType>("schedule_type", "trax");
                            o.MapEnum<DeadLetterStatus>("dead_letter_status", "trax");
                            o.MapEnum<WorkQueueStatus>("work_queue_status", "trax");
                            o.MapEnum<MisfirePolicy>("misfire_policy", "trax");
                        }
                    )
                    .UseLoggerFactory(new NullLoggerFactory())
                    .ConfigureWarnings(x => x.Log(CoreEventId.ManyServiceProvidersCreatedWarning));
            }
        );

        // Register PostgresContext directly for injection (created from the factory)
        configurationBuilder.ServiceCollection.AddScoped<IDataContext, PostgresContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<PostgresContext>>().CreateDbContext()
        );

        // Enable data context logging
        configurationBuilder.DataContextLoggingEffectEnabled = true;

        // Register the PostgresContextProviderFactory
        configurationBuilder.AddEffect<IDataContextProviderFactory, PostgresContextProviderFactory>(
            toggleable: false
        );

        configurationBuilder.HasDatabaseProvider = true;
        configurationBuilder.HasDataProvider = true;

        var promoted =
            configurationBuilder as TraxEffectBuilderWithData
            ?? new TraxEffectBuilderWithData(configurationBuilder);
        promoted.DataContextLoggingEffectEnabled = true;
        return promoted;
    }
}
