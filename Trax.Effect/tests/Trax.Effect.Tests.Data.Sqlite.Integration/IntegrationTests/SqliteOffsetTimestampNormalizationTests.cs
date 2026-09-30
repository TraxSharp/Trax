using DbUp;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Trax.Effect.Data.Sqlite.Utils;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>
/// SQLite compares the text of a <c>DateTimeOffset</c> column byte by byte. Trax now writes it as
/// fixed-width UTC text, but a row written before that holds EF's default text, whose fraction is
/// as short as it can be (<c>12:00:00.12+00:00</c>). That still sorts correctly against a
/// different instant, but against the same instant in the fixed-width form it compares as less, so
/// a lease or a draft's age reads one tick earlier than it is at the exact boundary. The migrator
/// rewrites those rows into the fixed-width form.
/// </summary>
[TestFixture]
public class SqliteOffsetTimestampNormalizationTests
{
    [Test]
    public void A_row_written_before_the_fixed_width_form_ties_with_the_same_instant_after_migration()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"trax_offset_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";
        try
        {
            var before = DeployChanges
                .To.SqliteDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(
                    typeof(DatabaseMigrator).Assembly,
                    name => Number(name) <= 15
                )
                .WithTransactionPerScript()
                .LogToNowhere()
                .Build()
                .PerformUpgrade();
            before.Successful.Should().BeTrue(before.Error?.ToString());

            Exec(
                connectionString,
                "INSERT INTO effect_claim (effect_key, owner_token, lease_expires_at, created_at) VALUES "
                    + "('short-fraction', 'a', '2026-09-29 12:00:00.12+00:00', '2026-09-29 11:00:00+00:00'),"
                    + "('non-utc-offsets', 'b', '2026-09-29 08:00:00.5+04:00', '2026-09-29 07:59:59-04:00');"
                    + "INSERT INTO snapshot_draft (id, user_key, machine, version, state, concurrency_token, updated_at) "
                    + "VALUES ('d', 'u', 'm', 1, 's', 't', '2026-09-29 12:00:00+00:00');"
            );

            DatabaseMigrator.Migrate(connectionString).Wait();

            Query(
                    connectionString,
                    "SELECT lease_expires_at FROM effect_claim WHERE lease_expires_at < "
                        + "'2026-09-29 12:00:00.1200000+00:00' AND effect_key = 'short-fraction';"
                )
                .Should()
                .BeEmpty("a lease expiring at the instant it is compared with has not expired yet");

            Query(
                    connectionString,
                    "SELECT effect_key || ' ' || lease_expires_at || ' ' || created_at "
                        + "FROM effect_claim ORDER BY effect_key;"
                )
                .Should()
                .Equal(
                    "non-utc-offsets 2026-09-29 04:00:00.5000000+00:00 2026-09-29 11:59:59.0000000+00:00",
                    "short-fraction 2026-09-29 12:00:00.1200000+00:00 2026-09-29 11:00:00.0000000+00:00"
                );
            Query(connectionString, "SELECT updated_at FROM snapshot_draft;")
                .Should()
                .Equal("2026-09-29 12:00:00.0000000+00:00");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
                File.Delete(dbPath);
        }
    }

    private static int Number(string resourceName)
    {
        var file = resourceName[(resourceName.IndexOf(".Migrations.") + ".Migrations.".Length)..];
        return int.Parse(file[..file.IndexOf('_')]);
    }

    private static void Exec(string connectionString, string sql)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static List<string> Query(string connectionString, string sql)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var values = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            values.Add(reader.GetString(0));
        return values;
    }
}
