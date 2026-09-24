using DbUp;
using DbUp.Engine;

namespace Trax.Effect.Data.Sqlite.Utils;

/// <summary>
/// Applies embedded SQL migrations to a SQLite database using DbUp.
/// </summary>
public class DatabaseMigrator
{
    /// <remarks>
    /// Each script runs in its own transaction, together with its journal row. SQLite has no
    /// <c>ADD COLUMN IF NOT EXISTS</c>, so a script that added a column and then failed, or whose
    /// process died before DbUp journaled it, would fail on "duplicate column name" at every
    /// later startup. SQLite DDL is transactional, so a script either lands whole with its
    /// journal row or not at all.
    /// </remarks>
    public static UpgradeEngine CreateEngineWithEmbeddedScripts(string connectionString) =>
        DeployChanges
            .To.SqliteDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(AssemblyMarker).Assembly)
            .WithTransactionPerScript()
            .LogToTrace()
            .Build();

    public static Task Migrate(string connectionString)
    {
        try
        {
            var result = CreateEngineWithEmbeddedScripts(connectionString).PerformUpgrade();

            if (!result.Successful)
                throw result.Error;
        }
        catch (Exception e)
        {
            Console.WriteLine(
                $"Caught Exception ({e.GetType()}) while attempting to migrate SQLite database: {e}"
            );
            throw;
        }

        return Task.CompletedTask;
    }
}
