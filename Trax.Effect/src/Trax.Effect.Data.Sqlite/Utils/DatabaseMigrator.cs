using DbUp;
using DbUp.Engine;

namespace Trax.Effect.Data.Sqlite.Utils;

/// <summary>
/// Applies embedded SQL migrations to a SQLite database using DbUp.
/// </summary>
public static class DatabaseMigrator
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

    /// <summary>
    /// Applies every embedded SQLite migration script not yet recorded in the DbUp journal, in
    /// name order. Runs synchronously; the returned task is already complete.
    /// </summary>
    /// <param name="connectionString">SQLite connection string for the target database.</param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// <c>UseSqlite</c> calls this during service registration unless migrations are skipped. The
    /// failing exception is also written to standard output before it is rethrown.
    /// </remarks>
    /// <exception cref="Exception">The script that failed, as reported by DbUp, is rethrown unchanged.</exception>
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
