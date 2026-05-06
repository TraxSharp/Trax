using FluentAssertions;
using Trax.Effect.Data.Sqlite.Utils;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

[TestFixture]
public class SqliteDatabaseMigratorTests
{
    [Test]
    public async Task Migrate_FreshTempDatabase_AppliesEmbeddedScripts()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"trax_migrator_ok_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        try
        {
            await DatabaseMigrator.Migrate(connectionString);

            File.Exists(dbPath).Should().BeTrue();
            new FileInfo(dbPath).Length.Should().BeGreaterThan(0);
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    [Test]
    public async Task Migrate_InvalidConnectionString_Rethrows()
    {
        // A path under a non-writable directory triggers an SQLite IO failure during PerformUpgrade.
        // DbUp captures the exception, sets Successful = false, and Migrator rethrows from the
        // !Successful branch — the catch block then logs and rethrows again, covering lines 25, 27-32.
        var invalidPath = "/proc/this-cannot-be-written/migrator-fail.db";
        var connectionString = $"Data Source={invalidPath}";

        var act = async () => await DatabaseMigrator.Migrate(connectionString);

        await act.Should().ThrowAsync<Exception>();
    }

    [Test]
    public void CreateEngineWithEmbeddedScripts_ReturnsEngine()
    {
        var dbPath = Path.Combine(
            Path.GetTempPath(),
            $"trax_migrator_engine_{Guid.NewGuid():N}.db"
        );
        try
        {
            var engine = DatabaseMigrator.CreateEngineWithEmbeddedScripts($"Data Source={dbPath}");

            engine.Should().NotBeNull();
        }
        finally
        {
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }
}
