using DbUp;
using DbUp.Engine;

namespace Trax.Effect.Data.Sqlite.Utils;

/// <summary>
/// Applies embedded SQL migrations to a SQLite database using DbUp.
/// </summary>
public class DatabaseMigrator
{
    public static UpgradeEngine CreateEngineWithEmbeddedScripts(string connectionString) =>
        DeployChanges
            .To.SQLiteDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(typeof(AssemblyMarker).Assembly)
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
