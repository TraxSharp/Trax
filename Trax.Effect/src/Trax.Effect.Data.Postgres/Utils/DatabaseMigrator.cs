using DbUp;
using DbUp.Engine;
using LanguageExt;
using Npgsql;

namespace Trax.Effect.Data.Postgres.Utils;

/// <summary>
/// Provides functionality for migrating the PostgreSQL database schema for the Trax.Effect.Data.Postgres system.
/// </summary>
/// <remarks>
/// The DatabaseMigrator class is responsible for ensuring that the PostgreSQL database schema
/// is up-to-date with the latest version of the Trax.Effect.Data.Postgres system.
///
/// This class:
/// 1. Creates the necessary schema if it doesn't exist
/// 2. Applies embedded SQL migration scripts in order
/// 3. Tracks applied migrations in a migrations table
///
/// The migration process is handled by the DbUp library, which provides a robust
/// framework for database migrations with features like versioning, script ordering,
/// and transaction support.
///
/// This class is typically used when the application starts up to ensure that
/// the database schema is compatible with the current version of the application.
/// </remarks>
public class DatabaseMigrator
{
    /// <summary>
    /// Creates a DbUp upgrade engine configured with embedded SQL scripts.
    /// </summary>
    /// <param name="connectionString">The connection string to the PostgreSQL database</param>
    /// <returns>A configured DbUp upgrade engine</returns>
    /// <remarks>
    /// This method creates a new DbUp upgrade engine that is configured to:
    /// 1. Target a PostgreSQL database using the specified connection string
    /// 2. Track applied migrations in a "migrations" table in the "trax" schema
    /// 3. Use SQL scripts embedded in the assembly as migration sources
    /// 4. Log migration operations to the trace output
    ///
    /// The upgrade engine is used by the Migrate method to apply pending migrations
    /// to the database.
    /// </remarks>
    public static UpgradeEngine CreateEngineWithEmbeddedScripts(string connectionString) =>
        DeployChanges
            .To.PostgresqlDatabase(connectionString)
            .JournalToPostgresqlTable("trax", "migrations")
            .WithScriptsEmbeddedInAssembly(typeof(AssemblyMarker).Assembly)
            .LogToTrace()
            .Build();

    /// <summary>
    /// Migrates the PostgreSQL database to the latest schema version.
    /// </summary>
    /// <param name="connectionString">The connection string to the PostgreSQL database</param>
    /// <returns>A task representing the asynchronous operation</returns>
    /// <exception cref="Exception">Thrown if the migration fails</exception>
    /// <remarks>
    /// This method performs the following steps:
    /// 1. Creates the "trax" schema if it doesn't exist
    /// 2. Reloads PostgreSQL types to ensure enum mappings are up-to-date
    /// 3. Creates a DbUp upgrade engine using the CreateEngineWithEmbeddedScripts method
    /// 4. Applies any pending migrations to the database
    /// 5. Throws an exception if the migration fails
    ///
    /// This method is typically called when the application starts up, such as
    /// in the AddPostgresEffect extension method in ServiceExtensions.
    /// </remarks>
    public static async Task Migrate(string connectionString)
    {
        try
        {
            // Create the schema and reload types
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await connection.ReloadTypesAsync();

                var command = new NpgsqlCommand(
                    "create schema if not exists trax;",
                    connection
                );
                await command.ExecuteNonQueryAsync();
            }

            // Apply migrations
            var result = CreateEngineWithEmbeddedScripts(connectionString).PerformUpgrade();

            // Check for migration errors
            if (result.Successful == false)
                result.Error.Rethrow();
        }
        catch (Exception e)
        {
            Console.WriteLine(
                $"Caught Exception ({e.GetType()}) while attempting to migrate Trax.Core database: {e}"
            );
            throw;
        }
    }
}
